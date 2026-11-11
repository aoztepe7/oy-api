using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;
using Oy.Api.Infrastructure.Integrations;

namespace Oy.Api.Features.Finance;

public record ExpenseInput(Guid ProgramId, DateOnly Date, [Required, MaxLength(100)] string Category,
    [MaxLength(500)] string? Description, [Range(0.01, 10_000_000)] decimal Amount);

public record ExpenseDto(Guid Id, Guid ProgramId, string ProgramTitle, DateOnly Date, string Category, string Description, decimal Amount);

public record FinanceTotals(decimal Revenue, decimal StaffCost, decimal Expenses, decimal Profit, decimal Margin);

public record ProgramFinanceDto(Guid ProgramId, string Title, IReadOnlyList<ProgramMonthFinance> Months, FinanceTotals Totals);

public record FinanceSummaryRow(Guid ProgramId, string Title, ProgramType Type, decimal Revenue, decimal StaffCost, decimal Expenses, decimal Profit, decimal Margin);

public record PaymentInput(Guid EnrollmentId, [Range(0.01, 10_000_000)] decimal Amount, PaymentMethod Method, DateTime? PaidAt, [MaxLength(500)] string? Note,
    /// <summary>Instalment paid (1st of the month); null = applied to the oldest unpaid instalments.</summary>
    DateOnly? Period = null);

public record PaymentDto(Guid Id, Guid EnrollmentId, string ChildName, string ProgramTitle, decimal Amount, PaymentMethod Method,
    DateTime PaidAt, string Note, InvoiceStatus? InvoiceStatus, string? InvoiceNumber);

public record InvoiceDto(Guid Id, Guid PaymentId, string Number, InvoiceStatus Status, string Provider, string Error, DateTime CreatedAt, DateTime? SentAt);

