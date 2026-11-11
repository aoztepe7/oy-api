using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Integrations;

namespace Oy.Api.Features.Billing;

public record BillingSettingsDto(
    decimal VatRate, int MonthlyDueDay, int ReminderDays, string ItemName, string Unit, string Note, string DefaultTaxId,
    /// <summary>An e-invoice integration is active: invoices are issued by it instead of the manual template.</summary>
    bool IntegrationActive, string? IntegrationProvider);

public record BillingSettingsInput(
    [Range(0, 100)] decimal VatRate,
    [Range(1, 31)] int MonthlyDueDay,
    [Range(0, 60)] int ReminderDays,
    [Required, MaxLength(300)] string ItemName,
    [MaxLength(30)] string? Unit,
    [MaxLength(500)] string? Note,
    [MaxLength(11)] string? DefaultTaxId);

public record DuePaymentDto(
    Guid Id, decimal Amount, PaymentMethod Method, DateTime PaidAt, string Note,
    InvoiceStatus? InvoiceStatus, string? InvoiceNumber, string? InvoiceProvider);

/// <summary>One fee instalment of an enrollment with what was paid and the invoices of those payments.</summary>
public record DueDto(
    Guid EnrollmentId, string ChildName, string ParentName, Guid ProgramId, string ProgramTitle, PricePeriod FeePeriod,
    DateOnly Period, string PeriodLabel, DateOnly DueDate, decimal Amount, decimal Paid, DueStatus Status,
    bool Overdue, bool DueSoon, bool HasBillingInfo, List<DuePaymentDto> Payments,
    /// <summary>The enrollment fee; differs from <c>Amount</c> when this instalment has a manual price.</summary>
    decimal StandardAmount,
    /// <summary>Manual amount and/or due date for this instalment.</summary>
    bool Adjusted, string AdjustmentNote,
    /// <summary>Share of the fee for the days of the month the enrollment covers (the full fee for whole months).</summary>
    decimal ProratedAmount);

/// <summary>Manual price / due date of one instalment. Both null = back to the standard.</summary>
public record AdjustDueInput(
    Guid EnrollmentId, DateOnly Period,
    [Range(0, 10_000_000)] decimal? Amount,
    DateOnly? DueDate,
    [MaxLength(500)] string? Note);

/// <summary>New fee of an enrollment from a month on; earlier months keep the old fee.</summary>
public record ChangeFeeInput([Range(0, 10_000_000)] decimal Fee, DateOnly? FromPeriod);

public record PayDueInput(
    Guid EnrollmentId, DateOnly Period,
    /// <summary>null = the remaining amount of the instalment.</summary>
    [Range(0.01, 10_000_000)] decimal? Amount,
    PaymentMethod Method, DateOnly? PaidOn, [MaxLength(500)] string? Note);

public record InvoiceDraftDto(
    Guid PaymentId, InvoiceDraft Draft, InvoiceStatus? Status, string? Number, DateOnly? IssuedOn, string? Provider, bool IntegrationActive);

public record ManualInvoiceInput([Required, MaxLength(30)] string Number, DateOnly IssuedOn);

public record BillingInfoInput(
    BillingKind Kind,
    [MaxLength(200)] string? Name,
    [MaxLength(11)] string? TaxId,
    [MaxLength(100)] string? TaxOffice,
    [MaxLength(500)] string? Address,
    [MaxLength(100)] string? District,
    [MaxLength(100)] string? City,
    [MaxLength(256)] string? Email);

public record ObligationInput(
    [Required, MaxLength(200)] string Title,
    ObligationCategory Category,
    [Range(0, 100_000_000)] decimal? Amount,
    [Range(1, 31)] int DayOfMonth,
    DateOnly StartMonth,
    DateOnly? EndMonth,
    bool IsActive,
    [MaxLength(1000)] string? Note,
    Guid? StaffId);

