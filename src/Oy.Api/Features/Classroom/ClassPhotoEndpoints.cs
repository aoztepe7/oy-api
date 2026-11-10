using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Classroom;

public record ClassPhotoDto(
    Guid Id, ImageDto Image, string Caption, string UploadedByName, DateTime CreatedAt, DateTime? PublishedAt,
    List<Guid> EnrollmentIds, bool CanDelete);

public record PhotoChild(Guid EnrollmentId, string ChildName, bool InPool);

public record ClassPhotosDto(List<ClassPhotoDto> Photos, List<PhotoChild> Children);

/// <summary>Who is in the photo; changing it on a shared photo changes who sees it right away.</summary>
public record PhotoUpdate(List<Guid> EnrollmentIds, [MaxLength(300)] string? Caption);

public record PublishResult(int Published, int Untagged, ClassPhotosDto Photos);

/// <summary>
/// Lesson photos: teachers upload them in the classroom, tag the children in each, then share. Every child's
/// guardians see only the shared photos their child is tagged in.
/// </summary>
public static class ClassPhotoEndpoints
{
    public const int MaxPhotosPerSession = 300;

    public static void MapClassPhotos(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/classroom").WithTags("Classroom").RequireAuthorization(Permissions.Classroom);

        group.MapGet("/sessions/{id:guid}/photos", async (Guid id, ClaimsPrincipal me, AppDbContext db, PrivateFileService files) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return s is null ? Results.NotFound() : Results.Ok(await BuildAsync(s, viewer, db, files));
        });

