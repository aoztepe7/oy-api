using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Classroom;

public record ClassroomSessionSummary(
    Guid Id, Guid ProgramId, string ProgramTitle, ProgramType ProgramType, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime,
    bool IsCancelled, DateTime? StartedAt, DateTime? EndedAt, int RosterCount, int PresentCount, int NoteCount, List<string> Staff);

public record ClassroomStaff(Guid StaffId, string FullName, AssignmentRole Role, string PhotoUrl);

public record ClassroomChild(
    Guid EnrollmentId, string ChildName, DateOnly? BirthDate, AttendanceStatus? Status, DateTime? ArrivedAt, string MarkedByName, int NoteCount);

public record ClassroomNote(
    Guid Id, Guid EnrollmentId, string ChildName, string Text, List<string> Tags, string AuthorName,
    DateTime CreatedAt, DateTime UpdatedAt, bool CanEdit);

/// <summary>Everything the attendance screen and the live pool need, in one poll.</summary>
public record ClassroomSessionDto(
    Guid Id, Guid ProgramId, string ProgramTitle, ProgramType ProgramType, string Location, DateOnly Date,
    TimeOnly StartTime, TimeOnly EndTime, bool IsCancelled, string Note,
    DateTime? StartedAt, string StartedByName, DateTime? EndedAt, bool CanStart,
    List<ClassroomStaff> Staff, List<ClassroomChild> Children, List<ClassroomNote> Notes, IReadOnlyList<string> NoteTags);

/// <summary>null clears the mark (back to "not marked").</summary>
public record AttendanceInput(AttendanceStatus? Status);

public record NoteInput(Guid EnrollmentId, [Required, MaxLength(2000)] string Text, List<string>? Tags);

public record NoteUpdate([Required, MaxLength(2000)] string Text, List<string>? Tags);

/// <summary>
/// Lesson day ("ders"): start it, take attendance, and write quick notes about the children in the live pool
/// (those marked present / late). Teachers reach only the sessions of programs their staff record is assigned
/// to on that date; users who can edit programs reach every session.
/// </summary>
public static class ClassroomEndpoints
{
    /// <summary>Development areas offered as note tags; fixed so later reports can group by them.</summary>
    public static readonly IReadOnlyList<string> NoteTags =
        ["Sosyal", "Duygusal", "Motor", "Dil ve iletişim", "Bilişsel", "Doğa ile bağ", "Öz bakım", "Yaratıcılık"];

