using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Feed;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Guardians;

public record GuardianChild(
    Guid EnrollmentId, string ChildName, DateOnly? BirthDate, Guid ProgramId, string ProgramTitle, ProgramType ProgramType,
    GuardianRelation Relation, int PhotoCount);

public record FeedChild(Guid EnrollmentId, string ChildName);

/// <summary>
/// One card in the guardian's feed. <c>Kind</c> "post": an announcement from the school; "photos": the photos of
/// one child from one lesson, shared together.
/// </summary>
public record FeedItem(
    string Kind, string Id, DateTime At, string? Text, List<ImageDto> Images, string? AuthorName, string? ProgramTitle,
    FeedChild? Child, Guid? SessionId, DateOnly? LessonDate);

/// <summary><c>Next</c>: pass as <c>before</c> to load older items; null = nothing older.</summary>
public record FeedPage(List<FeedItem> Items, DateTime? Next);

public record GalleryPhoto(Guid Id, ImageDto Image, string Caption, DateTime PublishedAt);

/// <summary>A group in a child's album: one lesson, or (<c>FromLesson</c> false) photos added outside lessons on one day.</summary>
public record GalleryLesson(Guid? SessionId, DateOnly Date, string ProgramTitle, bool FromLesson, List<GalleryPhoto> Photos);

