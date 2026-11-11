using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Push;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Feed;

public record FeedPostDto(
    Guid Id, string Text, List<ImageDto> Images, Guid? ProgramId, string? ProgramTitle, string AuthorName,
    DateTime? PublishedAt, DateTime CreatedAt, DateTime UpdatedAt);

/// <summary>An image of the "Akış" folder; <c>UsedIn</c>: number of posts showing it.</summary>
public record FeedImageDto(ImageDto Image, string OriginalName, DateTime CreatedAt, int UsedIn);

/// <summary><c>ProgramId</c> null = every guardian. <c>Publish</c>: share now (otherwise saved as a draft).</summary>
public record FeedPostInput([MaxLength(5000)] string? Text, List<Guid>? ImageIds, Guid? ProgramId, bool Publish);

public static class FeedEndpoints
{
    public const int MaxImages = 10;

    public static void MapFeed(this IEndpointRouteBuilder app)
    {
        // Signed private files (children's photos, feed images). The link itself is the permission.
        app.MapGet(PrivateFileService.Route + "{id:guid}", async (Guid id, long e, string? s, AppDbContext db, PrivateFileService files,
            HttpContext ctx, CancellationToken ct) =>
        {
            if (!files.Verify(id, e, s)) return Results.NotFound();
            var file = await db.PrivateFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
            if (file is null) return Results.NotFound();
            // Cache until the link expires — or, on Spaces, while the pre-signed URL behind the redirect is valid.
            var untilLink = Math.Max(0, e - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var maxAge = (long)Math.Min(untilLink, files.MaxCache.TotalSeconds);
            ctx.Response.Headers.CacheControl = $"private, max-age={maxAge}";
            return await files.ServeAsync(file, ct);
        }).AllowAnonymous().ExcludeFromDescription();

        var admin = app.MapGroup("/api/admin").WithTags("Feed");
        var feed = admin.MapGroup("/feed").RequireAuthorization(Permissions.FeedManage);

        // "Akış" folder: every feed image is kept here (private) and can be used again in later posts.
        // Browser resizes before upload.
        feed.MapPost("/library", async (IFormFile file, int? width, int? height, ClaimsPrincipal me, AppDbContext db,
            PrivateFileService files, CancellationToken ct) =>
        {
            var saved = await files.SaveAsync(file, width ?? 0, height ?? 0, me.UserId(), ct);
            if (saved is null) return new Errors().Check(false, "file", "Sadece JPG, PNG veya WEBP (en fazla 8 MB) yüklenebilir.").ToResult();
            saved.Folder = PrivateFolders.Feed;
            saved.OriginalName = Path.GetFileName(file.FileName ?? "").Trim();
            db.PrivateFiles.Add(saved);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new FeedImageDto(new ImageDto(saved.Id, files.Url(saved.Id), saved.Width, saved.Height), saved.OriginalName, saved.CreatedAt, 0));
        }).DisableAntiforgery();

        feed.MapGet("/library", async (AppDbContext db, PrivateFileService files) =>
        {
            var rows = await db.PrivateFiles.AsNoTracking().Where(f => f.Folder == PrivateFolders.Feed)
                .OrderByDescending(f => f.CreatedAt).Take(500).ToListAsync();
            var usage = await UsageCountsAsync(db);
            return rows.Select(f => new FeedImageDto(new ImageDto(f.Id, files.Url(f.Id), f.Width, f.Height), f.OriginalName, f.CreatedAt,
                usage.GetValueOrDefault(f.Id))).ToList();
        });

        // Removing from the folder deletes the file; images still used in a post stay.
        feed.MapDelete("/library/{id:guid}", async (Guid id, AppDbContext db, PrivateFileService files) =>
        {
            var file = await db.PrivateFiles.FirstOrDefaultAsync(f => f.Id == id && f.Folder == PrivateFolders.Feed);
            if (file is null) return Results.NotFound();
            if ((await UsageCountsAsync(db)).GetValueOrDefault(id) is var used and > 0)
                return ApiResults.Conflict($"Bu görsel {used} gönderide kullanılıyor; önce gönderilerden çıkarın.");
            db.PrivateFiles.Remove(file);
            await db.SaveChangesAsync();
            await files.DeleteAsync(file);
            return Results.NoContent();
        });

        feed.MapGet("/", async (AppDbContext db, PrivateFileService files) =>
            await ToDtosAsync(await db.FeedPosts.AsNoTracking().Include(p => p.Program)
                .OrderByDescending(p => p.PublishedAt == null).ThenByDescending(p => p.PublishedAt ?? p.CreatedAt).Take(200).ToListAsync(), db, files));

        feed.MapPost("/", async (FeedPostInput input, ClaimsPrincipal me, AppDbContext db, PrivateFileService files, Notifier notifier) =>
        {
            var post = new FeedPost { AuthorUserId = me.UserId(), AuthorName = me.FindFirstValue("name") ?? "" };
            if (await ApplyAsync(post, input, db, files) is { } error) return error;
            db.FeedPosts.Add(post);
            await db.SaveChangesAsync();
            if (post.PublishedAt is not null) await NotifyAsync(post, notifier);
            return Results.Ok(await OneAsync(post.Id, db, files));
        });

        feed.MapPut("/{id:guid}", async (Guid id, FeedPostInput input, AppDbContext db, PrivateFileService files, Notifier notifier) =>
        {
            var post = await db.FeedPosts.FirstOrDefaultAsync(p => p.Id == id);
            if (post is null) return Results.NotFound();
            var wasPublished = post.PublishedAt is not null;
            if (await ApplyAsync(post, input, db, files) is { } error) return error;
            await db.SaveChangesAsync();
            // A draft published now (re-publishing an edited post does not notify again).
            if (!wasPublished && post.PublishedAt is not null) await NotifyAsync(post, notifier);
            return Results.Ok(await OneAsync(id, db, files));
        });

        feed.MapPost("/{id:guid}/unpublish", async (Guid id, AppDbContext db, PrivateFileService files) =>
        {
            var post = await db.FeedPosts.FirstOrDefaultAsync(p => p.Id == id);
            if (post is null) return Results.NotFound();
            post.PublishedAt = null;
            await db.SaveChangesAsync();
            return Results.Ok(await OneAsync(id, db, files));
        });

        // The images stay in the "Akış" folder.
        feed.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.FeedPosts.Where(p => p.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound());
    }

