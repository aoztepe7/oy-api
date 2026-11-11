using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Push;

namespace Oy.Api.Infrastructure.Integrations;

public record EInvoiceLine(string Description, decimal Quantity, decimal UnitPriceExclVat, decimal VatRate);

public record EInvoiceDocument(
    Guid InvoiceId, DateTime IssueDate, string BuyerName, string BuyerEmail, string BuyerTaxId,
    IReadOnlyList<EInvoiceLine> Lines, decimal TotalInclVat);

public record EInvoiceResult(bool Success, string Number, string ExternalId, string? Error);

/// <summary>
/// e-Fatura / e-Arşiv provider. GİB has no direct API for taxpayers: invoices go through a
/// GİB-authorised private integrator (özel entegratör). Each integrator gets one implementation.
/// </summary>
public interface IEInvoiceProvider
{
    string Name { get; }
    Task<EInvoiceResult> IssueAsync(EInvoiceDocument document, ResolvedIntegration config, CancellationToken ct);
}

/// <summary>Simulates a successful issue; for testing the end-to-end flow.</summary>
public sealed class MockEInvoiceProvider : IEInvoiceProvider
{
    public string Name => "Mock";

    public Task<EInvoiceResult> IssueAsync(EInvoiceDocument document, ResolvedIntegration config, CancellationToken ct)
    {
        var prefix = config.Setting("invoicePrefix", "OYE").ToUpperInvariant().PadRight(3, 'X')[..3];
        // GİB number format: 3-char prefix + year + 9-digit sequence (16 chars).
        var seq = (uint)document.InvoiceId.GetHashCode() % 1_000_000_000;
        return Task.FromResult(new EInvoiceResult(true, $"{prefix}{document.IssueDate:yyyy}{seq:D9}", Guid.NewGuid().ToString(), null));
    }
}

/// <summary>Placeholder for real integrators until their API clients are written.</summary>
public sealed class PendingEInvoiceProvider(string name) : IEInvoiceProvider
{
    public string Name => name;

    public Task<EInvoiceResult> IssueAsync(EInvoiceDocument document, ResolvedIntegration config, CancellationToken ct) =>
        Task.FromResult(new EInvoiceResult(false, "", "", $"{name} entegrasyonu henüz uygulanmadı."));
}

public class InvoiceService(AppDbContext db, IntegrationService integrations)
{
    /// <summary>Creates a Pending invoice for the payment (idempotent). Worker sends it.</summary>
    public async Task<Invoice?> QueueAsync(Guid paymentId, CancellationToken ct)
    {
        var existing = await db.Invoices.FirstOrDefaultAsync(i => i.PaymentId == paymentId, ct);
        if (existing is not null)
        {
            if (existing.Status == InvoiceStatus.Failed)
            {
                existing.Status = InvoiceStatus.Pending;
                existing.Attempts = 0;
                existing.Error = "";
            }
            return existing;
        }

        var config = await integrations.GetActiveAsync(IntegrationType.EInvoice, ct);
        var invoice = new Invoice { PaymentId = paymentId, Provider = config?.Provider ?? "", Status = InvoiceStatus.Pending };
        db.Invoices.Add(invoice);
        return invoice;
    }

    public async Task<bool> AutoIssueEnabledAsync(CancellationToken ct) =>
        (await integrations.GetActiveAsync(IntegrationType.EInvoice, ct))?.Flag("autoIssue") == true;
}

/// <summary>Sends pending invoices to the active e-invoice provider in the background, with retries.</summary>
public sealed class InvoiceWorker(IServiceScopeFactory scopes, ILogger<InvoiceWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try { await ProcessAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Invoice worker failed"); }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = await scope.ServiceProvider.GetRequiredService<IntegrationService>().GetActiveAsync(IntegrationType.EInvoice, ct);
        if (config is null) return;

        var provider = scope.ServiceProvider.GetServices<IEInvoiceProvider>()
            .FirstOrDefault(p => p.Name.Equals(config.Provider, StringComparison.OrdinalIgnoreCase));
        if (provider is null) return;

        var pending = await db.Invoices
            .Include(i => i.Payment).ThenInclude(p => p.Enrollment).ThenInclude(e => e.Program)
            .Where(i => i.Status == InvoiceStatus.Pending && i.Attempts < MaxAttempts)
            .OrderBy(i => i.CreatedAt).Take(20).ToListAsync(ct);

        // Same values as the manual invoice template (Ödeme takibi → Fatura ayarları).
        var settings = await db.BillingSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new BillingSettings();
        var sent = new List<Invoice>();
        foreach (var invoice in pending)
        {
            var payment = invoice.Payment;
            var draft = Billing.Draft(payment, settings, DateOnly.FromDateTime(DateTime.UtcNow));
            var doc = new EInvoiceDocument(
                invoice.Id, DateTime.UtcNow, draft.Buyer.Name, draft.Buyer.Email, draft.Buyer.TaxId,
                [new EInvoiceLine(draft.ItemName, draft.Quantity, draft.UnitPrice, draft.VatRate)],
                draft.Total);

            invoice.Provider = provider.Name;
            invoice.Attempts++;
            var result = await provider.IssueAsync(doc, config, ct);
            if (result.Success)
            {
                invoice.Status = InvoiceStatus.Sent;
                invoice.Number = result.Number;
                invoice.ExternalId = result.ExternalId;
                invoice.SentAt = DateTime.UtcNow;
                invoice.IssuedOn = draft.IssueDate;
                invoice.VatRate = draft.VatRate;
                invoice.NetAmount = draft.UnitPrice * draft.Quantity;
                invoice.VatAmount = draft.VatAmount;
                invoice.Error = "";
                sent.Add(invoice);
            }
            else
            {
                invoice.Error = result.Error ?? "Bilinmeyen hata";
                if (invoice.Attempts >= MaxAttempts) invoice.Status = InvoiceStatus.Failed;
                logger.LogWarning("Invoice {InvoiceId} failed (attempt {Attempt}): {Error}", invoice.Id, invoice.Attempts, invoice.Error);
            }
        }
        await db.SaveChangesAsync(ct);

        var notifier = scope.ServiceProvider.GetRequiredService<Notifier>();
        foreach (var invoice in sent)
        {
            var e = invoice.Payment.Enrollment;
            await notifier.SendAsync(await notifier.GuardiansOfAsync([e.Id], ct), NotificationKind.InvoiceIssued, "Faturanız kesildi",
                $"{e.ChildName} · {e.Program.Title}: {ReminderWorker.Money(invoice.Payment.Amount)} ödemenizin faturası kesildi (no: {invoice.Number}).",
                "/veli/hesap", $"invoice:{invoice.Id}", ct);
        }
    }
}