    public static void MapClassroom(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/classroom").WithTags("Classroom").RequireAuthorization(Permissions.Classroom);

        group.MapGet("/sessions", async (DateOnly? date, ClaimsPrincipal me, AppDbContext db) =>
        {
            var viewer = await ViewerAsync(me, db);
            var day = date ?? Today();
            var rows = await Visible(db, viewer).AsNoTracking()
                .Where(s => s.Date == day)
                .OrderBy(s => s.StartTime).ThenBy(s => s.Program.Title)
                .Select(s => new ClassroomSessionSummary(
                    s.Id, s.ProgramId, s.Program.Title, s.Program.Type, s.Date, s.StartTime, s.EndTime, s.IsCancelled,
                    s.StartedAt, s.EndedAt,
                    s.Program.Enrollments.Count(e =>
                        (e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed) &&
                        e.StartDate <= s.Date && (e.EndDate == null || e.EndDate >= s.Date)),
                    s.Attendance.Count(a => a.Status != AttendanceStatus.Absent),
                    s.Notes.Count,
                    s.Program.Assignments.Where(a => a.StartDate <= s.Date && a.EndDate >= s.Date)
                        .OrderBy(a => a.Role).Select(a => a.Staff.FullName).ToList()))
                .AsSplitQuery()
                .ToListAsync();
            return Results.Ok(rows);
        });

        group.MapGet("/sessions/{id:guid}", async (Guid id, ClaimsPrincipal me, AppDbContext db, MediaService media) =>
        {
            var viewer = await ViewerAsync(me, db);
            return await BuildAsync(id, viewer, db, media) is { } dto ? Results.Ok(dto) : Results.NotFound();
        });

        group.MapPost("/sessions/{id:guid}/start", async (Guid id, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var s = await Visible(db, viewer).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (s.IsCancelled) return ApiResults.Conflict("İptal edilen ders başlatılamaz.");
            if (s.Date > Today()) return ApiResults.Conflict("Ders, gününden önce başlatılamaz.");
            if (s.StartedAt is null)
            {
                s.StartedAt = DateTime.UtcNow;
                s.StartedByName = viewer.Name;
                await db.SaveChangesAsync();
            }
            return await ChangedAsync(id, viewer, db, media, notify);
        });

        group.MapPost("/sessions/{id:guid}/finish", async (Guid id, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var s = await Visible(db, viewer).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (s.StartedAt is null) return ApiResults.Conflict("Ders henüz başlatılmadı.");
            s.EndedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
            return await ChangedAsync(id, viewer, db, media, notify);
        });

        group.MapPost("/sessions/{id:guid}/reopen", async (Guid id, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var s = await Visible(db, viewer).FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            s.EndedAt = null;
            await db.SaveChangesAsync();
            return await ChangedAsync(id, viewer, db, media, notify);
        });

        group.MapPut("/sessions/{id:guid}/attendance/{enrollmentId:guid}",
            async (Guid id, Guid enrollmentId, AttendanceInput input, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
            {
                var viewer = await ViewerAsync(me, db);
                var s = await Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
                if (s is null) return Results.NotFound();
                if (Closed(s) is { } closed) return closed;
                if (!await Roster(db, s).AnyAsync(e => e.Id == enrollmentId))
                    return new Errors().Check(false, "enrollmentId", "Çocuk bu dersin listesinde değil.").ToResult();

                var mark = await db.Attendance.FirstOrDefaultAsync(a => a.SessionId == id && a.EnrollmentId == enrollmentId);
                var leavesPool = input.Status is null or AttendanceStatus.Absent;
                if (leavesPool && mark?.InPool == true &&
                    await db.ObservationNotes.AnyAsync(n => n.SessionId == id && n.EnrollmentId == enrollmentId))
                    return ApiResults.Conflict("Bu çocuk için bugün not yazılmış; gelmedi işaretlemeden önce notları silin.");

                if (input.Status is not { } status)
                {
                    if (mark is not null) db.Attendance.Remove(mark);
                }
                else
                {
                    if (mark is null)
                    {
                        mark = new Attendance { SessionId = id, EnrollmentId = enrollmentId };
                        db.Attendance.Add(mark);
                    }
                    var wasInPool = mark.InPool;
                    mark.Status = status;
                    mark.ArrivedAt = status == AttendanceStatus.Absent ? null : wasInPool ? mark.ArrivedAt : DateTime.UtcNow;
                    mark.MarkedByUserId = viewer.UserId;
                    mark.MarkedByName = viewer.Name;
                }
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbUpdateException)
                {
                    // Another teacher marked the same child at the same moment: theirs stands, the screen refreshes.
                    return ApiResults.Conflict("Bu çocuk az önce başka bir öğretmen tarafından işaretlendi.");
                }
                return await ChangedAsync(id, viewer, db, media, notify);
            });

        // "Hepsi geldi": marks every child not yet marked as present.
        group.MapPost("/sessions/{id:guid}/attendance/all-present", async (Guid id, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var s = await Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (Closed(s) is { } closed) return closed;
            var marked = db.Attendance.Where(a => a.SessionId == id).Select(a => a.EnrollmentId);
            var unmarked = await Roster(db, s).Where(e => !marked.Contains(e.Id)).Select(e => e.Id).ToListAsync();
            var now = DateTime.UtcNow;
            db.Attendance.AddRange(unmarked.Select(e => new Attendance
            {
                SessionId = id, EnrollmentId = e, Status = AttendanceStatus.Present, ArrivedAt = now,
                MarkedByUserId = viewer.UserId, MarkedByName = viewer.Name,
            }));
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                return ApiResults.Conflict("Yoklama aynı anda güncellendi; liste yenilendi, tekrar deneyin.");
            }
            return await ChangedAsync(id, viewer, db, media, notify);
        });

