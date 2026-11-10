using Microsoft.Extensions.Options;

namespace Oy.Api.Infrastructure;

/// <summary>
/// One-off move from the server's disk to Spaces: <c>dotnet Oy.Api.dll storage-sync</c>.
/// Uploads every file under storage/media (public) and storage/private (private) that is not in the bucket yet;
/// nothing is deleted locally. Stored URLs ("/media/…") do not change, so the database needs no update.
/// </summary>
public static class StorageSync
{
    public const string Command = "storage-sync";

    public static async Task<int> RunAsync(IServiceProvider services)
    {
        var log = services.GetRequiredService<ILoggerFactory>().CreateLogger("StorageSync");
        var storage = services.GetRequiredService<IFileStorage>();
        if (!storage.IsRemote)
        {
            log.LogError("Storage:Spaces ayarları eksik; dosyalar zaten sunucu diskinde. Önce Spaces ayarlarını girin.");
            return 1;
        }

        var sources = new[]
        {
            (Root: Path.GetFullPath(services.GetRequiredService<IOptions<MediaOptions>>().Value.Path), Prefix: StorageKeys.Media, Public: true),
            (Root: Path.GetFullPath(services.GetRequiredService<IOptions<PrivateFileOptions>>().Value.Path), Prefix: StorageKeys.Private, Public: false),
        };
        int uploaded = 0, skipped = 0, failed = 0;
        foreach (var (root, prefix, isPublic) in sources)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var key = prefix + Path.GetRelativePath(root, file).Replace('\\', '/');
                try
                {
                    if (await storage.ExistsAsync(key)) { skipped++; continue; }
                    await using var stream = File.OpenRead(file);
                    await storage.SaveAsync(key, stream, StorageKeys.ContentTypeOf(file), isPublic);
                    uploaded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    log.LogError(ex, "Yüklenemedi: {Key}", key);
                }
            }
        }
        log.LogInformation("Spaces aktarımı bitti: {Uploaded} yüklendi, {Skipped} zaten vardı, {Failed} hata.", uploaded, skipped, failed);
        return failed == 0 ? 0 : 2;
    }
}
