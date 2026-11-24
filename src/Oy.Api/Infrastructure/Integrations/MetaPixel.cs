using System.Text.RegularExpressions;
using Oy.Api.Features;

namespace Oy.Api.Infrastructure.Integrations;

/// <summary>Meta (Facebook/Instagram) Pixel: browser-side ad conversion tracking on oy-ui.</summary>
public static partial class MetaPixel
{
    public const string Provider = "MetaPixel";
    public const string PixelIdKey = "pixelId";
    public const string DomainVerificationKey = "domainVerification";

    [GeneratedRegex(@"^\d{10,20}$")]
    private static partial Regex PixelIdRegex();

    [GeneratedRegex(@"^[A-Za-z0-9]{1,64}$")]
    private static partial Regex DomainCodeRegex();

    public static bool IsValidPixelId(string? v) => v is not null && PixelIdRegex().IsMatch(v);

    public static bool IsValidDomainVerification(string? v) => string.IsNullOrEmpty(v) || DomainCodeRegex().IsMatch(v);

    /// <summary>Validation errors for a save (keys: settings.pixelId / settings.domainVerification).
    /// The values end up inside a script on the site, so they are checked strictly.</summary>
    public static Errors Validate(bool isActive, IReadOnlyDictionary<string, string> settings)
    {
        settings.TryGetValue(PixelIdKey, out var pixelId);
        settings.TryGetValue(DomainVerificationKey, out var code);
        var errors = new Errors();
        var idEmpty = string.IsNullOrEmpty(pixelId);
        errors.Check(idEmpty ? !isActive : IsValidPixelId(pixelId), $"settings.{PixelIdKey}",
            "Pixel ID yalnızca rakamlardan oluşmalı (Events Manager'daki 15–16 haneli kimlik).");
        errors.Check(IsValidDomainVerification(code), $"settings.{DomainVerificationKey}",
            "Doğrulama kodu yalnızca harf ve rakamlardan oluşmalı (meta etiketindeki content değeri).");
        return errors;
    }
}
