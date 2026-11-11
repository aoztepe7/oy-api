using System.Security.Cryptography;
using System.Text;

namespace Oy.Api.Domain;

public enum DevicePlatform { Web, Ios, Android }

/// <summary>
/// A signed-in device of the mobile app: a long-lived refresh token (only its hash is stored, rotated on every use)
/// and the device's push notification token.
/// </summary>
public class UserSession : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DevicePlatform Platform { get; set; }
    /// <summary>"iPhone 15", "Pixel 8" — shown in the account's device list.</summary>
    public string DeviceName { get; set; } = "";
    public string AppVersion { get; set; } = "";
    /// <summary>SHA-256 of the current refresh token.</summary>
    public string TokenHash { get; set; } = "";
    /// <summary>SHA-256 of the previous refresh token: presenting it again means it was stolen → the session is closed.</summary>
    public string PreviousTokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    /// <summary>Firebase Cloud Messaging registration token (Android and iOS); empty until the app registers.</summary>
    public string PushToken { get; set; } = "";

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}

public static class Sessions
{
    /// <summary>A device stays signed in this long after its last use.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);

    /// <summary>256 random bits, base64url.</summary>
    public static string NewRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