/// <summary>
/// The guardian app: only data about the guardian's own children (approved enrollments they are linked to) and
/// the school's shared posts. Every photo link is signed per request.
/// </summary>
public static class GuardianEndpoints
{
    public static void MapGuardian(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardian").WithTags("Guardian app").RequireAuthorization(Permissions.Guardian);

        group.MapGet("/children", async (ClaimsPrincipal me, AppDbContext db) =>
        {
            var uid = me.UserId();
            return await Links(db, uid)
                .OrderBy(l => l.Enrollment.ChildName).ThenByDescending(l => l.Enrollment.StartDate)
                .Select(l => new GuardianChild(
                    l.EnrollmentId, l.Enrollment.ChildName, l.Enrollment.ChildBirthDate, l.Enrollment.ProgramId,
                    l.Enrollment.Program.Title, l.Enrollment.Program.Type, l.Relation,
                    db.ClassPhotoTags.Count(t => t.EnrollmentId == l.EnrollmentId && t.Photo.PublishedAt != null)))
                .ToListAsync();
        });

        group.MapGet("/feed", async (DateTime? before, int? limit, ClaimsPrincipal me, AppDbContext db, PrivateFileService files) =>
        {
            var take = Math.Clamp(limit ?? 15, 1, 50);
            var until = before ?? DateTime.UtcNow.AddMinutes(1);
            var mine = await Links(db, me.UserId()).Select(l => new { l.EnrollmentId, l.Enrollment.ProgramId }).ToListAsync();
            var enrollmentIds = mine.Select(m => m.EnrollmentId).ToList();
            var programIds = mine.Select(m => m.ProgramId).Distinct().ToList();

            var posts = await db.FeedPosts.AsNoTracking().Include(p => p.Program)
                .Where(p => p.PublishedAt != null && p.PublishedAt < until && (p.ProgramId == null || programIds.Contains(p.ProgramId.Value)))
                .OrderByDescending(p => p.PublishedAt).Take(take).ToListAsync();
            var postDtos = await FeedEndpoints.ToDtosAsync(posts, db, files);

            var photos = await db.ClassPhotoTags.AsNoTracking()
                .Where(t => enrollmentIds.Contains(t.EnrollmentId) && t.Photo.PublishedAt != null && t.Photo.PublishedAt < until)
                .OrderByDescending(t => t.Photo.PublishedAt).ThenBy(t => t.Photo.CreatedAt)
                .Take(take * 30)
                .Select(t => new
                {
                    t.EnrollmentId, t.Enrollment.ChildName, t.Photo.SessionId, t.Photo.FileId, PublishedAt = t.Photo.PublishedAt!.Value,
                    // Lesson date, or the date a photo added outside lessons was taken.
                    Date = t.Photo.Session != null ? (DateOnly?)t.Photo.Session.Date : t.Photo.TakenOn,
                    ProgramTitle = t.Photo.Program.Title,
                })
                .ToListAsync();
            var images = await FeedEndpoints.ImagesAsync(photos.Select(p => p.FileId), db, files);

            var items = postDtos.Select(p => new FeedItem(
                    "post", p.Id.ToString(), p.PublishedAt!.Value, p.Text, p.Images, p.AuthorName, p.ProgramTitle, null, null, null))
                .Concat(photos
                    .GroupBy(p => (p.SessionId, p.EnrollmentId, p.PublishedAt))
                    .Select(g => new FeedItem(
                        "photos", $"{g.Key.SessionId ?? Guid.Empty:N}-{g.Key.EnrollmentId:N}-{g.Key.PublishedAt.Ticks}", g.Key.PublishedAt, null,
                        g.Where(p => images.ContainsKey(p.FileId)).Select(p => images[p.FileId]).ToList(), null, g.First().ProgramTitle,
                        new FeedChild(g.Key.EnrollmentId, g.First().ChildName), g.Key.SessionId,
                        g.Select(p => p.Date).Max())))
                .OrderByDescending(i => i.At)
                .ToList();

            // Keep items sharing the last timestamp together, so paging with "before" never skips any.
            var page = items.Take(take).ToList();
            if (page.Count == take) page.AddRange(items.Skip(take).TakeWhile(i => i.At == page[^1].At));
            var more = items.Count > page.Count || posts.Count == take;
            return Results.Ok(new FeedPage(page, more && page.Count > 0 ? page[^1].At : null));
        });

        group.MapGet("/children/{enrollmentId:guid}/photos", async (Guid enrollmentId, ClaimsPrincipal me, AppDbContext db, PrivateFileService files) =>
        {
            if (!await Links(db, me.UserId()).AnyAsync(l => l.EnrollmentId == enrollmentId)) return Results.NotFound();
            var rows = await db.ClassPhotoTags.AsNoTracking()
                .Where(t => t.EnrollmentId == enrollmentId && t.Photo.PublishedAt != null)
                .Select(t => new
                {
                    t.PhotoId, t.Photo.FileId, t.Photo.Caption, PublishedAt = t.Photo.PublishedAt!.Value, t.Photo.CreatedAt,
                    t.Photo.SessionId, SessionDate = t.Photo.Session != null ? (DateOnly?)t.Photo.Session.Date : null,
                    t.Photo.TakenOn, ProgramTitle = t.Photo.Program.Title,
                })
                .ToListAsync();
            var images = await FeedEndpoints.ImagesAsync(rows.Select(r => r.FileId), db, files);
            // Lesson photos grouped by lesson; photos added outside lessons grouped by the day they were taken
            // (or shared, when no date was given).
            return Results.Ok(rows
                .GroupBy(r => r.SessionId is { } sid
                    ? (Session: (Guid?)sid, Date: r.SessionDate!.Value)
                    : (Session: (Guid?)null, Date: r.TakenOn ?? DateOnly.FromDateTime(r.PublishedAt)))
                .Select(g => new GalleryLesson(g.Key.Session, g.Key.Date, g.First().ProgramTitle, g.Key.Session is not null, g
                    .Where(r => images.ContainsKey(r.FileId)).OrderBy(r => r.CreatedAt)
                    .Select(r => new GalleryPhoto(r.PhotoId, images[r.FileId], r.Caption, r.PublishedAt)).ToList()))
                .OrderByDescending(l => l.Date)
                .ToList());
        });
    }

    /// <summary>The guardian's children: approved (active or completed) enrollments they are linked to.</summary>
    private static IQueryable<GuardianLink> Links(AppDbContext db, Guid? userId) =>
        db.GuardianLinks.AsNoTracking().Where(l => l.UserId == userId &&
            (l.Enrollment.Status == EnrollmentStatus.Active || l.Enrollment.Status == EnrollmentStatus.Completed));
}
