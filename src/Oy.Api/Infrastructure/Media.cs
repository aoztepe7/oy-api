using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure;

public class MediaOptions
{
    /// <summary>Folder where uploaded files are stored.</summary>
    public string Path { get; set; } = "data/media";
    /// <summary>Public base URL of this API, e.g. https://service.okulumyeryuzu.com</summary>
    public string PublicBaseUrl { get; set; } = "http://localhost:8080";
    public long MaxBytes { get; set; } = 5 * 1024 * 1024;
}

/// <summary>
/// Media URLs are stored relative ("/media/x.webp", optionally with "?focus=…") and exposed absolute, so changing
/// the public domain — or moving files from the server's disk to DigitalOcean Spaces — never breaks stored content.
/// </summary>
public class MediaService(IOptions<MediaOptions> options, IFileStorage storage)
{
    public const string RoutePrefix = "/media/";
    private readonly MediaOptions _o = options.Value;
    private string BaseUrl => _o.PublicBaseUrl.TrimEnd('/');
    /// <summary>Absolute prefix of media files where they are served: Spaces/CDN, or this API.</summary>
    private string PublicPrefix => storage.PublicUrl(StorageKeys.Media) ?? BaseUrl + RoutePrefix;

    public string FullPath => System.IO.Path.GetFullPath(_o.Path);

    public string ToPublic(string? url) =>
        string.IsNullOrEmpty(url) ? "" : url.StartsWith(RoutePrefix) ? PublicPrefix + url[RoutePrefix.Length..] : url;

    public string ToStored(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        url = url.Trim();
        // Either public form (Spaces/CDN or this API — also the old API form after a move) goes back to "/media/…".
        foreach (var prefix in new[] { PublicPrefix, BaseUrl + RoutePrefix })
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return RoutePrefix + url[prefix.Length..];
        return url;
    }

    private static readonly Dictionary<string, byte[][]> Signatures = new()
    {
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".webp"] = [[0x52, 0x49, 0x46, 0x46]], // RIFF....WEBP
    };

    /// <summary>Validates by magic bytes (not by client-supplied name/type) and saves with a random name.</summary>
    /// <returns>The stored relative URL ("/media/x.webp"), or null when the file is not an accepted image.</returns>
    public async Task<string?> SaveAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0 || file.Length > _o.MaxBytes) return null;

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        var ext = Signatures.FirstOrDefault(s => s.Value.Any(sig => read >= sig.Length && header.AsSpan(0, sig.Length).SequenceEqual(sig))).Key;
        if (ext is null) return null;
        if (ext == ".webp" && (read < 12 || !header.AsSpan(8, 4).SequenceEqual("WEBP"u8))) return null;

        var name = $"{Guid.CreateVersion7():N}{ext}";
        stream.Position = 0;
        await storage.SaveAsync(StorageKeys.Media + name, stream, StorageKeys.ContentTypeOf(name), isPublic: true, ct);
        return RoutePrefix + name;
    }
}
