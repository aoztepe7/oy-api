namespace Oy.Api.Infrastructure.Integrations;

public record PaymentRequest(Guid EnrollmentId, decimal Amount, string Description, string BuyerName, string BuyerEmail, string CallbackUrl);

public record PaymentInitResult(bool Success, string? RedirectUrl, string? Error);

/// <summary>
/// Online payment provider contract. Concrete integrations (iyzico, PayTR) will be implemented later;
/// configuration is already read from <see cref="IntegrationService"/>.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    Task<PaymentInitResult> InitializeAsync(PaymentRequest request, ResolvedIntegration config, CancellationToken ct);
}

/// <summary>Placeholder used until a real provider client is written.</summary>
public sealed class PendingPaymentProvider(string name) : IPaymentProvider
{
    public string Name => name;

    public Task<PaymentInitResult> InitializeAsync(PaymentRequest request, ResolvedIntegration config, CancellationToken ct) =>
        Task.FromResult(new PaymentInitResult(false, null, $"{name} entegrasyonu henüz uygulanmadı."));
}

public class PaymentProviderResolver(IEnumerable<IPaymentProvider> providers, IntegrationService integrations)
{
    /// <summary>Returns the active provider with its config, or null when online payment is not configured.</summary>
    public async Task<(IPaymentProvider Provider, ResolvedIntegration Config)?> ResolveAsync(CancellationToken ct = default)
    {
        var config = await integrations.GetActiveAsync(Domain.IntegrationType.Payment, ct);
        var provider = config is null ? null : providers.FirstOrDefault(p => p.Name.Equals(config.Provider, StringComparison.OrdinalIgnoreCase));
        return provider is null ? null : (provider, config!);
    }
}
