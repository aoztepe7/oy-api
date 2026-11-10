using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure;

/// <summary>
/// DigitalOcean Spaces (S3 compatible). When <see cref="IsConfigured"/> files go to the bucket; otherwise they
/// stay on the server's disk (storage/media, storage/private).
/// </summary>
public class SpacesOptions
{
    /// <summary>Region endpoint, e.g. https://fra1.digitaloceanspaces.com</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>Region used for signing, e.g. fra1.</summary>
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    /// <summary>
    /// Base URL of public files: the Spaces CDN (https://{bucket}.fra1.cdn.digitaloceanspaces.com) or a custom
    /// domain. Empty → https://{bucket}.{region-host}.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
    /// <summary>Optional key prefix to share one bucket between environments, e.g. "prod/".</summary>
    public string Prefix { get; set; } = "";
    /// <summary>endpoint/bucket/key URLs instead of bucket.endpoint/key (not needed for Spaces; for S3 emulators).</summary>
    public bool ForcePathStyle { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Bucket) &&
        !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>
/// Where uploaded files live. Keys are "media/{name}" (public: site images) and "private/{name}" (children's
/// photos, feed images — reachable only through links the API signs).
/// </summary>
public interface IFileStorage
{
    /// <summary>"DigitalOcean Spaces (bucket)" or "Sunucu diski" — for logs.</summary>
    string Description { get; }
    bool IsRemote { get; }
    Task SaveAsync(string key, Stream content, string contentType, bool isPublic, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    /// <summary>Every stored file under a prefix (e.g. "media/"), keys without the environment prefix.</summary>
    IAsyncEnumerable<StoredObject> ListAsync(string prefix, CancellationToken ct = default);
    /// <summary>Absolute URL of a public file, or null when the API serves it itself (disk).</summary>
    string? PublicUrl(string key);
    /// <summary>Answers a request for a private file the caller is already allowed to see.</summary>
    Task<IResult> ServePrivateAsync(string key, string contentType, CancellationToken ct = default);

    /// <summary>
    /// How long a browser may keep the answer of <see cref="ServePrivateAsync"/>: a redirect to a pre-signed URL
    /// must not outlive that URL.
    /// </summary>
    TimeSpan MaxPrivateCache { get; }
}

public record StoredObject(string Key, long Size);

public static class StorageKeys
{
    public const string Media = "media/";
    public const string Private = "private/";

    public static string ContentTypeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".gif" => "image/gif",
        _ => "application/octet-stream",
    };
}

/// <summary>Files on the server's disk: media/ → Media:Path, private/ → PrivateFiles:Path.</summary>
public class LocalFileStorage(IOptions<MediaOptions> media, IOptions<PrivateFileOptions> privateFiles) : IFileStorage
{
    public string Description => $"Sunucu diski ({Path.GetFullPath(media.Value.Path)}, {Path.GetFullPath(privateFiles.Value.Path)})";
    public bool IsRemote => false;

    public string PathOf(string key)
    {
        var (root, name) = key.StartsWith(StorageKeys.Private)
            ? (privateFiles.Value.Path, key[StorageKeys.Private.Length..])
            : (media.Value.Path, key.StartsWith(StorageKeys.Media) ? key[StorageKeys.Media.Length..] : key);
        var full = Path.GetFullPath(Path.Combine(root, name));
        // Keys come from our own code, but never let one escape its folder.
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid storage key.");
        return full;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, bool isPublic, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = File.Create(path);
        await content.CopyToAsync(target, ct);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(PathOf(key)));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        try { File.Delete(PathOf(key)); }
        catch (IOException) { /* already gone */ }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var root = prefix.StartsWith(StorageKeys.Private) ? privateFiles.Value.Path : media.Value.Path;
        var folder = Path.GetFullPath(root);
        if (!Directory.Exists(folder)) yield break;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var top = prefix.StartsWith(StorageKeys.Private) ? StorageKeys.Private : StorageKeys.Media;
            yield return new StoredObject(top + Path.GetRelativePath(folder, file).Replace('\\', '/'), new FileInfo(file).Length);
        }
        await Task.CompletedTask;
    }

    public string? PublicUrl(string key) => null;

    public TimeSpan MaxPrivateCache => TimeSpan.MaxValue;

    public Task<IResult> ServePrivateAsync(string key, string contentType, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return Task.FromResult(File.Exists(path) ? Results.File(path, contentType) : Results.NotFound());
    }
}

