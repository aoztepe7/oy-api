using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;

namespace Oy.Api.Features.Enrollments;

public record EnrollmentInput(
    Guid ProgramId,
    [Required, MaxLength(200)] string ChildName,
    DateOnly? ChildBirthDate,
    [Required, MaxLength(200)] string ParentName,
    [MaxLength(50)] string? ParentPhone,
    [MaxLength(256)] string? ParentEmail,
    [Range(0, 10_000_000)] decimal Fee,
    PricePeriod FeePeriod,
    DateOnly StartDate,
    DateOnly? EndDate,
    EnrollmentStatus Status,
    [MaxLength(2000)] string? Note);

public record EnrollmentDto(
    Guid Id, Guid ProgramId, string ProgramTitle, string ChildName, DateOnly? ChildBirthDate, string ParentName,
    string ParentPhone, string ParentEmail, decimal Fee, PricePeriod FeePeriod, DateOnly StartDate, DateOnly? EndDate,
    EnrollmentStatus Status, string Note, decimal PaidTotal, DateTime CreatedAt);

public static class EnrollmentEndpoints
{
    public static void MapEnrollments(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/enrollments").WithTags("Enrollments");

        group.MapGet("/", async (Guid? programId, EnrollmentStatus? status, AppDbContext db) =>
        {
            var query = db.Enrollments.AsNoTracking();
            if (programId is not null) query = query.Where(e => e.ProgramId == programId);
            if (status is not null) query = query.Where(e => e.Status == status);
            return await Project(query.OrderByDescending(e => e.CreatedAt)).ToListAsync();
        }).RequireAuthorization(Permissions.EnrollmentsRead);

        group.MapPost("/", async (EnrollmentInput input, AppDbContext db, UiRevalidator ui) =>
        {
            var e = new Enrollment { ChildName = "", ParentName = "" };
            return await SaveAsync(e, input, db, ui, isNew: true);
        }).RequireAuthorization(Permissions.EnrollmentsWrite);

        group.MapPut("/{id:guid}", async (Guid id, EnrollmentInput input, AppDbContext db, UiRevalidator ui) =>
            await db.Enrollments.FindAsync(id) is { } e ? await SaveAsync(e, input, db, ui, isNew: false) : Results.NotFound())
            .RequireAuthorization(Permissions.EnrollmentsWrite);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            if (await db.Payments.AnyAsync(p => p.EnrollmentId == id))
                return ApiResults.Conflict("Ödemesi olan kayıt silinemez. İptal durumuna alabilirsiniz.");
            if (await db.Enrollments.Where(e => e.Id == id).ExecuteDeleteAsync() == 0) return Results.NotFound();
            await ui.RevalidateAsync(CacheTags.Programs);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.EnrollmentsWrite);
    }

    private static async Task<IResult> SaveAsync(Enrollment e, EnrollmentInput input, AppDbContext db, UiRevalidator ui, bool isNew)
    {
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
        e.ParentPhone = input.ParentPhone?.Trim() ?? "";
        e.ParentEmail = input.ParentEmail?.Trim() ?? "";
        e.Fee = input.Fee;
        e.FeePeriod = input.FeePeriod;
        e.StartDate = input.StartDate;
        e.EndDate = input.EndDate;
        e.Status = input.Status;
        e.Note = input.Note?.Trim() ?? "";
        if (isNew) db.Enrollments.Add(e);
        await db.SaveChangesAsync();
        await ui.RevalidateAsync(CacheTags.Programs); // remaining seats changed

        var dto = await Project(db.Enrollments.AsNoTracking().Where(x => x.Id == e.Id)).FirstAsync();
        return isNew ? Results.Created($"/api/admin/enrollments/{e.Id}", dto) : Results.Ok(dto);
    }

    private static IQueryable<EnrollmentDto> Project(IQueryable<Enrollment> q) => q.Select(e => new EnrollmentDto(
        e.Id, e.ProgramId, e.Program.Title, e.ChildName, e.ChildBirthDate, e.ParentName, e.ParentPhone, e.ParentEmail,
        e.Fee, e.FeePeriod, e.StartDate, e.EndDate, e.Status, e.Note, e.Payments.Sum(p => p.Amount), e.CreatedAt));
}