        group.MapPost("/sessions/{id:guid}/notes", async (Guid id, NoteInput input, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var s = await Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            if (Closed(s) is { } closed) return closed;
            var inPool = await db.Attendance.AnyAsync(a =>
                a.SessionId == id && a.EnrollmentId == input.EnrollmentId && a.Status != AttendanceStatus.Absent);
            var (text, tags, errors) = ValidateNote(input.Text, input.Tags);
            errors.Check(inPool, "enrollmentId", "Not yalnızca derse gelen çocuklar için yazılabilir.");
            if (errors.Any) return errors.ToResult();

            db.ObservationNotes.Add(new ObservationNote
            {
                ProgramId = s.ProgramId, SessionId = id, EnrollmentId = input.EnrollmentId,
                AuthorUserId = viewer.UserId, AuthorName = viewer.Name, Text = text, Tags = tags,
            });
            await db.SaveChangesAsync();
            return await ChangedAsync(id, viewer, db, media, notify);
        });

        group.MapPut("/notes/{noteId:guid}", async (Guid noteId, NoteUpdate input, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var (note, error) = await EditableNoteAsync(noteId, viewer, db);
            if (error is not null) return error;
            var (text, tags, errors) = ValidateNote(input.Text, input.Tags);
            if (errors.Any) return errors.ToResult();
            note!.Text = text;
            note.Tags = tags;
            await db.SaveChangesAsync();
            return await ChangedAsync(note.SessionId, viewer, db, media, notify);
        });

