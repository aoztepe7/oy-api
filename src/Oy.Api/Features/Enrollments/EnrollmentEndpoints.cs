using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Enrollments;

public record EnrollmentInput(
    Guid ProgramId,
    [Required, MaxLength(200)] string ChildName,
    DateOnly? ChildBirthDate,
    [Required, MaxLength(200)] string ParentName,
    [MaxLength(50)] string? ParentPhone,
    [MaxLength(256)] string? ParentEmail,
    /// <summary>Applied only with enrollments.fees.</summary>
    [Range(0, 10_000_000)] decimal? Fee,
    PricePeriod FeePeriod,
    DateOnly StartDate,
    DateOnly? EndDate,
    EnrollmentStatus Status,
    [MaxLength(2000)] string? Note);

/// <summary>
/// Parent phone / e-mail are null without enrollments.contact; <c>Fee</c> / <c>PaidTotal</c> null without enrollments.fees.
/// </summary>
public record EnrollmentDto(
    Guid Id, Guid ProgramId, string ProgramTitle, string ChildName, DateOnly? ChildBirthDate, string ParentName,
    string? ParentPhone, string? ParentEmail, decimal? Fee, PricePeriod FeePeriod, DateOnly StartDate, DateOnly? EndDate,
    EnrollmentStatus Status, string Note, decimal? PaidTotal, DateTime CreatedAt, List<EnrollmentAnswer> Answers,
    /// <summary>Registration form link of the enrollment; null when none was created.</summary>
    RegistrationDto? Registration,
    /// <summary>Invoice recipient; null when not given (or without enrollments.contact).</summary>
    BillingInfo? Billing);

/// <summary>Registration form link (okulumyeryuzu.com/kayit/{Token}) and the answers once submitted.</summary>
public record RegistrationDto(
    string Token, RegistrationStatus Status, DateTime IssuedAt, DateTime ExpiresAt, DateTime? OpenedAt, DateTime? SubmittedAt,
    List<EnrollmentAnswer> Answers);

public static class EnrollmentEndpoints
{
    public static void MapEnrollments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/enrollments").WithTags("Enrollments");

        group.MapGet("/", async (Guid? programId, EnrollmentStatus? status, AppDbContext db, ClaimsPrincipal me) =>
        {
            var query = db.Enrollments.AsNoTracking();
            if (programId is not null) query = query.Where(e => e.ProgramId == programId);
            if (status is not null) query = query.Where(e => e.Status == status);
            return (await Project(query.OrderByDescending(e => e.CreatedAt)).ToListAsync()).Select(e => Shape(e, me));
        }).RequireAuthorization(Permissions.EnrollmentsRead);

