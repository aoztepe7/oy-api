using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Infrastructure;

public class PrivateFileOptions
{
    /// <summary>Folder for private files; never served as static files.</summary>
    public string Path { get; set; } = "storage/private";
    public long MaxBytes { get; set; } = 8 * 1024 * 1024;
}

/// <summary>
/// Children's photos and feed images. Files live outside the public media folder and are reachable only through
/// signed links (<c>/files/{id}?e=…&amp;s=…</c>) that the API hands to users allowed to see them. A link stays the
/// same within a 6-hour window (so browsers can cache it) and expires 6–12 hours after it was issued.
/// </summary>
public class PrivateFileService(
    IOptions<PrivateFileOptions> options, IOptions<MediaOptions> media, IOptions<JwtOptions> jwt, IFileStorage storage)
{
    public const string Route = "/files/";
    private static readonly TimeSpan Window = TimeSpan.FromHours(6);
    private readonly PrivateFileOptions _o = options.Value;
    private readonly byte[] _key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), "oy.private-files.v1"u8);

    public static string KeyOf(PrivateFile f) => StorageKeys.Private + f.StoredName;

    /// <summary>Disk: the file itself. Spaces: a redirect to a short-lived pre-signed URL.</summary>
    public Task<IResult> ServeAsync(PrivateFile f, CancellationToken ct) => storage.ServePrivateAsync(KeyOf(f), f.ContentType, ct);

    public TimeSpan MaxCache => storage.MaxPrivateCache;

    public string Url(Guid id)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var window = (long)Window.TotalSeconds;
        var expires = (now / window + 2) * window;
        return $"{media.Value.PublicBaseUrl.TrimEnd('/')}{Route}{id:N}?e={expires}&s={Sign(id, expires)}";
    }

    public bool Verify(Guid id, long expires, string? signature) =>
        expires > DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
        signature is not null &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(id, expires)), Encoding.ASCII.GetBytes(signature));

    private string Sign(Guid id, long expires) =>
        Base64Url.EncodeToString(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes($"{id:N}.{expires}")).AsSpan(0, 18));

    private static readonly (string Ext, string Type, byte[] Magic)[] Formats =
    [
        (".jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
        (".png", "image/png", [0x89, 0x50, 0x4E, 0x47]),
        (".webp", "image/webp", "RIFF"u8.ToArray()),
    ];

    /// <summary>Checks the file by its magic bytes (not the client's name / type) and stores it under a random name.</summary>
    public async Task<PrivateFile?> SaveAsync(IFormFile file, int width, int height, Guid? userId, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > _o.MaxBytes) return null;
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var format = Formats.FirstOrDefault(f => read >= f.Magic.Length && header.AsSpan(0, f.Magic.Length).SequenceEqual(f.Magic));
        if (format.Ext is null) return null;
        if (format.Ext == ".webp" && (read < 12 || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8))) return null;

        var entity = new PrivateFile
        {
            StoredName = $"{Guid.CreateVersion7():N}{format.Ext}", ContentType = format.Type, Size = file.Length,
            Width = Math.Clamp(width, 0, 20_000), Height = Math.Clamp(height, 0, 20_000), UploadedByUserId = userId,
        };
        stream.Position = 0;
        await storage.SaveAsync(KeyOf(entity), stream, entity.ContentType, isPublic: false, ct);
        return entity;
    }

    /// <summary>Removes the stored file; a failure is logged by the caller's pipeline but never blocks the delete.</summary>
    public async Task DeleteAsync(PrivateFile f)
    {
        try { await storage.DeleteAsync(KeyOf(f)); }
        catch (Exception) { /* the row is gone; an orphaned object costs only storage */ }
    }
}

/// <summary>An image as the clients get it: signed URL plus size for layout.</summary>
public record ImageDto(Guid Id, string Url, int Width, int Height);