public record ObligationDto(
    Guid Id, string Title, ObligationCategory Category, decimal? Amount, int DayOfMonth, DateOnly StartMonth, DateOnly? EndMonth,
    bool IsActive, string Note, Guid? StaffId, string? StaffName);

/// <summary>One month of an obligation.</summary>
public record ObligationDueDto(
    Guid ObligationId, string Title, ObligationCategory Category, DateOnly Month, DateOnly DueDate, decimal? Amount,
    bool Paid, decimal? PaidAmount, DateOnly? PaidOn, string PaidNote, bool Overdue, bool DueSoon);

public record PayObligationInput(DateOnly Month, [Range(0.01, 100_000_000)] decimal Amount, DateOnly? PaidOn, [MaxLength(500)] string? Note);

/// <summary>Counts behind the sidebar warning badge and the page summary.</summary>
public record BillingAlerts(int OverdueDues, int DueSoonDues, int OverdueObligations, int DueSoonObligations, int InvoicesMissing)
{
    public int Total => OverdueDues + OverdueObligations + DueSoonObligations;
}

public static class BillingEndpoints
{
    /// <summary>Provider name of invoices marked as issued by hand in GİB e-Arşiv Portal.</summary>
    public const string ManualProvider = "Manual";

    public static void MapBilling(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Billing");
        var read = Permissions.FinanceRead;
        var write = Permissions.FinanceWrite;

        // ----- Settings -----
        group.MapGet("/billing/settings", async (AppDbContext db, IntegrationService integrations, CancellationToken ct) =>
            ToDto(await SettingsAsync(db, ct), await integrations.GetActiveAsync(IntegrationType.EInvoice, ct)))
            .RequireAuthorization(read);

        group.MapPut("/billing/settings", async (BillingSettingsInput input, AppDbContext db, IntegrationService integrations, CancellationToken ct) =>
        {
            var taxId = input.DefaultTaxId?.Trim() ?? "";
            var errors = new Errors().Check(taxId.Length == 0 || Domain.Billing.IsTaxId(taxId), "defaultTaxId", "10 haneli VKN ya da 11 haneli TCKN girin.");
            if (errors.Any) return errors.ToResult();
            var s = await db.BillingSettings.FirstOrDefaultAsync(ct);
            if (s is null) db.BillingSettings.Add(s = new BillingSettings());
            s.VatRate = input.VatRate;
            s.MonthlyDueDay = input.MonthlyDueDay;
            s.ReminderDays = input.ReminderDays;
            s.ItemName = input.ItemName.Trim();
            s.Unit = string.IsNullOrWhiteSpace(input.Unit) ? "Adet" : input.Unit.Trim();
            s.Note = input.Note?.Trim() ?? "";
            s.DefaultTaxId = taxId.Length == 0 ? "11111111111" : taxId;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(s, await integrations.GetActiveAsync(IntegrationType.EInvoice, ct)));
        }).RequireAuthorization(write);

        // ----- Fee instalments of enrollments -----
        // Instalments whose month is in [from, to], plus older unpaid ones (overdue) unless includeOverdue=false.
        group.MapGet("/billing/dues", async (DateOnly? from, DateOnly? to, Guid? programId, bool? includeOverdue, AppDbContext db, CancellationToken ct) =>
        {
            var today = Today();
            var start = Domain.Billing.MonthStart(from ?? today.AddMonths(-1));
            var end = Domain.Billing.MonthStart(to ?? today.AddMonths(1));
            var settings = await SettingsAsync(db, ct);
            var dues = await DuesAsync(db, settings, programId, ct);
            return dues
                .Where(d => (d.Period >= start && d.Period <= end) || (includeOverdue != false && d.Period < start && d.Overdue))
                .OrderBy(d => d.DueDate).ThenBy(d => d.ChildName)
                .ToList();
        }).RequireAuthorization(read);

