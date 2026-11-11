using Oy.Api.Domain;

namespace Oy.Api.Infrastructure.Integrations;

public record IntegrationField(string Key, string Label, string Type = "text");

public record IntegrationProviderDefinition(
    IntegrationType Type, string Provider, string Label,
    IReadOnlyList<IntegrationField> SettingFields, IReadOnlyList<IntegrationField> SecretFields);

/// <summary>Known providers and the configuration fields each one needs.</summary>
public static class IntegrationCatalog
{
    private static readonly IntegrationField[] TaxpayerFields =
    [
        new("autoIssue", "Ödeme alınınca otomatik fatura kes", "boolean"),
        new("vkn", "VKN / TCKN"),
        new("title", "Ünvan"),
        new("taxOffice", "Vergi dairesi"),
        new("address", "Adres"),
        new("city", "İl"),
        new("email", "E-posta"),
        new("invoicePrefix", "Fatura seri öneki (3 karakter)"),
    ];

    private static readonly IntegrationField[] IntegratorSecrets =
    [
        new("username", "Kullanıcı adı"),
        new("password", "Şifre"),
    ];

    public static readonly IReadOnlyList<IntegrationProviderDefinition> All =
    [
        new(IntegrationType.Payment, "Iyzico", "iyzico",
            [new("baseUrl", "API URL")],
            [new("apiKey", "API Key"), new("secretKey", "Secret Key")]),
        new(IntegrationType.Payment, "PayTR", "PayTR",
            [new("merchantId", "Mağaza No")],
            [new("merchantKey", "Merchant Key"), new("merchantSalt", "Merchant Salt")]),

        new(IntegrationType.EInvoice, "Mock", "Test (simülasyon)", TaxpayerFields, []),
        new(IntegrationType.EInvoice, "Uyumsoft", "Uyumsoft",
            [.. TaxpayerFields, new("serviceUrl", "Servis URL")], IntegratorSecrets),
        new(IntegrationType.EInvoice, "QnbEFinans", "QNB eFinans",
            [.. TaxpayerFields, new("serviceUrl", "Servis URL")], IntegratorSecrets),
        new(IntegrationType.EInvoice, "Logo", "Logo eLogo",
            [.. TaxpayerFields, new("serviceUrl", "Servis URL")], IntegratorSecrets),
    ];

    public static IntegrationProviderDefinition? Find(IntegrationType type, string provider) =>
        All.FirstOrDefault(p => p.Type == type && p.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));
}
