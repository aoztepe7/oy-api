using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Billing;
using Oy.Api.Features.Classroom;

namespace Oy.Api.Infrastructure.Push;

/// <summary>
/// Daily reminders, sent once a day from 09:00 Turkey time: guardians' upcoming / overdue payments, the finance
/// team's payment summary, to-dos due today. Every reminder has a key, so a restart or a second run sends nothing twice.
/// </summary>
public sealed class ReminderWorker(IServiceScopeFactory scopes, ILogger<ReminderWorker> logger) : BackgroundService
{
    private static readonly TimeOnly From = new(9, 0);
    private static readonly TimeOnly Until = new(21, 0); // a deploy late in the evening waits for the next morning
    private static readonly CultureInfo Tr = new("tr-TR");
    private DateOnly lastRun;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
        do
        {
            try
            {
                var now = ClassroomEndpoints.LocalNow();
                var (today, time) = (DateOnly.FromDateTime(now), TimeOnly.FromDateTime(now));
                if (today != lastRun && time >= From && time < Until)
                {
                    await RunAsync(today, stoppingToken);
                    lastRun = today;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Reminder run failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunAsync(DateOnly today, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var notifier = sp.GetRequiredService<Notifier>();
        var settings = await BillingEndpoints.SettingsAsync(db, ct);
        var dues = await BillingEndpoints.DuesAsync(db, settings, null, ct);

        // Guardians: one reminder when an instalment comes close, one when it is late.
        foreach (var d in dues.Where(d => d.Overdue || d.DueSoon))
        {
            var guardians = await notifier.GuardiansOfAsync([d.EnrollmentId], ct);
            if (guardians.Count == 0) continue;
            var left = Money(d.Amount - d.Paid);
            var what = $"{d.ChildName} · {d.ProgramTitle}{(d.FeePeriod == PricePeriod.Monthly ? $" · {d.PeriodLabel}" : "")}";
            if (d.Overdue)
                await notifier.SendAsync(guardians, NotificationKind.PaymentOverdue, "Ödemesi geciken taksit",
                    $"{what}: {left} ödemenin son günü {Date(d.DueDate)} idi. Ödediyseniz bu mesajı dikkate almayın.",
                    "/veli/hesap", $"due:{d.EnrollmentId}:{d.Period:yyyy-MM}:overdue", ct);
            else
                await notifier.SendAsync(guardians, NotificationKind.PaymentDue, "Yaklaşan ödeme",
                    $"{what}: {left}, son ödeme günü {Date(d.DueDate)}.",
                    "/veli/hesap", $"due:{d.EnrollmentId}:{d.Period:yyyy-MM}:soon", ct);
        }

        // Finance: what needs attention today.
        var month = Domain.Billing.MonthStart(today);
        var obligations = await BillingEndpoints.ObligationScheduleAsync(db, settings, month, month.AddMonths(1), ct);
        var parts = new List<string>();
        if (dues.Count(d => d.Overdue) is var lateDues and > 0) parts.Add($"{lateDues} gecikmiş veli ödemesi");
        if (dues.Count(d => d.DueSoon) is var soonDues and > 0) parts.Add($"{soonDues} yaklaşan veli ödemesi");
        if (obligations.Count(o => o.Overdue) is var lateOb and > 0) parts.Add($"{lateOb} gecikmiş SGK / gider ödemesi");
        if (obligations.Count(o => o.DueSoon) is var soonOb and > 0) parts.Add($"{soonOb} yaklaşan SGK / gider ödemesi");
        if (parts.Count > 0)
            await notifier.SendAsync(await notifier.StaffWithAsync(Permissions.FinanceRead, ct), NotificationKind.PaymentDigest,
                "Bugünün ödeme özeti", string.Join(", ", parts) + ".", "/odeme-takibi", $"digest:{today:yyyy-MM-dd}", ct);

        // To-dos due today: their owner and the people they are assigned to.
        var todos = await db.Todos.AsNoTracking().Where(t => t.CompletedAt == null && t.DueDate == today)
            .Select(t => new { t.Id, t.Title, t.CreatedById, Assignees = t.Assignees.Select(a => a.UserId).ToList() })
            .ToListAsync(ct);
        foreach (var t in todos)
        {
            var people = t.Assignees.Count > 0 ? t.Assignees : t.CreatedById is { } c ? [c] : [];
            await notifier.SendAsync(people, NotificationKind.TodoDue, "Bugün son gün", t.Title,
                $"/yapilacaklar?is={t.Id}", $"todo-due:{t.Id}:{today:yyyy-MM-dd}", ct);
        }
    }

    internal static string Money(decimal value) => value.ToString("N0", Tr) + " ₺";
    internal static string Date(DateOnly d) => d.ToString("d MMMM", Tr);
}
