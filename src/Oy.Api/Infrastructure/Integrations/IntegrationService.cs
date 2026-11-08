using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;

namespace Oy.Api.Infrastructure.Integrations;

/// <summary>Decrypted, ready-to-use provider configuration.</summary>
public record ResolvedIntegration(
    IntegrationType Type, string Provider, bool IsActive, bool IsTestMode,
    IReadOnlyDictionary<string, string> Settings, IReadOnlyDictionary<string, string> Secrets)
{
    public string Setting(string key, string fallback = "") =>
        Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public bool Flag(string key) => Setting(key).Equals("true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads provider configs; secrets are encrypted at rest with ASP.NET Data Protection.</summary>
public class IntegrationService(AppDbContext db, IDataProtectionProvider dataProtection)
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Oy.Integrations.Secrets.v1");

    public string Protect(Dictionary<string, string> secrets) =>
        secrets.Count == 0 ? "" : _protector.Protect(JsonSerializer.Serialize(secrets));

    public Dictionary<string, string> Unprotect(string encrypted) =>
        string.IsNullOrEmpty(encrypted)
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(encrypted)) ?? [];

    public async Task<ResolvedIntegration?> GetActiveAsync(IntegrationType type, CancellationToken ct = default)
    {
        var config = await db.Integrations.AsNoTracking().FirstOrDefaultAsync(x => x.Type == type && x.IsActive, ct);
        return config is null
            ? null
            : new ResolvedIntegration(config.Type, config.Provider, config.IsActive, config.IsTestMode,
                config.Settings, Unprotect(config.EncryptedSecrets));
    }

    public static string Mask(string value) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= 4 ? "••••" : "••••" + value[^4..];
}
