using Oy.Api.Infrastructure;

namespace Oy.Api.Data;

/// <summary>
/// Images shipped with the API (SeedMedia folder, from the previous site) and copied into media
/// storage on startup, so seeded pages, programs and staff photos work out of the box.
/// </summary>
public static class SeedMedia
{
    private const string Folder = "seed";
    private const int GalleryCount = 28;

    public static string Url(string file) => $"{MediaService.RoutePrefix}{Folder}/{file}";

    // Framing (?focus / ?fit) is read by oy-ui and editable in the panel ("Konumla").
    public static readonly string HeroPhoto = Url("galeri-24.webp") + "?focus=40,60";
    public static readonly string CirclePhoto = Url("galeri-03.webp");
    public static readonly string ContactPhoto = Url("galeri-28.webp") + "?focus=50,40";
    public static readonly string PelinPhoto = Url("pelin-oztepe.jpeg") + "?focus=50,22";
    /// <summary>The camp images are posters with text on them: shown whole, never cropped.</summary>
    public static readonly string LyciaPoster = Url("likya-yolu-kampi.jpeg") + "?fit=contain";
    public static readonly string FamilyCampPoster = Url("aile-macera-kampi.jpeg") + "?fit=contain";

    public static IEnumerable<string> Gallery() =>
        Enumerable.Range(1, GalleryCount).Select(i => Url($"galeri-{i:00}.webp"));

    /// <summary>Copies missing seed images into media storage (never overwrites).</summary>
    public static void CopyTo(string mediaRoot)
    {
        var source = Path.Combine(AppContext.BaseDirectory, "SeedMedia");
        if (!Directory.Exists(source)) return;
        var target = Path.Combine(mediaRoot, Folder);
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var dest = Path.Combine(target, Path.GetFileName(file));
            if (!File.Exists(dest)) File.Copy(file, dest);
        }
    }
}
