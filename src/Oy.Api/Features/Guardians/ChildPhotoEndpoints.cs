using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Guardians;

/// <summary>
/// A photo of one child as the panel shows it on the enrollment: lesson photos the child is tagged in and photos
/// added to the child outside lessons. <c>Date</c>: lesson date or the date it was taken.
/// </summary>
public record ChildPhotoDto(
    Guid Id, ImageDto Image, string Caption, DateOnly? Date, bool FromLesson, string? LessonTitle, DateTime? PublishedAt,
    string UploadedByName, DateTime CreatedAt, bool CanEdit);

public record ChildPhotoUpdate([MaxLength(300)] string? Caption, DateOnly? TakenOn);

/// <summary>
/// Photos added to a child outside lessons (e.g. an older photo). They behave like lesson photos for the
/// guardians — in the feed once shared and in the child's album — but belong to that child only.
/// Who: users who manage programs or the guardians' feed.
/// </summary>
public static class ChildPhotoEndpoints
{
    public const int MaxPerChild = 500;

    public static void MapChildPhotos(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Child photos")
            .RequireAnyPermission(Permissions.ProgramsWrite, Permissions.FeedManage);

        group.MapGet("/enrollments/{id:guid}/photos", async (Guid id, AppDbContext db, PrivateFileService files) =>
        {
            if (!await db.Enrollments.AnyAsync(e => e.Id == id)) return Results.NotFound();
            var rows = await db.ClassPhotoTags.AsNoTracking().Where(t => t.EnrollmentId == id)
                .Select(t => t.Photo).Include(p => p.File).Include(p => p.Session).ThenInclude(s => s!.Program)
                .ToListAsync();
            return Results.Ok(rows
                .OrderByDescending(p => p.Session?.Date ?? p.TakenOn ?? DateOnly.FromDateTime(p.CreatedAt)).ThenBy(p => p.CreatedAt)
                .Select(p => ToDto(p, files)).ToList());
        });

        // One photo per request; the browser resizes it first. Lands as a draft (not visible to guardians).
        group.MapPost("/enrollments/{id:guid}/photos", async (Guid id, IFormFile file, int? width, int? height, DateOnly? takenOn,
            ClaimsPrincipal me, AppDbContext db, PrivateFileService files, CancellationToken ct) =>
        {
            var enrollment = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (enrollment is null) return Results.NotFound();
            if (enrollment.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Completed))
                return ApiResults.Conflict("Fotoğraf yalnızca onaylanmış kayıtlara eklenebilir.");
            if (await db.ClassPhotoTags.CountAsync(t => t.EnrollmentId == id && t.Photo.SessionId == null, ct) >= MaxPerChild)
                return ApiResults.Conflict($"Bir çocuğa ders dışı en fazla {MaxPerChild} fotoğraf eklenebilir.");
            if (takenOn is { } d && d > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
                return new Errors().Check(false, "takenOn", "Çekim tarihi gelecekte olamaz.").ToResult();

            var saved = await files.SaveAsync(file, width ?? 0, height ?? 0, me.UserId(), ct);
            if (saved is null) return new Errors().Check(false, "file", "Sadece JPG, PNG veya WEBP (en fazla 8 MB) yüklenebilir.").ToResult();
            saved.Folder = PrivateFolders.Child;
            saved.OriginalName = Path.GetFileName(file.FileName ?? "").Trim();
            db.PrivateFiles.Add(saved);
            var photo = new ClassPhoto
            {
                SessionId = null, TakenOn = takenOn, ProgramId = enrollment.ProgramId, FileId = saved.Id,
                UploadedByUserId = me.UserId(), UploadedByName = me.FindFirstValue("name") ?? "",
                Tags = [new ClassPhotoTag { EnrollmentId = id }],
            };
            photo.File = saved;
            db.ClassPhotos.Add(photo);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(photo, files));
        }).DisableAntiforgery();

        group.MapPut("/child-photos/{photoId:guid}", async (Guid photoId, ChildPhotoUpdate input, AppDbContext db, PrivateFileService files) =>
        {
            var photo = await OwnAsync(db, photoId);
            if (photo is null) return Results.NotFound();
            photo.Caption = input.Caption?.Trim() ?? "";
            photo.TakenOn = input.TakenOn;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(photo, files));
        });

        // Shares every draft photo added to this child outside lessons.
        group.MapPost("/enrollments/{id:guid}/photos/publish", async (Guid id, AppDbContext db) =>
        {
            var drafts = await db.ClassPhotos.Where(p => p.SessionId == null && p.PublishedAt == null && p.Tags.Any(t => t.EnrollmentId == id))
                .ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var p in drafts) p.PublishedAt = now;
            await db.SaveChangesAsync();
            return Results.Ok(new { published = drafts.Count });
        });

        group.MapPost("/child-photos/{photoId:guid}/unpublish", async (Guid photoId, AppDbContext db, PrivateFileService files) =>
        {
            var photo = await OwnAsync(db, photoId);
            if (photo is null) return Results.NotFound();
            photo.PublishedAt = null;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(photo, files));
        });

        group.MapDelete("/child-photos/{photoId:guid}", async (Guid photoId, AppDbContext db, PrivateFileService files) =>
        {
            var photo = await OwnAsync(db, photoId);
            if (photo is null) return Results.NotFound();
            db.ClassPhotos.Remove(photo);
            db.PrivateFiles.Remove(photo.File);
            await db.SaveChangesAsync();
            await files.DeleteAsync(photo.File);
            return Results.NoContent();
        });
    }

    /// <summary>Only photos added outside lessons are edited here; lesson photos are managed in the classroom.</summary>
    private static Task<ClassPhoto?> OwnAsync(AppDbContext db, Guid id) =>
        db.ClassPhotos.Include(p => p.File).FirstOrDefaultAsync(p => p.Id == id && p.SessionId == null);

    private static ChildPhotoDto ToDto(ClassPhoto p, PrivateFileService files) => new(
        p.Id, new ImageDto(p.FileId, files.Url(p.FileId), p.File.Width, p.File.Height), p.Caption,
        p.Session?.Date ?? p.TakenOn, p.SessionId is not null, p.Session?.Program.Title, p.PublishedAt, p.UploadedByName, p.CreatedAt,
        CanEdit: p.SessionId is null);
}
