using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure.Push;

/// <summary>
/// Firebase service account (Firebase console → Project settings → Service accounts → Generate new private key).
/// Secret: give the file's path (<c>Push:CredentialsPath</c>, a mounted file) or its JSON (<c>Push:Credentials</c>).
/// Without either, notifications still land in the inbox; only the push to phones is skipped.
/// </summary>
public class PushOptions
{
    public string CredentialsPath { get; set; } = "";
    public string Credentials { get; set; } = "";
}

public enum PushResult { Sent, InvalidToken, Failed }

public record PushMessage(string Token, string Title, string Body, string Url, int Badge);

/// <summary>Firebase Cloud Messaging HTTP v1 (Android and iOS through APNs). No SDK: an OAuth token from the service account.</summary>
public sealed class FcmSender(HttpClient http, IOptions<PushOptions> options, ILogger<FcmSender> logger)
{
    private const string Scope = "https://www.googleapis.com/auth/firebase.messaging";
    private readonly Lazy<ServiceAccount?> account = new(() => Load(options.Value, logger));
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string accessToken = "";
    private DateTime accessTokenExpires = DateTime.MinValue;

    public bool IsConfigured => account.Value is not null;

    public async Task<PushResult> SendAsync(PushMessage m, CancellationToken ct)
    {
        var sa = account.Value;
        if (sa is null) return PushResult.Failed;

        var body = new
        {
            message = new
            {
                token = m.Token,
                notification = new { title = m.Title, body = m.Body },
                data = new Dictionary<string, string> { ["url"] = m.Url },
                // Small icon: AndroidManifest's default_notification_icon (until then the app icon).
                android = new { priority = "HIGH", notification = new { color = "#2f6347" } },
                apns = new { payload = new { aps = new { badge = m.Badge, sound = "default" } } },
            },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://fcm.googleapis.com/v1/projects/{sa.ProjectId}/messages:send")
        {
            Content = JsonContent.Create(body),
        };
        req.Headers.Authorization = new("Bearer", await AccessTokenAsync(sa, ct));
        using var res = await http.SendAsync(req, ct);
        if (res.IsSuccessStatusCode) return PushResult.Sent;

        var error = await res.Content.ReadAsStringAsync(ct);
        // The app was removed / the token rotated: drop it.
        if (res.StatusCode == HttpStatusCode.NotFound || error.Contains("UNREGISTERED") ||
            (res.StatusCode == HttpStatusCode.BadRequest && error.Contains("registration token")))
            return PushResult.InvalidToken;
        logger.LogWarning("Push failed ({Status}): {Error}", (int)res.StatusCode, error.Length > 500 ? error[..500] : error);
        if (res.StatusCode == HttpStatusCode.Unauthorized) accessTokenExpires = DateTime.MinValue;
        return PushResult.Failed;
    }

    private async Task<string> AccessTokenAsync(ServiceAccount sa, CancellationToken ct)
    {
        if (DateTime.UtcNow < accessTokenExpires) return accessToken;
        await tokenLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow < accessTokenExpires) return accessToken;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var assertion = Jwt(sa, new { iss = sa.ClientEmail, scope = Scope, aud = sa.TokenUri, iat = now, exp = now + 3600 });
            using var res = await http.PostAsync(sa.TokenUri, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = assertion,
            }), ct);
            res.EnsureSuccessStatusCode();
            var token = await res.Content.ReadFromJsonAsync<TokenResponse>(ct) ?? throw new InvalidOperationException("Empty token response");
            accessToken = token.AccessToken;
            accessTokenExpires = DateTime.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 300));
            return accessToken;
        }
        finally { tokenLock.Release(); }
    }

    private static string Jwt(ServiceAccount sa, object claims)
    {
        static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var head = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        using var rsa = RSA.Create();
        rsa.ImportFromPem(sa.PrivateKey);
        var signature = rsa.SignData(Encoding.ASCII.GetBytes($"{head}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{head}.{payload}.{B64(signature)}";
    }

    private static ServiceAccount? Load(PushOptions o, ILogger logger)
    {
        try
        {
            var json = o.Credentials.Length > 0 ? o.Credentials
                : o.CredentialsPath.Length > 0 && File.Exists(o.CredentialsPath) ? File.ReadAllText(o.CredentialsPath)
                : null;
            if (json is null)
            {
                logger.LogInformation("Push: Firebase service account not configured; notifications stay in the inbox only.");
                return null;
            }
            var sa = JsonSerializer.Deserialize<ServiceAccount>(json);
            if (sa is null || sa.ProjectId.Length == 0 || sa.ClientEmail.Length == 0 || sa.PrivateKey.Length == 0)
                throw new InvalidOperationException("project_id, client_email or private_key missing");
            logger.LogInformation("Push: Firebase project {Project}", sa.ProjectId);
            return sa;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Push: Firebase service account could not be read");
            return null;
        }
    }

    private sealed record ServiceAccount(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("client_email")] string ClientEmail,
        [property: JsonPropertyName("private_key")] string PrivateKey,
        [property: JsonPropertyName("token_uri")] string TokenUri);

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
