using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Content;

/// <summary><c>Url</c>: absolute (to show); <c>StoredUrl</c>: relative "/media/…" (what forms save).</summary>
public record MediaAssetDto(
    Guid Id, string Url, string StoredUrl, string Folder, string OriginalName, string ContentType, long Size,
    int Width, int Height, DateTime CreatedAt);

public record MediaFolderDto(string Name, int Count);

public record MediaLibraryDto(List<MediaFolderDto> Folders, List<MediaAssetDto> Items);

public record MediaMove([Required, MaxLength(100)] string Folder);

/// <summary>
/// Public media library: every site image is kept in a folder and can be picked again (covers, gallery blocks,
/// staff photos). Private files (children's photos, feed images) are never listed here.
/// </summary>
public static class MediaLibraryEndpoints
{
    public static void MapMediaLibrary(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Media");

        // Upload (optionally into a folder). Keeps the old { url } answer for existing callers.
        admin.MapPost("/media", async (IFormFile file, string? folder, int? width, int? height, ClaimsPrincipal me,
            AppDbContext db, MediaService media, CancellationToken ct) =>
        {
            var stored = await media.SaveAsync(file, ct);
            if (stored is null) return new Errors().Check(false, "file", "Sadece JPG, PNG veya WEBP (en fazla 5 MB) yüklenebilir.").ToResult();
            var asset = new MediaAsset
            {
                Url = stored, Folder = CleanFolder(folder), OriginalName = Path.GetFileName(file.FileName ?? "").Trim(),
                ContentType = StorageKeys.ContentTypeOf(stored), Size = file.Length, Width = Math.Max(0, width ?? 0),
                Height = Math.Max(0, height ?? 0), UploadedByUserId = me.UserId(),
            };
            db.MediaAssets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { url = media.ToPublic(stored), asset = ToDto(asset, media) });
        }).RequireAuthorization(Permissions.MediaUpload).DisableAntiforgery();

        var library = admin.MapGroup("/media/library");

        library.MapGet("/", async (string? folder, string? q, AppDbContext db, MediaService media) =>
        {
            var folders = await db.MediaAssets.GroupBy(a => a.Folder)
                .Select(g => new MediaFolderDto(g.Key, g.Count())).OrderBy(f => f.Name).ToListAsync();
            var query = db.MediaAssets.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(folder)) query = query.Where(a => a.Folder == folder);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => EF.Functions.ILike(a.OriginalName, $"%{q.Trim()}%"));
            var items = await query.OrderByDescending(a => a.CreatedAt).Take(500).ToListAsync();
            return new MediaLibraryDto(folders, items.Select(a => ToDto(a, media)).ToList());
        }).RequireAuthorization(Permissions.MediaUpload);

        library.MapPut("/{id:guid}", async (Guid id, MediaMove input, AppDbContext db, MediaService media) =>
        {
            var asset = await db.MediaAssets.FindAsync(id);
            if (asset is null) return Results.NotFound();
            asset.Folder = CleanFolder(input.Folder);
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(asset, media));
        }).RequireAuthorization(Permissions.ContentWrite);

        // A file still shown somewhere on the site cannot be deleted (it would leave a broken image).
        library.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, IFileStorage storage) =>
        {
            var asset = await db.MediaAssets.FindAsync(id);
            if (asset is null) return Results.NotFound();
            var usage = await UsageAsync(db, asset.Url);
            if (usage.Count > 0)
                return ApiResults.Conflict($"Bu görsel kullanılıyor: {string.Join(", ", usage)}. Önce oradan kaldırın.");
            db.MediaAssets.Remove(asset);
            await db.SaveChangesAsync();
            try { await storage.DeleteAsync(StorageKeys.Media + asset.Url[MediaService.RoutePrefix.Length..]); }
            catch (Exception) { /* row is gone; an orphaned object costs only storage */ }
            return Results.NoContent();
        }).RequireAuthorization(Permissions.ContentWrite);
    }

    private static string CleanFolder(string? folder)
    {
        var name = string.Join(' ', (folder ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return name.Length == 0 ? MediaFolders.Default : name.Length > 100 ? name[..100] : name;
    }

    public static MediaAssetDto ToDto(MediaAsset a, MediaService media) =>
        new(a.Id, media.ToPublic(a.Url), a.Url, a.Folder, a.OriginalName, a.ContentType, a.Size, a.Width, a.Height, a.CreatedAt);

    /// <summary>Where a stored "/media/…" URL is used: pages, programs, staff, themes, header / footer style.</summary>
    public static async Task<List<string>> UsageAsync(AppDbContext db, string url)
    {
        var pattern = "%" + url.Replace("%", "\\%").Replace("_", "\\_") + "%";
        var places = new List<string>();
        var pages = await db.Database.SqlQuery<string>($"""
            SELECT "Title" AS "Value" FROM "Pages" WHERE "Draft"::text LIKE {pattern} OR "Published"::text LIKE {pattern}
            """).ToListAsync();
        places.AddRange(pages.Select(t => $"“{t}” sayfası"));
        places.AddRange((await db.Programs.Where(p => EF.Functions.Like(p.CoverImageUrl, pattern)).Select(p => p.Title).ToListAsync())
            .Select(t => $"“{t}” eğitimi"));
        places.AddRange((await db.Staff.Where(s => EF.Functions.Like(s.PhotoUrl, pattern)).Select(s => s.FullName).ToListAsync())
            .Select(n => $"{n} (personel)"));
        places.AddRange((await db.Themes.Where(t => EF.Functions.Like(t.ImageUrl, pattern)).Select(t => t.Title).ToListAsync())
            .Select(t => $"“{t}” teması"));
        var settings = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM "SiteSettings" WHERE "HeaderStyle"::text LIKE {pattern} OR "FooterStyle"::text LIKE {pattern}
            """).SingleAsync();
        if (settings > 0) places.Add("site üst/alt bölüm ayarları");
        return places;
    }

    /// <summary>
    /// Adds library rows for stored media that has none yet (files uploaded before the library existed, seed
    /// images). Runs on startup; idempotent.
    /// </summary>
    public static async Task ImportExistingAsync(AppDbContext db, IFileStorage storage, CancellationToken ct = default)
    {
        var known = (await db.MediaAssets.Select(a => a.Url).ToListAsync(ct)).ToHashSet();
        var added = 0;
        await foreach (var o in storage.ListAsync(StorageKeys.Media, ct))
        {
            var url = MediaService.RoutePrefix + o.Key[StorageKeys.Media.Length..];
            if (!known.Add(url)) continue;
            var name = o.Key[StorageKeys.Media.Length..];
            db.MediaAssets.Add(new MediaAsset
            {
                Url = url, Folder = name.StartsWith("seed/") ? MediaFolders.Seed : MediaFolders.Default,
                OriginalName = Path.GetFileName(name), ContentType = StorageKeys.ContentTypeOf(name), Size = o.Size,
            });
            added++;
        }
        if (added > 0) await db.SaveChangesAsync(ct);
    }
}