public static class FinanceEndpoints
{
    public static void MapFinance(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Finance");
        var read = Permissions.FinanceRead;
        var write = Permissions.FinanceWrite;

        // Expenses
        // One program's expenses belong to its profitability view; all expenses to the finance page.
        group.MapGet("/expenses", async (Guid? programId, DateOnly? from, DateOnly? to, AppDbContext db, ClaimsPrincipal me) =>
        {
            if (programId is null && !me.Can(read)) return Results.Forbid();
            var q = db.Expenses.AsNoTracking();
            if (programId is not null) q = q.Where(e => e.ProgramId == programId);
            if (from is not null) q = q.Where(e => e.Date >= from);
            if (to is not null) q = q.Where(e => e.Date <= to);
            return Results.Ok(await q.OrderByDescending(e => e.Date)
                .Select(e => new ExpenseDto(e.Id, e.ProgramId, e.Program.Title, e.Date, e.Category, e.Description, e.Amount)).ToListAsync());
        }).RequireAnyPermission(read, Permissions.ProgramsFinance);

        group.MapPost("/expenses", async (ExpenseInput input, AppDbContext db) =>
        {
            var program = await db.Programs.FindAsync(input.ProgramId);
            if (program is null) return new Errors().Check(false, "programId", "Eğitim bulunamadı.").ToResult();
            var e = new Expense
            {
                ProgramId = input.ProgramId, Date = input.Date, Category = input.Category.Trim(),
                Description = input.Description?.Trim() ?? "", Amount = input.Amount,
            };
            db.Expenses.Add(e);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/expenses/{e.Id}", new ExpenseDto(e.Id, e.ProgramId, program.Title, e.Date, e.Category, e.Description, e.Amount));
        }).RequireAuthorization(write);

        group.MapDelete("/expenses/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Expenses.Where(e => e.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(write);

        // Profitability
        group.MapGet("/programs/{id:guid}/finance", async (Guid id, AppDbContext db) =>
        {
            var p = await LoadForFinance(db).FirstOrDefaultAsync(x => x.Id == id);
            if (p is null) return Results.NotFound();
            var months = FinanceCalculator.ProgramFinance(p);
            return Results.Ok(new ProgramFinanceDto(p.Id, p.Title, months, Totals(months)));
        }).RequireAnyPermission(read, Permissions.ProgramsFinance);

        group.MapGet("/finance/summary", async (AppDbContext db) =>
        {
            var programs = await LoadForFinance(db).OrderByDescending(p => p.StartDate).ToListAsync();
            return programs.Select(p =>
            {
                var t = Totals(FinanceCalculator.ProgramFinance(p));
                return new FinanceSummaryRow(p.Id, p.Title, p.Type, t.Revenue, t.StaffCost, t.Expenses, t.Profit, t.Margin);
            });
        }).RequireAuthorization(read);

        // Payments
        // One enrollment's payments are part of its fee details; the full list is the finance page.
        group.MapGet("/payments", async (Guid? enrollmentId, AppDbContext db, ClaimsPrincipal me) =>
        {
            if (enrollmentId is null && !me.Can(read)) return Results.Forbid();
            var q = db.Payments.AsNoTracking();
            if (enrollmentId is not null) q = q.Where(p => p.EnrollmentId == enrollmentId);
            return Results.Ok(await ProjectPayments(q.OrderByDescending(p => p.PaidAt)).Take(500).ToListAsync());
        }).RequireAnyPermission(read, Permissions.EnrollmentsFees);

        group.MapPost("/payments", async (PaymentInput input, AppDbContext db, InvoiceService invoices, CancellationToken ct) =>
        {
            if (!await db.Enrollments.AnyAsync(e => e.Id == input.EnrollmentId, ct))
                return new Errors().Check(false, "enrollmentId", "Kayıt bulunamadı.").ToResult();

            var payment = new Payment
            {
                EnrollmentId = input.EnrollmentId, Amount = input.Amount, Method = input.Method,
                PaidAt = input.PaidAt?.ToUniversalTime() ?? DateTime.UtcNow, Note = input.Note?.Trim() ?? "",
                Period = input.Period is { } period ? Domain.Billing.MonthStart(period) : null,
            };
            db.Payments.Add(payment);
            if (await invoices.AutoIssueEnabledAsync(ct)) await invoices.QueueAsync(payment.Id, ct);
            await db.SaveChangesAsync(ct);
            var dto = await ProjectPayments(db.Payments.AsNoTracking().Where(p => p.Id == payment.Id)).FirstAsync(ct);
            return Results.Created($"/api/admin/payments/{payment.Id}", dto);
        }).RequireAuthorization(write);

        group.MapDelete("/payments/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            if (await db.Invoices.AnyAsync(i => i.PaymentId == id && i.Status == InvoiceStatus.Sent))
                return ApiResults.Conflict("Faturası kesilmiş ödeme silinemez. İade/iptal faturası gerekir.");
            await db.Invoices.Where(i => i.PaymentId == id).ExecuteDeleteAsync();
            return await db.Payments.Where(p => p.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        }).RequireAuthorization(write);

        // Invoices
        group.MapGet("/invoices", async (AppDbContext db) =>
            await db.Invoices.AsNoTracking().OrderByDescending(i => i.CreatedAt).Take(500)
                .Select(i => new InvoiceDto(i.Id, i.PaymentId, i.Number, i.Status, i.Provider, i.Error, i.CreatedAt, i.SentAt)).ToListAsync())
            .RequireAuthorization(read);

        group.MapPost("/payments/{id:guid}/invoice", async (Guid id, AppDbContext db, InvoiceService invoices, CancellationToken ct) =>
        {
            if (!await db.Payments.AnyAsync(p => p.Id == id, ct)) return Results.NotFound();
            var invoice = await invoices.QueueAsync(id, ct);
            await db.SaveChangesAsync(ct);
            return Results.Accepted(value: new InvoiceDto(invoice!.Id, invoice.PaymentId, invoice.Number, invoice.Status,
                invoice.Provider, invoice.Error, invoice.CreatedAt, invoice.SentAt));
        }).RequireAuthorization(write);
    }

    private static IQueryable<TrainingProgram> LoadForFinance(AppDbContext db) =>
        db.Programs.AsNoTracking()
            .Include(p => p.Enrollments).ThenInclude(e => e.DueAdjustments)
            .Include(p => p.Sessions)
            .Include(p => p.Expenses)
            .Include(p => p.Assignments).ThenInclude(a => a.Staff)
            .AsSplitQuery();

    private static FinanceTotals Totals(IReadOnlyList<ProgramMonthFinance> months)
    {
        var revenue = months.Sum(m => m.Revenue);
        var staff = months.Sum(m => m.StaffCost);
        var expenses = months.Sum(m => m.Expenses);
        var profit = revenue - staff - expenses;
        return new FinanceTotals(revenue, staff, expenses, profit, revenue == 0 ? 0 : Math.Round(profit / revenue, 4));
    }

    private static IQueryable<PaymentDto> ProjectPayments(IQueryable<Payment> q) => q.Select(p => new PaymentDto(
        p.Id, p.EnrollmentId, p.Enrollment.ChildName, p.Enrollment.Program.Title, p.Amount, p.Method, p.PaidAt, p.Note,
        p.Invoice == null ? null : p.Invoice.Status, p.Invoice == null ? null : p.Invoice.Number));
}