        // One photo per request (the browser uploads them in parallel and shows progress per photo).
        group.MapPost("/sessions/{id:guid}/photos", async (Guid id, IFormFile file, int? width, int? height, ClaimsPrincipal me,
            AppDbContext db, PrivateFileService files, ClassroomNotifier notify, CancellationToken ct) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return Results.NotFound();
            if (s.StartedAt is null) return ApiResults.Conflict("Fotoğraf eklemek için önce dersi başlatın.");
            if (await db.ClassPhotos.CountAsync(p => p.SessionId == id, ct) >= MaxPhotosPerSession)
                return ApiResults.Conflict($"Bir derse en fazla {MaxPhotosPerSession} fotoğraf eklenebilir.");

            var saved = await files.SaveAsync(file, width ?? 0, height ?? 0, viewer.UserId, ct);
            if (saved is null) return new Errors().Check(false, "file", "Sadece JPG, PNG veya WEBP (en fazla 8 MB) yüklenebilir.").ToResult();
            saved.Folder = PrivateFolders.Lesson;
            db.PrivateFiles.Add(saved);
            var photo = new ClassPhoto
            {
                SessionId = id, ProgramId = s.ProgramId, FileId = saved.Id, UploadedByUserId = viewer.UserId, UploadedByName = viewer.Name,
            };
            db.ClassPhotos.Add(photo);
            await db.SaveChangesAsync(ct);
            await notify.SessionChangedAsync(id, s.Date);
            return Results.Ok(ToDto(photo, saved, [], viewer, files));
        }).DisableAntiforgery();

        group.MapPut("/photos/{photoId:guid}", async (Guid photoId, PhotoUpdate input, ClaimsPrincipal me, AppDbContext db,
            PrivateFileService files, ClassroomNotifier notify) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var photo = await db.ClassPhotos.Include(p => p.Tags).Include(p => p.File).FirstOrDefaultAsync(p => p.Id == photoId);
            if (photo is null) return Results.NotFound();
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == photo.SessionId);
            if (s is null) return Results.NotFound();

            var wanted = (input.EnrollmentIds ?? []).Distinct().ToList();
            var roster = await ClassroomEndpoints.Roster(db, s).Where(e => wanted.Contains(e.Id)).Select(e => e.Id).ToListAsync();
            if (roster.Count != wanted.Count)
                return new Errors().Check(false, "enrollmentIds", "Seçilen çocuklardan biri bu dersin listesinde değil.").ToResult();

            photo.Tags.RemoveAll(t => !wanted.Contains(t.EnrollmentId));
            foreach (var e in wanted.Where(e => photo.Tags.All(t => t.EnrollmentId != e)))
                photo.Tags.Add(new ClassPhotoTag { PhotoId = photo.Id, EnrollmentId = e });
            photo.Caption = input.Caption?.Trim() ?? "";
            // A shared photo left with nobody in it would show nowhere: take it back to drafts.
            if (photo.Tags.Count == 0) photo.PublishedAt = null;
            await db.SaveChangesAsync();
            await notify.SessionChangedAsync(s.Id, s.Date);
            return Results.Ok(ToDto(photo, photo.File, photo.Tags.Select(t => t.EnrollmentId).ToList(), viewer, files));
        });

        group.MapDelete("/photos/{photoId:guid}", async (Guid photoId, ClaimsPrincipal me, AppDbContext db, PrivateFileService files,
            ClassroomNotifier notify) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var photo = await db.ClassPhotos.Include(p => p.File).FirstOrDefaultAsync(p => p.Id == photoId);
            if (photo is null) return Results.NotFound();
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == photo.SessionId);
            if (s is null) return Results.NotFound();
            if (!CanDelete(photo, viewer))
                return Results.Problem(title: "Yalnızca kendi yüklediğiniz fotoğrafları silebilirsiniz.", statusCode: StatusCodes.Status403Forbidden);
            db.ClassPhotos.Remove(photo);
            db.PrivateFiles.Remove(photo.File);
            await db.SaveChangesAsync();
            await files.DeleteAsync(photo.File);
            await notify.SessionChangedAsync(s.Id, s.Date);
            return Results.NoContent();
        });

        // Shares every not-yet-shared photo that has at least one child tagged; untagged ones stay as drafts.
        group.MapPost("/sessions/{id:guid}/photos/publish", async (Guid id, ClaimsPrincipal me, AppDbContext db, PrivateFileService files,
            ClassroomNotifier notify) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            var drafts = await db.ClassPhotos.Where(p => p.SessionId == id && p.PublishedAt == null)
                .Select(p => new { Photo = p, Tagged = p.Tags.Any() }).ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var d in drafts.Where(d => d.Tagged)) d.Photo.PublishedAt = now;
            await db.SaveChangesAsync();
            await notify.SessionChangedAsync(id, s.Date);
            return Results.Ok(new PublishResult(drafts.Count(d => d.Tagged), drafts.Count(d => !d.Tagged), await BuildAsync(s, viewer, db, files)));
        });

        group.MapPost("/photos/{photoId:guid}/unpublish", async (Guid photoId, ClaimsPrincipal me, AppDbContext db, PrivateFileService files,
            ClassroomNotifier notify) =>
        {
            var viewer = await ClassroomEndpoints.ViewerAsync(me, db);
            var photo = await db.ClassPhotos.Include(p => p.Tags).Include(p => p.File).FirstOrDefaultAsync(p => p.Id == photoId);
            if (photo is null) return Results.NotFound();
            var s = await ClassroomEndpoints.Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == photo.SessionId);
            if (s is null) return Results.NotFound();
            photo.PublishedAt = null;
            await db.SaveChangesAsync();
            await notify.SessionChangedAsync(s.Id, s.Date);
            return Results.Ok(ToDto(photo, photo.File, photo.Tags.Select(t => t.EnrollmentId).ToList(), viewer, files));
        });
    }

    private static bool CanDelete(ClassPhoto p, ClassroomEndpoints.Viewer v) => v.IsManager || p.UploadedByUserId == v.UserId;

    private static ClassPhotoDto ToDto(ClassPhoto p, PrivateFile f, List<Guid> tags, ClassroomEndpoints.Viewer v, PrivateFileService files) =>
        new(p.Id, new ImageDto(f.Id, files.Url(f.Id), f.Width, f.Height), p.Caption, p.UploadedByName, p.CreatedAt, p.PublishedAt, tags, CanDelete(p, v));

    private static async Task<ClassPhotosDto> BuildAsync(Session s, ClassroomEndpoints.Viewer viewer, AppDbContext db, PrivateFileService files)
    {
        var photos = await db.ClassPhotos.AsNoTracking().Include(p => p.File).Include(p => p.Tags)
            .Where(p => p.SessionId == s.Id).OrderBy(p => p.CreatedAt).ToListAsync();
        var pool = await db.Attendance.Where(a => a.SessionId == s.Id && a.Status != AttendanceStatus.Absent)
            .Select(a => a.EnrollmentId).ToListAsync();
        var children = await ClassroomEndpoints.Roster(db, s).OrderBy(e => e.ChildName)
            .Select(e => new { e.Id, e.ChildName }).ToListAsync();
        return new ClassPhotosDto(
            photos.Select(p => ToDto(p, p.File, p.Tags.Select(t => t.EnrollmentId).ToList(), viewer, files)).ToList(),
            // Children who came first: they are the ones in the photos.
            children.Select(c => new PhotoChild(c.Id, c.ChildName, pool.Contains(c.Id)))
                .OrderByDescending(c => c.InPool).ThenBy(c => c.ChildName).ToList());
    }
}
