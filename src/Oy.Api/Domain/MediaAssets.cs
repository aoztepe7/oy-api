namespace Oy.Api.Domain;

/// <summary>
/// A public image in the media library (site pictures: covers, page and gallery images, staff photos), kept in
/// folders so it can be picked again later. Children's photos and feed images are never here — they are private.
/// </summary>
public class MediaAsset : Entity
{
    /// <summary>Folder name as shown, e.g. "Genel", "Galeri", "Kamplar".</summary>
    public string Folder { get; set; } = MediaFolders.Default;
    /// <summary>Stored relative URL, "/media/{name}" — what pages and programs save.</summary>
    public required string Url { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Guid? UploadedByUserId { get; set; }
}

public static class MediaFolders
{
    public const string Default = "Genel";
    /// <summary>Images shipped with the API (seed).</summary>
    public const string Seed = "Hazır görseller";
}
