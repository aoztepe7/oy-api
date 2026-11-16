using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Classroom;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Meetings;

public record MeetingDto(
    Guid Id, Guid EnrollmentId, string ChildName, Guid ProgramId, string ProgramTitle, string ParentName,
    DateOnly Date, MeetingChannel Channel, string With, string Text, string AuthorName,
    DateTime CreatedAt, DateTime UpdatedAt,
    /// <summary>The current user wrote it, or may edit enrollments.</summary>
    bool CanEdit);

public record MeetingInput(Guid EnrollmentId, DateOnly? Date, MeetingChannel Channel, string? With, string? Text);

/// <summary>A child the meeting can be about (picker of the meetings page).</summary>
public record MeetingChild(Guid EnrollmentId, string ChildName, string ProgramTitle, string ParentName, EnrollmentStatus Status);

/// <summary>
/// Meetings with a child's parents ("veli görüşme notları"), written outside lessons. Every panel user (teachers
/// included, whatever their role) reads and writes them; only the author — or someone who edits enrollments —
/// changes or deletes one. Guardians never see them.
/// </summary>
public static class MeetingEndpoints
{
    public const int PageMax = 200;

    public static void MapMeetings(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/meetings").WithTags("Guardian meetings").RequireAuthorization(Permissions.Staff);

        // Newest first. `take` grows for "daha fazla göster".
        group.MapGet("/", async (Guid? enrollmentId, Guid? programId, string? q, int? take, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.GuardianMeetings.AsNoTracking();
            if (enrollmentId is { } eid) query = query.Where(m => m.EnrollmentId == eid);
            if (programId is { } pid) query = query.Where(m => m.Enrollment.ProgramId == pid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q.Trim()}%";
                query = query.Where(m => EF.Functions.ILike(m.Enrollment.ChildName, like)
                    || EF.Functions.ILike(m.Enrollment.ParentName, like) || EF.Functions.ILike(m.Text, like)
                    || EF.Functions.ILike(m.With, like) || EF.Functions.ILike(m.Enrollment.Program.Title, like));
            }
            var limit = Math.Clamp(take ?? 50, 1, PageMax);
            var rows = await Project(query.OrderByDescending(m => m.Date).ThenByDescending(m => m.CreatedAt).Take(limit + 1), me)
                .ToListAsync(ct);
            return Results.Ok(new { items = rows.Take(limit), hasMore = rows.Count > limit });
        });

        group.MapGet("/children", async (string? q, AppDbContext db, CancellationToken ct) =>
        {
            var query = db.Enrollments.AsNoTracking().Where(e => e.Status != EnrollmentStatus.Cancelled);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q.Trim()}%";
                query = query.Where(e => EF.Functions.ILike(e.ChildName, like) || EF.Functions.ILike(e.ParentName, like));
            }
            return await query
                .OrderBy(e => e.Status == EnrollmentStatus.Active ? 0 : e.Status == EnrollmentStatus.Pending ? 1 : 2)
                .ThenBy(e => e.ChildName).ThenByDescending(e => e.StartDate)
                .Take(30)
                .Select(e => new MeetingChild(e.Id, e.ChildName, e.Program.Title, e.ParentName, e.Status))
                .ToListAsync(ct);
        });

        group.MapPost("/", async (MeetingInput input, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            if (!await db.Enrollments.AnyAsync(e => e.Id == input.EnrollmentId, ct))
                return new Errors().Check(false, "enrollmentId", "Çocuğu seçin.").ToResult();
            var m = new GuardianMeeting
            {
                EnrollmentId = input.EnrollmentId, Text = "",
                AuthorUserId = me.UserId(), AuthorName = me.FindFirstValue("name") ?? "",
            };
            if (Apply(m, input) is { } error) return error;
            db.GuardianMeetings.Add(m);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await Project(db.GuardianMeetings.Where(x => x.Id == m.Id), me).FirstAsync(ct));
        });

        group.MapPut("/{id:guid}", async (Guid id, MeetingInput input, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.GuardianMeetings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            if (!CanEdit(m.AuthorUserId, me)) return Forbidden();
            if (Apply(m, input) is { } error) return error;
            await db.SaveChangesAsync(ct);
            return Results.Ok(await Project(db.GuardianMeetings.Where(x => x.Id == m.Id), me).FirstAsync(ct));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var m = await db.GuardianMeetings.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) return Results.NotFound();
            if (!CanEdit(m.AuthorUserId, me)) return Forbidden();
            db.GuardianMeetings.Remove(m);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Validates and copies the input (the child is set on create only).</summary>
    public static IResult? Apply(GuardianMeeting m, MeetingInput input)
    {
        var text = input.Text?.Trim() ?? "";
        var with = input.With?.Trim() ?? "";
        var today = ClassroomEndpoints.Today();
        var errors = new Errors()
            .Check(text.Length > 0, "text", "Görüşmede konuşulanları yazın.")
            .Check(text.Length <= MeetingRules.TextMax, "text", $"En fazla {MeetingRules.TextMax} karakter.")
            .Check(with.Length <= MeetingRules.WithMax, "with", $"En fazla {MeetingRules.WithMax} karakter.")
            .Check(input.Date is not null, "date", "Görüşme tarihini seçin.")
            .Check(input.Date is null || input.Date <= today, "date", "Görüşme tarihi gelecekte olamaz.")
            .Check(input.Date is null || input.Date >= today.AddYears(-10), "date", "Geçerli bir tarih seçin.")
            .Check(Enum.IsDefined(input.Channel), "channel", "Görüşme şeklini seçin.");
        if (errors.Any) return errors.ToResult();
        m.Text = text;
        m.With = with;
        m.Date = input.Date!.Value;
        m.Channel = input.Channel;
        return null;
    }

    public static bool CanEdit(Guid? authorId, ClaimsPrincipal me) =>
        (authorId is { } a && a == me.UserId()) || me.Can(Permissions.EnrollmentsWrite);

    private static IResult Forbidden() =>
        Results.Problem(title: "Görüşme notunu yalnızca yazan kişi değiştirebilir.", statusCode: StatusCodes.Status403Forbidden);

    private static IQueryable<MeetingDto> Project(IQueryable<GuardianMeeting> query, ClaimsPrincipal me)
    {
        var userId = me.UserId();
        var editsAll = me.Can(Permissions.EnrollmentsWrite);
        return query.Select(m => new MeetingDto(
            m.Id, m.EnrollmentId, m.Enrollment.ChildName, m.Enrollment.ProgramId, m.Enrollment.Program.Title, m.Enrollment.ParentName,
            m.Date, m.Channel, m.With, m.Text, m.AuthorName, m.CreatedAt, m.UpdatedAt,
            editsAll || (m.AuthorUserId != null && m.AuthorUserId == userId)));
    }
}
