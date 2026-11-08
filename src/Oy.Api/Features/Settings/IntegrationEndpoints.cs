using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Integrations;

namespace Oy.Api.Features.Settings;

public record IntegrationInput(string Provider, bool IsActive, bool IsTestMode,
    Dictionary<string, string>? Settings, Dictionary<string, string>? Secrets);

public record IntegrationDto(IntegrationType Type, string Provider, bool IsActive, bool IsTestMode,
    Dictionary<string, string> Settings, Dictionary<string, string> Secrets);

public static class IntegrationEndpoints
{
    public static void MapIntegrations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/integrations").WithTags("Integrations")
            .RequireAuthorization(Permissions.SettingsManage);

        group.MapGet("/providers", () => IntegrationCatalog.All);

        group.MapGet("/", async (AppDbContext db, IntegrationService integrations) =>
            (await db.Integrations.AsNoTracking().OrderBy(i => i.Type).ToListAsync()).Select(i => ToDto(i, integrations)));

        group.MapPut("/{type}", async (IntegrationType type, IntegrationInput input, AppDbContext db, IntegrationService integrations) =>
        {
            var def = IntegrationCatalog.Find(type, input.Provider ?? "");
            if (def is null) return new Errors().Check(false, "provider", "Geçersiz sağlayıcı.").ToResult();

            var config = await db.Integrations.FirstOrDefaultAsync(i => i.Type == type);
            if (config is null)
            {
                config = new IntegrationConfig { Type = type };
                db.Integrations.Add(config);
            }

            var providerChanged = config.Provider != def.Provider;
            var secrets = providerChanged ? [] : integrations.Unprotect(config.EncryptedSecrets);
            foreach (var field in def.SecretFields)
            {
                // Empty value = keep existing secret (panel never receives the real value).
                if (input.Secrets?.TryGetValue(field.Key, out var v) == true && !string.IsNullOrWhiteSpace(v))
                    secrets[field.Key] = v.Trim();
            }

            config.Provider = def.Provider;
            config.IsActive = input.IsActive;
            config.IsTestMode = input.IsTestMode;
            config.Settings = def.SettingFields.ToDictionary(
                f => f.Key,
                f => input.Settings?.TryGetValue(f.Key, out var v) == true ? v?.Trim() ?? "" : "");
            config.EncryptedSecrets = integrations.Protect(secrets.Where(s => def.SecretFields.Any(f => f.Key == s.Key)).ToDictionary());
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(config, integrations));
        });
    }

    private static IntegrationDto ToDto(IntegrationConfig c, IntegrationService integrations) => new(
        c.Type, c.Provider, c.IsActive, c.IsTestMode, c.Settings,
        integrations.Unprotect(c.EncryptedSecrets).ToDictionary(s => s.Key, s => IntegrationService.Mask(s.Value)));
}