        // Sidebar badge: website pre-registrations waiting for approval.
        group.MapGet("/pending-count", async (AppDbContext db) =>
            new { count = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Pending) })
            .RequireAuthorization(Permissions.EnrollmentsRead);

        group.MapPost("/", async (EnrollmentInput input, AppDbContext db, UiRevalidator ui, ClaimsPrincipal me) =>
        {
            var e = new Enrollment { ChildName = "", ParentName = "" };
            return await SaveAsync(e, input, db, ui, me, isNew: true);
        }).RequireAuthorization(Permissions.EnrollmentsWrite);

        group.MapPut("/{id:guid}", async (Guid id, EnrollmentInput input, AppDbContext db, UiRevalidator ui, ClaimsPrincipal me) =>
            await db.Enrollments.Include(x => x.Registration).FirstOrDefaultAsync(x => x.Id == id) is { } e ? await SaveAsync(e, input, db, ui, me, isNew: false) : Results.NotFound())
            .RequireAuthorization(Permissions.EnrollmentsWrite);

        // Registration form link: create, or renew (new link, open for answers again).
        group.MapPost("/{id:guid}/registration", async (Guid id, AppDbContext db, ClaimsPrincipal me) =>
        {
            var e = await db.Enrollments.Include(x => x.Registration).Include(x => x.Program).FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();
            if (!RegistrationLinks.HasForm(e.Program))
                return ApiResults.Conflict("Bu eğitimin esas kayıt formu yok. Önce eğitim ayarlarından form sorularını ekleyin.");
            if (e.Registration is null) db.Registrations.Add(RegistrationLinks.Issue(null, e.Id, e.Program, DateTime.UtcNow));
            else RegistrationLinks.Issue(e.Registration, e.Id, e.Program, DateTime.UtcNow);
            await db.SaveChangesAsync();
            return Results.Ok(Shape(await Project(db.Enrollments.AsNoTracking().Where(x => x.Id == id)).FirstAsync(), me));
        }).RequireAuthorization(Permissions.EnrollmentsWrite);

        group.MapDelete("/{id:guid}/registration", async (Guid id, AppDbContext db) =>
            await db.Registrations.Where(r => r.EnrollmentId == id).ExecuteDeleteAsync() == 0 ? Results.NotFound() : Results.NoContent())
            .RequireAuthorization(Permissions.EnrollmentsWrite);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            if (await db.Payments.AnyAsync(p => p.EnrollmentId == id))
                return ApiResults.Conflict("Ödemesi olan kayıt silinemez. İptal durumuna alabilirsiniz.");
            if (await db.Attendance.AnyAsync(a => a.EnrollmentId == id) || await db.ObservationNotes.AnyAsync(n => n.EnrollmentId == id))
                return ApiResults.Conflict("Yoklaması ya da gözlem notu olan kayıt silinemez. İptal durumuna alabilirsiniz.");
            if (await db.Enrollments.Where(e => e.Id == id).ExecuteDeleteAsync() == 0) return Results.NotFound();
            await ui.RevalidateAsync(CacheTags.Programs);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.EnrollmentsWrite);
    }

    private static EnrollmentDto Shape(EnrollmentDto e, ClaimsPrincipal me)
    {
        var contact = me.Can(Permissions.EnrollmentsContact);
        var fees = me.Can(Permissions.EnrollmentsFees);
        var now = DateTime.UtcNow;
        return e with
        {
            Registration = e.Registration is { } r
                ? r with { Status = RegistrationLinks.Status(r.SubmittedAt, r.OpenedAt, r.ExpiresAt, now) }
                : null,
            ParentPhone = contact ? e.ParentPhone : null,
            Billing = contact ? e.Billing : null,
            ParentEmail = contact ? e.ParentEmail : null,
            Fee = fees ? e.Fee : null,
            PaidTotal = fees ? e.PaidTotal : null,
        };
    }

    private static async Task<IResult> SaveAsync(
        Enrollment e, EnrollmentInput input, AppDbContext db, UiRevalidator ui, ClaimsPrincipal me, bool isNew)
    {
        // Hidden fields are not overwritten by someone who cannot see them: an existing enrollment keeps its
        // fee / contact details; a new one gets the program price (contact typed in by the creator is kept).
        var fees = me.Can(Permissions.EnrollmentsFees);
        var contact = me.Can(Permissions.EnrollmentsContact) || isNew;
        var programChanged = isNew || e.ProgramId != input.ProgramId;
        var program = await db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.ProgramId);
        var errors = new Errors()
            .Check(program is not null, "programId", "Eğitim bulunamadı.")
            .Check(input.EndDate is null || input.EndDate >= input.StartDate, "endDate", "Bitiş tarihi başlangıçtan önce olamaz.");
        if (errors.Any) return errors.ToResult();

        if (input.Status == EnrollmentStatus.Active)
        {
            var active = await db.Enrollments.CountAsync(x => x.ProgramId == input.ProgramId && x.Status == EnrollmentStatus.Active && x.Id != e.Id);
            if (active >= program!.Capacity) return ApiResults.Conflict($"Kontenjan dolu ({program.Capacity}).");
        }

        e.ProgramId = input.ProgramId;
        e.ChildName = input.ChildName.Trim();
        e.ChildBirthDate = input.ChildBirthDate;
        e.ParentName = input.ParentName.Trim();
        if (contact)
        {
            e.ParentPhone = input.ParentPhone?.Trim() ?? "";
            e.ParentEmail = input.ParentEmail?.Trim() ?? "";
        }
        if (fees)
        {
            e.Fee = input.Fee ?? 0;
            e.FeePeriod = input.FeePeriod;
        }
        else if (programChanged)
        {
            e.Fee = program!.Price;
            e.FeePeriod = program.PricePeriod;
        }
        e.StartDate = input.StartDate;
        e.EndDate = input.EndDate;
        e.Status = input.Status;
        e.Note = input.Note?.Trim() ?? "";
        if (isNew) db.Enrollments.Add(e);
        // Approving (or adding an active enrollment) creates the registration form link to send to the parent.
        if (e.Status == EnrollmentStatus.Active && e.Registration is null && RegistrationLinks.HasForm(program!))
            e.Registration = RegistrationLinks.Issue(null, e.Id, program!, DateTime.UtcNow);
        await db.SaveChangesAsync();
        await ui.RevalidateAsync(CacheTags.Programs); // remaining seats changed

        var dto = Shape(await Project(db.Enrollments.AsNoTracking().Where(x => x.Id == e.Id)).FirstAsync(), me);
        return isNew ? Results.Created($"/api/admin/enrollments/{e.Id}", dto) : Results.Ok(dto);
    }

    private static IQueryable<EnrollmentDto> Project(IQueryable<Enrollment> q) => q.Select(e => new EnrollmentDto(
        e.Id, e.ProgramId, e.Program.Title, e.ChildName, e.ChildBirthDate, e.ParentName, e.ParentPhone, e.ParentEmail,
        e.Fee, e.FeePeriod, e.StartDate, e.EndDate, e.Status, e.Note, e.Payments.Sum(p => p.Amount), e.CreatedAt, e.Answers,
        e.Registration == null ? null : new RegistrationDto(
            e.Registration.Token, RegistrationStatus.Sent, e.Registration.IssuedAt, e.Registration.ExpiresAt,
            e.Registration.OpenedAt, e.Registration.SubmittedAt, e.Registration.Answers),
        e.Billing));
}