        // "Ödendi": records a payment for the instalment (the remaining amount unless given).
        group.MapPost("/billing/dues/pay", async (PayDueInput input, AppDbContext db, InvoiceService invoices, CancellationToken ct) =>
        {
            var settings = await SettingsAsync(db, ct);
            var e = await db.Enrollments.Include(x => x.Program).Include(x => x.Payments).Include(x => x.DueAdjustments)
                .FirstOrDefaultAsync(x => x.Id == input.EnrollmentId, ct);
            if (e is null) return Results.NotFound();
            var state = Domain.Billing.Allocate(Domain.Billing.DuesOf(e, e.Program.EndDate, settings.MonthlyDueDay), e.Payments)
                .FirstOrDefault(d => d.Due.Period == input.Period);
            if (state is null) return new Errors().Check(false, "period", "Bu kayıt için böyle bir ödeme dönemi yok.").ToResult();
            var amount = input.Amount ?? state.Remaining;
            if (amount <= 0) return ApiResults.Conflict("Bu dönemin ödemesi zaten tamam.");

            var payment = new Payment
            {
                EnrollmentId = e.Id, Period = input.Period, Amount = amount, Method = input.Method,
                PaidAt = (input.PaidOn ?? Today()).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), Note = input.Note?.Trim() ?? "",
            };
            db.Payments.Add(payment);
            if (await invoices.AutoIssueEnabledAsync(ct)) await invoices.QueueAsync(payment.Id, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { paymentId = payment.Id });
        }).RequireAuthorization(write);

        // "Ödenmedi": removes the payments recorded for the instalment.
        group.MapDelete("/billing/dues", async (Guid enrollmentId, DateOnly period, AppDbContext db, CancellationToken ct) =>
        {
            var payments = db.Payments.Where(p => p.EnrollmentId == enrollmentId && p.Period == period);
            if (!await payments.AnyAsync(ct))
                return ApiResults.Conflict("Bu döneme kayıtlı ödeme yok. Dönemsiz girilen ödemeleri kaydın Ödemeler bölümünden silebilirsiniz.");
            if (await payments.AnyAsync(p => p.Invoice != null && p.Invoice.Status == InvoiceStatus.Sent, ct))
                return ApiResults.Conflict("Faturası kesilmiş ödeme geri alınamaz. Önce faturayı iptal edin (GİB’de) ve fatura kaydını kaldırın.");
            await db.Invoices.Where(i => i.Payment.EnrollmentId == enrollmentId && i.Payment.Period == period).ExecuteDeleteAsync(ct);
            await payments.ExecuteDeleteAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(write);

        // Manual price (discount, part of a month) and/or due date of one instalment.
        group.MapPut("/billing/dues/adjust", async (AdjustDueInput input, AppDbContext db, CancellationToken ct) =>
        {
            var e = await db.Enrollments.Include(x => x.Program).Include(x => x.DueAdjustments)
                .FirstOrDefaultAsync(x => x.Id == input.EnrollmentId, ct);
            if (e is null) return Results.NotFound();
            var period = Domain.Billing.MonthStart(input.Period);
            var first = Domain.Billing.MonthStart(e.StartDate);
            var last = e.FeePeriod == PricePeriod.OneTime ? first : Domain.Billing.MonthStart(e.EndDate ?? e.Program.EndDate);
            if (period < first || period > last) return new Errors().Check(false, "period", "Bu ay kaydın dönemi dışında.").ToResult();

            var existing = e.DueAdjustments.FirstOrDefault(a => a.Period == period);
            if (input.Amount is null && input.DueDate is null)
            {
                if (existing is not null) db.DueAdjustments.Remove(existing);
            }
            else
            {
                var a = existing ?? new DueAdjustment { EnrollmentId = e.Id, Period = period };
                if (existing is null) db.DueAdjustments.Add(a);
                a.Amount = input.Amount;
                a.DueDate = input.DueDate;
                a.Note = input.Note?.Trim() ?? "";
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(write, Permissions.EnrollmentsFees);

        // Discount for the rest of the program: the enrollment fee changes from the given month on;
        // earlier months without a manual price keep the old fee (so what was already due does not change).
        group.MapPut("/billing/enrollments/{id:guid}/fee", async (Guid id, ChangeFeeInput input, AppDbContext db, CancellationToken ct) =>
        {
            var e = await db.Enrollments.Include(x => x.DueAdjustments).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            var from = input.FromPeriod is { } f ? Domain.Billing.MonthStart(f) : Domain.Billing.MonthStart(e.StartDate);
            if (e.FeePeriod == PricePeriod.Monthly && e.Fee != input.Fee)
            {
                for (var m = Domain.Billing.MonthStart(e.StartDate); m < from; m = m.AddMonths(1))
                {
                    if (e.DueAdjustments.Any(a => a.Period == m)) continue;
                    db.DueAdjustments.Add(new DueAdjustment { EnrollmentId = e.Id, Period = m, Amount = e.Fee, Note = "Önceki ücret" });
                }
            }
            e.Fee = input.Fee;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(write, Permissions.EnrollmentsFees);

        // ----- Invoices of payments -----
        group.MapGet("/payments/{id:guid}/invoice-draft", async (Guid id, AppDbContext db, IntegrationService integrations, CancellationToken ct) =>
        {
            var p = await db.Payments.AsNoTracking().Include(x => x.Invoice).Include(x => x.Enrollment).ThenInclude(e => e.Program)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            var settings = await SettingsAsync(db, ct);
            var integration = await integrations.GetActiveAsync(IntegrationType.EInvoice, ct);
            var draft = Domain.Billing.Draft(p, settings, p.Invoice?.IssuedOn ?? Today());
            return Results.Ok(new InvoiceDraftDto(p.Id, draft, p.Invoice?.Status, p.Invoice?.Number, p.Invoice?.IssuedOn, p.Invoice?.Provider, integration is not null));
        }).RequireAuthorization(read);

        // "Faturayı kestim": the invoice was issued by hand in GİB e-Arşiv Portal from the template.
        group.MapPost("/payments/{id:guid}/invoice/manual", async (Guid id, ManualInvoiceInput input, AppDbContext db, CancellationToken ct) =>
        {
            var p = await db.Payments.Include(x => x.Invoice).Include(x => x.Enrollment).ThenInclude(e => e.Program)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            if (p.Invoice is { Status: InvoiceStatus.Sent } sent && sent.Provider != ManualProvider)
                return ApiResults.Conflict("Bu ödemenin faturası entegrasyonla kesildi.");
            var settings = await SettingsAsync(db, ct);
            var draft = Domain.Billing.Draft(p, settings, input.IssuedOn);
            var invoice = p.Invoice ?? new Invoice { PaymentId = p.Id };
            if (p.Invoice is null) db.Invoices.Add(invoice);
            invoice.Provider = ManualProvider;
            invoice.Status = InvoiceStatus.Sent;
            invoice.Number = input.Number.Trim().ToUpperInvariant();
            invoice.IssuedOn = input.IssuedOn;
            invoice.SentAt = DateTime.UtcNow;
            invoice.Error = "";
            invoice.VatRate = draft.VatRate;
            invoice.NetAmount = draft.UnitPrice * draft.Quantity;
            invoice.VatAmount = draft.VatAmount;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(write);

        // Undo a manual "issued" mark (typo, wrong payment). Integration invoices stay.
        group.MapDelete("/payments/{id:guid}/invoice", async (Guid id, AppDbContext db, CancellationToken ct) =>
        {
            var removed = await db.Invoices
                .Where(i => i.PaymentId == id && (i.Provider == ManualProvider || i.Status != InvoiceStatus.Sent))
                .ExecuteDeleteAsync(ct);
            return removed > 0 ? Results.NoContent() : ApiResults.Conflict("Entegrasyonla kesilmiş fatura buradan kaldırılamaz.");
        }).RequireAuthorization(write);

        // ----- Billing info of an enrollment -----
        group.MapPut("/enrollments/{id:guid}/billing", async (Guid id, BillingInfoInput input, AppDbContext db, CancellationToken ct) =>
        {
            var e = await db.Enrollments.FindAsync([id], ct);
            if (e is null) return Results.NotFound();
            var name = input.Name?.Trim() ?? "";
            var taxId = input.TaxId?.Trim() ?? "";
            if (name.Length == 0)
            {
                e.Billing = null; // cleared: invoices go to the parent's name
                await db.SaveChangesAsync(ct);
                return Results.NoContent();
            }
            var corporate = input.Kind == BillingKind.Corporate;
            var errors = new Errors()
                .Check(taxId.Length == 0 || (corporate ? taxId.Length == 10 && taxId.All(char.IsAsciiDigit) : EnrollmentForms.IsNationalId(taxId)),
                    "taxId", corporate ? "10 haneli vergi kimlik numarasını girin." : "Geçerli bir T.C. kimlik numarası girin.")
                .Check(!corporate || taxId.Length > 0, "taxId", "Şirket faturası için VKN gerekli.")
                .Check(!corporate || !string.IsNullOrWhiteSpace(input.TaxOffice), "taxOffice", "Vergi dairesini girin.");
            if (errors.Any) return errors.ToResult();
            e.Billing = new BillingInfo
            {
                Kind = input.Kind, Name = name, TaxId = taxId, TaxOffice = input.TaxOffice?.Trim() ?? "",
                Address = input.Address?.Trim() ?? "", District = input.District?.Trim() ?? "", City = input.City?.Trim() ?? "",
                Email = input.Email?.Trim() ?? "",
            };
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.EnrollmentsWrite);

        // ----- Recurring payments of the school (SGK, salaries …) -----
        group.MapGet("/obligations", async (AppDbContext db, CancellationToken ct) =>
            await db.Obligations.AsNoTracking().OrderBy(o => o.DayOfMonth).ThenBy(o => o.Title)
                .Select(o => new ObligationDto(o.Id, o.Title, o.Category, o.Amount, o.DayOfMonth, o.StartMonth, o.EndMonth, o.IsActive, o.Note,
                    o.StaffId, o.Staff == null ? null : o.Staff.FullName))
                .ToListAsync(ct))
            .RequireAuthorization(read);

        group.MapPost("/obligations", async (ObligationInput input, AppDbContext db, CancellationToken ct) =>
            await SaveObligationAsync(new Obligation { Title = "" }, input, db, isNew: true, ct)).RequireAuthorization(write);

        group.MapPut("/obligations/{id:guid}", async (Guid id, ObligationInput input, AppDbContext db, CancellationToken ct) =>
            await db.Obligations.FindAsync([id], ct) is { } o ? await SaveObligationAsync(o, input, db, isNew: false, ct) : Results.NotFound())
            .RequireAuthorization(write);

        group.MapDelete("/obligations/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
            await db.Obligations.Where(o => o.Id == id).ExecuteDeleteAsync(ct) > 0 ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(write);

        group.MapGet("/obligations/schedule", async (DateOnly? from, DateOnly? to, AppDbContext db, CancellationToken ct) =>
        {
            var today = Today();
            var settings = await SettingsAsync(db, ct);
            var start = Domain.Billing.MonthStart(from ?? today.AddMonths(-1));
            var end = Domain.Billing.MonthStart(to ?? today.AddMonths(1));
            return (await ObligationScheduleAsync(db, settings, start, end, ct)).OrderBy(d => d.DueDate).ThenBy(d => d.Title).ToList();
        }).RequireAuthorization(read);

        group.MapPost("/obligations/{id:guid}/payments", async (Guid id, PayObligationInput input, AppDbContext db, CancellationToken ct) =>
        {
            var o = await db.Obligations.FindAsync([id], ct);
            if (o is null) return Results.NotFound();
            var month = Domain.Billing.MonthStart(input.Month);
            var existing = await db.ObligationPayments.FirstOrDefaultAsync(p => p.ObligationId == id && p.Month == month, ct);
            var p = existing ?? new ObligationPayment { ObligationId = id, Month = month };
            if (existing is null) db.ObligationPayments.Add(p);
            p.Amount = input.Amount;
            p.PaidOn = input.PaidOn ?? Today();
            p.Note = input.Note?.Trim() ?? "";
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(write);

        group.MapDelete("/obligations/{id:guid}/payments/{month}", async (Guid id, DateOnly month, AppDbContext db, CancellationToken ct) =>
        {
            var m = Domain.Billing.MonthStart(month);
            return await db.ObligationPayments.Where(p => p.ObligationId == id && p.Month == m).ExecuteDeleteAsync(ct) > 0
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization(write);

        // ----- Warnings -----
        group.MapGet("/billing/alerts", async (AppDbContext db, CancellationToken ct) =>
        {
            var today = Today();
            var settings = await SettingsAsync(db, ct);
            var dues = await DuesAsync(db, settings, null, ct);
            var month = Domain.Billing.MonthStart(today);
            var obligations = await ObligationScheduleAsync(db, settings, month, month.AddMonths(1), ct);
            return new BillingAlerts(
                dues.Count(d => d.Overdue),
                dues.Count(d => d.DueSoon),
                obligations.Count(o => o.Overdue),
                obligations.Count(o => o.DueSoon),
                dues.SelectMany(d => d.Payments).Where(p => p.InvoiceStatus != InvoiceStatus.Sent).Select(p => p.Id).Distinct().Count());
        }).RequireAuthorization(read);
    }

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);

    internal static async Task<BillingSettings> SettingsAsync(AppDbContext db, CancellationToken ct) =>
        await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BillingSettings();

    private static BillingSettingsDto ToDto(BillingSettings s, ResolvedIntegration? integration) => new(
        s.VatRate, s.MonthlyDueDay, s.ReminderDays, s.ItemName, s.Unit, s.Note, s.DefaultTaxId, integration is not null, integration?.Provider);

    /// <summary>Instalments of every active (or completed) enrollment, optionally of one program.</summary>
    internal static async Task<List<DueDto>> DuesAsync(AppDbContext db, BillingSettings settings, Guid? programId, CancellationToken ct)
    {
        var query = db.Enrollments.AsNoTracking()
            .Where(e => (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed) && (e.Fee > 0 || e.DueAdjustments.Any()));
        if (programId is not null) query = query.Where(e => e.ProgramId == programId);
        var enrollments = await query.Include(e => e.Program).Include(e => e.Payments).ThenInclude(p => p.Invoice)
            .Include(e => e.DueAdjustments).AsSplitQuery().ToListAsync(ct);

        var today = Today();
        var soon = today.AddDays(settings.ReminderDays);
        var result = new List<DueDto>();
        foreach (var e in enrollments)
        {
            foreach (var s in Domain.Billing.Allocate(Domain.Billing.DuesOf(e, e.Program.EndDate, settings.MonthlyDueDay), e.Payments))
            {
                var open = s.Status != DueStatus.Paid;
                var overdue = open && s.Due.DueDate < today;
                result.Add(new DueDto(
                    e.Id, e.ChildName, e.ParentName, e.ProgramId, e.Program.Title, e.FeePeriod,
                    s.Due.Period, e.FeePeriod == PricePeriod.Monthly ? Domain.Billing.MonthName(s.Due.Period) : "Tek seferlik",
                    s.Due.DueDate, s.Due.Amount, s.Paid, s.Status, overdue, open && !overdue && s.Due.DueDate <= soon,
                    e.Billing is { Name.Length: > 0 },
                    s.Payments.Select(p => new DuePaymentDto(p.Id, p.Amount, p.Method, p.PaidAt, p.Note,
                        p.Invoice?.Status, p.Invoice?.Number, p.Invoice?.Provider)).ToList(),
                    s.Due.StandardAmount, s.Due.Adjusted, s.Due.Note,
                    e.FeePeriod == PricePeriod.Monthly
                        ? Domain.Billing.ProratedFee(e.Fee, s.Due.Period, e.StartDate, e.EndDate ?? e.Program.EndDate)
                        : e.Fee));
            }
        }
        return result;
    }

    /// <summary>Months of active obligations in [start, end], plus earlier unpaid months (overdue, at most 3 years back).</summary>
    internal static async Task<List<ObligationDueDto>> ObligationScheduleAsync(
        AppDbContext db, BillingSettings settings, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var obligations = await db.Obligations.AsNoTracking().Where(o => o.IsActive).Include(o => o.Payments).ToListAsync(ct);
        var today = Today();
        var soon = today.AddDays(settings.ReminderDays);
        var result = new List<ObligationDueDto>();
        foreach (var o in obligations)
        {
            var first = Domain.Billing.MonthStart(o.StartMonth);
            var last = o.EndMonth is { } e && Domain.Billing.MonthStart(e) < end ? Domain.Billing.MonthStart(e) : end;
            var from = first > start.AddMonths(-36) ? first : start.AddMonths(-36);
            for (var m = from; m <= last; m = m.AddMonths(1))
            {
                var paid = o.Payments.FirstOrDefault(p => p.Month == m);
                var due = Domain.Billing.DayOf(m, o.DayOfMonth);
                var overdue = paid is null && due < today;
                if (m < start && !overdue) continue; // earlier months only when still unpaid
                result.Add(new ObligationDueDto(
                    o.Id, o.Title, o.Category, m, due, o.Amount, paid is not null, paid?.Amount, paid?.PaidOn, paid?.Note ?? "",
                    overdue, paid is null && !overdue && due <= soon));
            }
        }
        return result;
    }

    private static async Task<IResult> SaveObligationAsync(Obligation o, ObligationInput input, AppDbContext db, bool isNew, CancellationToken ct)
    {
        var start = Domain.Billing.MonthStart(input.StartMonth);
        DateOnly? endMonth = input.EndMonth is { } e ? Domain.Billing.MonthStart(e) : null;
        var errors = new Errors().Check(endMonth is null || endMonth >= start, "endMonth", "Bitiş ayı başlangıçtan önce olamaz.");
        if (input.StaffId is { } staffId && !await db.Staff.AnyAsync(s => s.Id == staffId, ct))
            errors.Check(false, "staffId", "Personel bulunamadı.");
        if (errors.Any) return errors.ToResult();

        o.Title = input.Title.Trim();
        o.Category = input.Category;
        o.Amount = input.Amount;
        o.DayOfMonth = input.DayOfMonth;
        o.StartMonth = start;
        o.EndMonth = endMonth;
        o.IsActive = input.IsActive;
        o.Note = input.Note?.Trim() ?? "";
        o.StaffId = input.StaffId;
        if (isNew) db.Obligations.Add(o);
        await db.SaveChangesAsync(ct);
        var staffName = o.StaffId is null ? null : await db.Staff.Where(s => s.Id == o.StaffId).Select(s => s.FullName).FirstOrDefaultAsync(ct);
        var dto = new ObligationDto(o.Id, o.Title, o.Category, o.Amount, o.DayOfMonth, o.StartMonth, o.EndMonth, o.IsActive, o.Note, o.StaffId, staffName);
        return isNew ? Results.Created($"/api/admin/obligations/{o.Id}", dto) : Results.Ok(dto);
    }
}
