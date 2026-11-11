using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;

namespace Oy.Api.Infrastructure.Push;

/// <summary>
/// Writes notifications into users' inboxes and queues them for push to their phones. Call it after the change it is
/// about has been saved; it never fails the request (a notification is not worth an error for the user).
/// </summary>
public sealed class Notifier(AppDbContext db, PushQueue queue, ILogger<Notifier> logger)
{
    public async Task SendAsync(IEnumerable<Guid> userIds, NotificationKind kind, string title, string body, string url,
        string key = "", CancellationToken ct = default)
    {
        try
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return;
            var recipients = await db.Users.AsNoTracking()
                .Where(u => ids.Contains(u.Id) && u.IsActive)
                .Where(u => !db.NotificationMutes.Any(m => m.UserId == u.Id && m.Kind == kind))
                .Where(u => key == "" || !db.Notifications.Any(n => n.UserId == u.Id && n.Key == key))
                .Select(u => u.Id).ToListAsync(ct);
            if (recipients.Count == 0) return;

            var rows = recipients.Select(id => new Notification
            {
                UserId = id, Kind = kind, Title = Clip(title, 120), Body = Clip(body, 500), Url = Clip(url, 300), Key = key,
            }).ToList();
            db.Notifications.AddRange(rows);
            await db.SaveChangesAsync(ct);
            queue.Enqueue(rows.Select(r => r.Id).ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Notification {Kind} could not be created", kind);
        }
    }

    // ----- Recipients -----

    /// <summary>Active staff whose role has the permission.</summary>
    public Task<List<Guid>> StaffWithAsync(string permission, CancellationToken ct = default) =>
        db.Users.AsNoTracking()
            .Where(u => u.Kind == UserKind.Staff && u.IsActive && u.Role != null && u.Role.Permissions.Contains(permission))
            .Select(u => u.Id).ToListAsync(ct);

    /// <summary>Guardian accounts following these enrollments (children).</summary>
    public Task<List<Guid>> GuardiansOfAsync(IEnumerable<Guid> enrollmentIds, CancellationToken ct = default)
    {
        var ids = enrollmentIds.Distinct().ToList();
        return db.GuardianLinks.AsNoTracking().Where(l => ids.Contains(l.EnrollmentId)).Select(l => l.UserId).Distinct().ToListAsync(ct);
    }

    /// <summary>Guardians with an active child: in the program, or anywhere (null).</summary>
    public Task<List<Guid>> GuardiansOfProgramAsync(Guid? programId, CancellationToken ct = default) =>
        db.GuardianLinks.AsNoTracking()
            .Where(l => l.Enrollment.Status == EnrollmentStatus.Active && (programId == null || l.Enrollment.ProgramId == programId))
            .Select(l => l.UserId).Distinct().ToListAsync(ct);

    /// <summary>"Rüzgar Öztepe" → "Rüzgar" (notification titles stay short).</summary>
    public static string FirstName(string fullName) => fullName.Trim().Split(' ', 2)[0];

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Notification ids waiting to be pushed (in memory; a restart only loses the push, not the inbox entry).</summary>
public sealed class PushQueue
{
    private readonly Channel<Guid[]> channel = Channel.CreateUnbounded<Guid[]>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid[] notificationIds) => channel.Writer.TryWrite(notificationIds);

    public ChannelReader<Guid[]> Reader => channel.Reader;
}

/// <summary>Sends queued notifications to every signed-in app device of their user.</summary>
public sealed class PushWorker(PushQueue queue, FcmSender fcm, IServiceScopeFactory scopes, ILogger<PushWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ids in queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!fcm.IsConfigured) continue;
            try { await SendAsync(ids, stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Push batch failed"); }
        }
    }

    private async Task SendAsync(Guid[] ids, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = await db.Notifications.AsNoTracking().Where(n => ids.Contains(n.Id))
            .Select(n => new { n.UserId, n.Title, n.Body, n.Url }).ToListAsync(ct);
        var users = notifications.Select(n => n.UserId).Distinct().ToList();
        var now = DateTime.UtcNow;
        var devices = await db.UserSessions.Where(s => users.Contains(s.UserId) && s.RevokedAt == null && s.ExpiresAt > now && s.PushToken != "")
            .ToListAsync(ct);
        if (devices.Count == 0) return;
        // App icon badge = unread notifications.
        var unread = await db.Notifications.Where(n => users.Contains(n.UserId) && n.ReadAt == null)
            .GroupBy(n => n.UserId).Select(g => new { UserId = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        var changed = false;
        foreach (var n in notifications)
        {
            foreach (var device in devices.Where(d => d.UserId == n.UserId && d.PushToken != "").DistinctBy(d => d.PushToken).ToList())
            {
                var result = await fcm.SendAsync(new PushMessage(device.PushToken, n.Title, n.Body, n.Url, unread.GetValueOrDefault(n.UserId)), ct);
                if (result == PushResult.InvalidToken)
                {
                    foreach (var d in devices.Where(d => d.PushToken == device.PushToken)) d.PushToken = "";
                    changed = true;
                }
            }
        }
        if (changed) await db.SaveChangesAsync(ct);
    }
}