    /// <summary>Guardians who can see the post (all, or those with a child in its program).</summary>
    private static async Task NotifyAsync(FeedPost post, Notifier notifier)
    {
        var text = post.Text.ReplaceLineEndings(" ").Trim();
        var body = text.Length > 0 ? text : post.ImageIds.Count > 1 ? $"{post.ImageIds.Count} yeni görsel paylaşıldı." : "Yeni bir görsel paylaşıldı.";
        await notifier.SendAsync(await notifier.GuardiansOfProgramAsync(post.ProgramId), NotificationKind.FeedPost,
            "Okulum Yeryüzü'nden yeni paylaşım", body, "/veli");
    }

    private static async Task<IResult?> ApplyAsync(FeedPost post, FeedPostInput input, AppDbContext db, PrivateFileService files)
    {
        var text = input.Text?.Trim() ?? "";
        var images = (input.ImageIds ?? []).Distinct().ToList();
        // Only images of the "Akış" folder: a child's photo (visible to that child's guardians only) can never be
        // put in a post that goes to everyone.
        var known = await db.PrivateFiles.Where(f => images.Contains(f.Id) && f.Folder == PrivateFolders.Feed)
            .Select(f => f.Id).ToListAsync();
        var errors = new Errors()
            .Check(text.Length > 0 || images.Count > 0, "text", "Bir metin yazın ya da görsel ekleyin.")
            .Check(images.Count <= MaxImages, "imageIds", $"En fazla {MaxImages} görsel eklenebilir.")
            .Check(known.Count == images.Count, "imageIds", "Görsellerden biri Akış klasöründe bulunamadı.");
        if (input.ProgramId is { } pid)
            errors.Check(await db.Programs.AnyAsync(p => p.Id == pid), "programId", "Eğitim bulunamadı.");
        if (errors.Any) return errors.ToResult();

        post.Text = text;
        post.ImageIds = images;
        post.ProgramId = input.ProgramId;
        if (input.Publish) post.PublishedAt ??= DateTime.UtcNow;
        return null;
    }

    /// <summary>How many posts use each feed image (the posts table is small; ids live in a jsonb list).</summary>
    private static async Task<Dictionary<Guid, int>> UsageCountsAsync(AppDbContext db) =>
        (await db.FeedPosts.AsNoTracking().Select(p => p.ImageIds).ToListAsync())
            .SelectMany(ids => ids.Distinct()).GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());

    private static async Task<FeedPostDto> OneAsync(Guid id, AppDbContext db, PrivateFileService files) =>
        (await ToDtosAsync([await db.FeedPosts.AsNoTracking().Include(p => p.Program).FirstAsync(p => p.Id == id)], db, files))[0];

    public static async Task<List<FeedPostDto>> ToDtosAsync(List<FeedPost> posts, AppDbContext db, PrivateFileService files)
    {
        var images = await ImagesAsync(posts.SelectMany(p => p.ImageIds), db, files);
        return posts.Select(p => new FeedPostDto(
            p.Id, p.Text, p.ImageIds.Where(images.ContainsKey).Select(i => images[i]).ToList(), p.ProgramId, p.Program?.Title,
            p.AuthorName, p.PublishedAt, p.CreatedAt, p.UpdatedAt)).ToList();
    }

    public static async Task<Dictionary<Guid, ImageDto>> ImagesAsync(IEnumerable<Guid> ids, AppDbContext db, PrivateFileService files)
    {
        var list = ids.Distinct().ToList();
        if (list.Count == 0) return [];
        return await db.PrivateFiles.AsNoTracking().Where(f => list.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => new ImageDto(f.Id, files.Url(f.Id), f.Width, f.Height));
    }
}