/// <summary>
/// DigitalOcean Spaces. Public files get a public-read ACL and a long cache; private files stay private and are
/// handed out as short-lived pre-signed URLs (the browser downloads straight from Spaces, not through the API).
/// </summary>
public sealed class SpacesFileStorage : IFileStorage, IDisposable
{
    private static readonly TimeSpan PresignFor = TimeSpan.FromHours(1);
    private readonly SpacesOptions _o;
    private readonly AmazonS3Client _s3;
    private readonly string _publicBase;

    public SpacesFileStorage(IOptions<SpacesOptions> options)
    {
        _o = options.Value;
        _s3 = new AmazonS3Client(new BasicAWSCredentials(_o.AccessKey, _o.SecretKey), new AmazonS3Config
        {
            ServiceURL = _o.Endpoint,
            AuthenticationRegion = _o.Region,
            ForcePathStyle = _o.ForcePathStyle,
            // Spaces does not support the newer default request checksums / aws-chunked uploads of SDK v4.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
        var host = new Uri(_o.Endpoint).Host;
        _publicBase = (string.IsNullOrWhiteSpace(_o.PublicBaseUrl) ? $"https://{_o.Bucket}.{host}" : _o.PublicBaseUrl).TrimEnd('/');
    }

    public string Description => $"DigitalOcean Spaces ({_o.Bucket} @ {_o.Endpoint}{(_o.Prefix.Length > 0 ? $", önek {_o.Prefix}" : "")})";
    public bool IsRemote => true;

    private string Key(string key) => _o.Prefix + key;

    public async Task SaveAsync(string key, Stream content, string contentType, bool isPublic, CancellationToken ct = default)
    {
        // A seekable copy: the SDK needs the length up front (no chunked upload on Spaces).
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        buffer.Position = 0;
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _o.Bucket,
            Key = Key(key),
            InputStream = buffer,
            ContentType = contentType,
            CannedACL = isPublic ? S3CannedACL.PublicRead : S3CannedACL.Private,
            // File names are unique (random), so a public file never changes.
            Headers = { CacheControl = isPublic ? "public, max-age=31536000, immutable" : "private, max-age=3600" },
            UseChunkEncoding = false,
        }, ct);
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_o.Bucket, Key(key), ct);
            return true;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct = default) => _s3.DeleteObjectAsync(_o.Bucket, Key(key), ct);

    public async IAsyncEnumerable<StoredObject> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new ListObjectsV2Request { BucketName = _o.Bucket, Prefix = Key(prefix) };
        ListObjectsV2Response page;
        do
        {
            page = await _s3.ListObjectsV2Async(request, ct);
            foreach (var o in page.S3Objects ?? [])
                yield return new StoredObject(o.Key[_o.Prefix.Length..], o.Size ?? 0);
            request.ContinuationToken = page.NextContinuationToken;
        } while (page.IsTruncated == true);
    }

    public string? PublicUrl(string key) => $"{_publicBase}/{Key(key)}";

    // Well inside the pre-signed URL's lifetime.
    public TimeSpan MaxPrivateCache => PresignFor - TimeSpan.FromMinutes(10);

    public async Task<IResult> ServePrivateAsync(string key, string contentType, CancellationToken ct = default)
    {
        var url = await _s3.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _o.Bucket,
            Key = Key(key),
            Verb = HttpVerb.GET,
            Protocol = _o.Endpoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? Protocol.HTTP : Protocol.HTTPS,
            Expires = DateTime.UtcNow.Add(PresignFor),
            ResponseHeaderOverrides = { ContentType = contentType, CacheControl = "private, max-age=3600" },
        });
        // The API link stays stable (and cacheable); the redirect target is fresh and short-lived.
        return Results.Redirect(url);
    }

    public void Dispose() => _s3.Dispose();
}