        group.MapDelete("/notes/{noteId:guid}", async (Guid noteId, ClaimsPrincipal me, AppDbContext db, MediaService media, ClassroomNotifier notify) =>
        {
            var viewer = await ViewerAsync(me, db);
            var (note, error) = await EditableNoteAsync(noteId, viewer, db);
            if (error is not null) return error;
            db.ObservationNotes.Remove(note!);
            await db.SaveChangesAsync();
            return await ChangedAsync(note!.SessionId, viewer, db, media, notify);
        });
    }

    /// <summary>Answers a change with the fresh session and tells everyone watching it (or its day) to reload.</summary>
    private static async Task<IResult> ChangedAsync(Guid id, Viewer viewer, AppDbContext db, MediaService media, ClassroomNotifier notify)
    {
        var dto = await BuildAsync(id, viewer, db, media);
        if (dto is null) return Results.NotFound();
        await notify.SessionChangedAsync(dto.Id, dto.Date);
        return Results.Ok(dto);
    }

    /// <summary>The signed-in user as the classroom sees them.</summary>
    public sealed record Viewer(Guid? UserId, string Name, bool IsManager, Guid? StaffId);

    public static async Task<Viewer> ViewerAsync(ClaimsPrincipal me, AppDbContext db)
    {
        var id = me.UserId();
        var staffId = await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.StaffId).FirstOrDefaultAsync();
        var isManager = me.HasClaim(AuthSetup.PermissionClaim, Permissions.ProgramsWrite);
        return new Viewer(id, me.FindFirstValue("name") ?? "", isManager, staffId);
    }

    /// <summary>Sessions the viewer may open: all for managers, else those of their assignments on that day.</summary>
    public static IQueryable<Session> Visible(AppDbContext db, Viewer viewer) =>
        viewer.IsManager
            ? db.Sessions
            : db.Sessions.Where(s => viewer.StaffId != null && db.Assignments.Any(a =>
                a.ProgramId == s.ProgramId && a.StaffId == viewer.StaffId && a.StartDate <= s.Date && a.EndDate >= s.Date));

    /// <summary>
    /// Children expected in a session: active (or completed) enrollments covering the date, plus anyone already
    /// marked (so a later status change does not hide recorded attendance).
    /// </summary>
    internal static IQueryable<Enrollment> Roster(AppDbContext db, Session s) =>
        db.Enrollments.AsNoTracking().Where(e => e.ProgramId == s.ProgramId && (
            ((e.Status == EnrollmentStatus.Active || e.Status == EnrollmentStatus.Completed) &&
             e.StartDate <= s.Date && (e.EndDate == null || e.EndDate >= s.Date)) ||
            db.Attendance.Any(a => a.SessionId == s.Id && a.EnrollmentId == e.Id)));

    private static IResult? Closed(Session s) =>
        s.StartedAt is null ? ApiResults.Conflict("Önce dersi başlatın.")
        : s.EndedAt is not null ? ApiResults.Conflict("Ders günü kapatıldı; değişiklik için yeniden açın.")
        : null;

    private static (string Text, List<string> Tags, Errors Errors) ValidateNote(string? text, List<string>? tags)
    {
        var t = text?.Trim() ?? "";
        var tagList = (tags ?? []).Select(x => x.Trim()).Distinct().ToList();
        var errors = new Errors()
            .Check(t.Length > 0, "text", "Not boş olamaz.")
            .Check(t.Length <= 2000, "text", "Not en fazla 2000 karakter olabilir.")
            .Check(tagList.All(NoteTags.Contains), "tags", "Geçersiz etiket.");
        return (t, tagList, errors);
    }

    private static async Task<(ObservationNote? Note, IResult? Error)> EditableNoteAsync(Guid noteId, Viewer viewer, AppDbContext db)
    {
        var note = await db.ObservationNotes.FirstOrDefaultAsync(n => n.Id == noteId);
        if (note is null) return (null, Results.NotFound());
        var s = await Visible(db, viewer).AsNoTracking().FirstOrDefaultAsync(x => x.Id == note.SessionId);
        if (s is null) return (null, Results.NotFound());
        if (!viewer.IsManager && note.AuthorUserId != viewer.UserId)
            return (null, Results.Problem(title: "Yalnızca kendi notlarınızı değiştirebilirsiniz.", statusCode: StatusCodes.Status403Forbidden));
        if (s.EndedAt is not null) return (null, ApiResults.Conflict("Ders günü kapatıldı; değişiklik için yeniden açın."));
        return (note, null);
    }

    private static async Task<ClassroomSessionDto?> BuildAsync(Guid id, Viewer viewer, AppDbContext db, MediaService media)
    {
        var s = await Visible(db, viewer).AsNoTracking().Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return null;

        var staff = await db.Assignments.AsNoTracking()
            .Where(a => a.ProgramId == s.ProgramId && a.StartDate <= s.Date && a.EndDate >= s.Date)
            .OrderBy(a => a.Role).ThenBy(a => a.Staff.FullName)
            .Select(a => new ClassroomStaff(a.StaffId, a.Staff.FullName, a.Role, a.Staff.PhotoUrl))
            .ToListAsync();
        var children = await Roster(db, s).OrderBy(e => e.ChildName)
            .Select(e => new { e.Id, e.ChildName, e.ChildBirthDate }).ToListAsync();
        var marks = await db.Attendance.AsNoTracking().Where(a => a.SessionId == id).ToDictionaryAsync(a => a.EnrollmentId);
        var notes = await db.ObservationNotes.AsNoTracking().Where(n => n.SessionId == id)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Id, n.EnrollmentId, n.Enrollment.ChildName, n.Text, n.Tags, n.AuthorName, n.AuthorUserId, n.CreatedAt, n.UpdatedAt })
            .ToListAsync();
        var noteCounts = notes.GroupBy(n => n.EnrollmentId).ToDictionary(g => g.Key, g => g.Count());

        return new ClassroomSessionDto(
            s.Id, s.ProgramId, s.Program.Title, s.Program.Type, s.Program.Location, s.Date, s.StartTime, s.EndTime,
            s.IsCancelled, s.Note, s.StartedAt, s.StartedByName, s.EndedAt,
            CanStart: !s.IsCancelled && s.Date <= Today(),
            staff.Select(x => x with { PhotoUrl = media.ToPublic(x.PhotoUrl) }).ToList(),
            children.Select(c =>
            {
                var m = marks.GetValueOrDefault(c.Id);
                return new ClassroomChild(c.Id, c.ChildName, c.ChildBirthDate, m?.Status, m?.ArrivedAt, m?.MarkedByName ?? "",
                    noteCounts.GetValueOrDefault(c.Id));
            }).ToList(),
            notes.Select(n => new ClassroomNote(n.Id, n.EnrollmentId, n.ChildName, n.Text, n.Tags, n.AuthorName,
                n.CreatedAt, n.UpdatedAt, viewer.IsManager || n.AuthorUserId == viewer.UserId)).ToList(),
            NoteTags);
    }

    private static readonly TimeZoneInfo Turkey = FindTurkey();

    private static TimeZoneInfo FindTurkey()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("TRT", TimeSpan.FromHours(3), "TRT", "TRT"); }
    }

    /// <summary>Today in Turkey: lessons are planned in local dates, so "today" must not flip at 03:00.</summary>
    public static DateOnly Today() => DateOnly.FromDateTime(LocalNow());

    /// <summary>Wall-clock time in Turkey.</summary>
    public static DateTime LocalNow() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Turkey);
}
