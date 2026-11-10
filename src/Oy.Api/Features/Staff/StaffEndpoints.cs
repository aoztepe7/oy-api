using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;
using StaffEntity = Oy.Api.Domain.Staff;

namespace Oy.Api.Features.Staff;

public record StaffInput(
    [Required, MaxLength(200)] string FullName,
    [MaxLength(200)] string? Title,
    [MaxLength(256)] string? Email,
    [MaxLength(50)] string? Phone,
    string? Bio,
    string? PhotoUrl,
    bool ShowOnWebsite,
    bool IsActive,
    // Pay fields: applied only with staff.compensation, otherwise ignored (stored values kept).
    EmploymentType? EmploymentType = null,
    [Range(0, 10_000_000)] decimal? MonthlyNetSalary = null,
    [Range(0, 10_000_000)] decimal? MonthlySgkPremium = null,
    [Range(0, 10_000_000)] decimal? MonthlyTaxAndOther = null,
    [Range(0, 1_000_000)] decimal? PerLessonFee = null);

/// <summary>Pay fields are null without staff.compensation.</summary>
public record StaffDto(
    Guid Id, string FullName, string Title, string Email, string Phone, string Bio, string PhotoUrl,
    bool ShowOnWebsite, bool IsActive, EmploymentType? EmploymentType, decimal? MonthlyNetSalary,
    decimal? MonthlySgkPremium, decimal? MonthlyTaxAndOther, decimal? PerLessonFee,
    decimal? MonthlyFixedCost, int ActiveAssignmentCount);

public record AssignmentInput(
    Guid StaffId, AssignmentRole Role, DateOnly StartDate, DateOnly EndDate,
    [Range(0, 100)] decimal? SalaryAllocationPercent = null, [Range(0, 1_000_000)] decimal? PerLessonFeeOverride = null);

public record AssignmentDto(
    Guid Id, Guid ProgramId, Guid StaffId, string StaffName, AssignmentRole Role, DateOnly StartDate, DateOnly EndDate,
    decimal? SalaryAllocationPercent, decimal? PerLessonFeeOverride, decimal? EstimatedMonthlyCost, decimal? EstimatedTotalCost);

public record StaffCostProgramRow(Guid ProgramId, string ProgramTitle, decimal FixedCost, int LessonCount, decimal LessonCost, decimal Total);

public record StaffCostMonth(string Month, decimal FixedCost, int LessonCount, decimal LessonCost, decimal Total, IReadOnlyList<StaffCostProgramRow> ByProgram);

public record StaffCostReport(Guid StaffId, string FullName, IReadOnlyList<StaffCostMonth> Months, decimal Total);

public static class StaffEndpoints
{
    public static void MapStaff(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Staff");

        group.MapGet("/staff", async (AppDbContext db, MediaService media, ClaimsPrincipal me) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var list = await db.Staff.AsNoTracking().OrderBy(s => s.FullName)
                .Select(s => new { Staff = s, Active = s.Assignments.Count(a => a.EndDate >= today) }).ToListAsync();
            return list.Select(x => ToDto(x.Staff, x.Active, media, Pay(me)));
        }).RequireAuthorization(Permissions.StaffRead);

        group.MapGet("/staff/{id:guid}", async (Guid id, AppDbContext db, MediaService media, ClaimsPrincipal me) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var x = await db.Staff.AsNoTracking().Where(s => s.Id == id)
                .Select(s => new { Staff = s, Active = s.Assignments.Count(a => a.EndDate >= today) }).FirstOrDefaultAsync();
            return x is null ? Results.NotFound() : Results.Ok(ToDto(x.Staff, x.Active, media, Pay(me)));
        }).RequireAuthorization(Permissions.StaffRead);

        group.MapPost("/staff", async (StaffInput input, AppDbContext db, MediaService media, UiRevalidator ui, ClaimsPrincipal me) =>
        {
            var staff = new StaffEntity { FullName = "" };
            Apply(staff, input, media, Pay(me));
            db.Staff.Add(staff);
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Team);
            return Results.Created($"/api/admin/staff/{staff.Id}", ToDto(staff, 0, media, Pay(me)));
        }).RequireAuthorization(Permissions.StaffWrite);

        group.MapPut("/staff/{id:guid}", async (Guid id, StaffInput input, AppDbContext db, MediaService media, UiRevalidator ui, ClaimsPrincipal me) =>
        {
            var staff = await db.Staff.FindAsync(id);
            if (staff is null) return Results.NotFound();
            Apply(staff, input, media, Pay(me));
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Team);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return Results.Ok(ToDto(staff, await db.Assignments.CountAsync(a => a.StaffId == id && a.EndDate >= today), media, Pay(me)));
        }).RequireAuthorization(Permissions.StaffWrite);

        group.MapDelete("/staff/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            if (await db.Assignments.AnyAsync(a => a.StaffId == id))
                return ApiResults.Conflict("Eğitime atanmış personel silinemez. Pasif yapabilirsiniz.");
            if (await db.Staff.Where(s => s.Id == id).ExecuteDeleteAsync() == 0) return Results.NotFound();
            await ui.RevalidateAsync(CacheTags.Team);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.StaffWrite);

        group.MapGet("/staff/{id:guid}/cost", async (Guid id, string? from, string? to, AppDbContext db) =>
        {
            var staff = await db.Staff.AsNoTracking()
                .Include(s => s.Assignments).ThenInclude(a => a.Program).ThenInclude(p => p.Sessions)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id);
            if (staff is null) return Results.NotFound();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = ParseMonth(from) ?? (staff.Assignments.Count > 0 ? staff.Assignments.Min(a => a.StartDate) : today);
            var end = ParseMonth(to) is { } t ? FinanceCalculator.MonthEnd(t)
                : staff.Assignments.Count > 0 ? staff.Assignments.Max(a => a.EndDate) : today;
            if (end < start || FinanceCalculator.Months(start, end).Count() > 60)
                return new Errors().Check(false, "to", "Ay aralığı geçersiz (en fazla 60 ay).").ToResult();

            var months = FinanceCalculator.Months(start, end).Select(m =>
            {
                var rows = staff.Assignments.Select(a =>
                {
                    var held = a.Program.Sessions.Where(s => !s.IsCancelled).Select(s => s.Date);
                    var c = FinanceCalculator.AssignmentCost(a, staff, held, m);
                    return new StaffCostProgramRow(a.ProgramId, a.Program.Title, c.FixedCost, c.LessonCount, c.LessonCost, c.Total);
                }).Where(r => r.Total > 0 || r.LessonCount > 0).ToList();
                return new StaffCostMonth(FinanceCalculator.MonthKey(m), rows.Sum(r => r.FixedCost), rows.Sum(r => r.LessonCount),
                    rows.Sum(r => r.LessonCost), rows.Sum(r => r.Total), rows);
            }).ToList();

            return Results.Ok(new StaffCostReport(staff.Id, staff.FullName, months, months.Sum(m => m.Total)));
        }).RequireAuthorization(Permissions.StaffCompensation);

        // Assignments
        group.MapGet("/programs/{programId:guid}/assignments", async (Guid programId, AppDbContext db, ClaimsPrincipal me) =>
        {
            var program = await LoadProgramForCost(db, programId);
            return program is null ? Results.NotFound() : Results.Ok(program.Assignments.OrderBy(a => a.StartDate).Select(a => ToDto(a, program, Pay(me))));
        }).RequireAuthorization(Permissions.ProgramsRead);

        group.MapPost("/programs/{programId:guid}/assignments", async (Guid programId, AssignmentInput input, AppDbContext db, ClaimsPrincipal me) =>
        {
            var program = await db.Programs.FindAsync(programId);
            if (program is null) return Results.NotFound();
            var staff = await db.Staff.FindAsync(input.StaffId);
            var errors = new Errors()
                .Check(staff is not null, "staffId", "Personel bulunamadı.")
                .Check(input.EndDate >= input.StartDate, "endDate", "Bitiş tarihi başlangıçtan önce olamaz.");
            if (errors.Any) return errors.ToResult();

            // Cost split and fee override are compensation data: without that permission the defaults apply
            // (full salary share, the staff's own lesson fee).
            var assignment = new StaffAssignment
            {
                ProgramId = programId, StaffId = input.StaffId, Role = input.Role,
                StartDate = input.StartDate, EndDate = input.EndDate,
                SalaryAllocationPercent = Pay(me) ? input.SalaryAllocationPercent ?? 100 : 100,
                PerLessonFeeOverride = Pay(me) ? input.PerLessonFeeOverride : null,
            };
            db.Assignments.Add(assignment);
            await db.SaveChangesAsync();

            var loaded = await LoadProgramForCost(db, programId);
            return Results.Created($"/api/admin/assignments/{assignment.Id}", ToDto(loaded!.Assignments.First(a => a.Id == assignment.Id), loaded, Pay(me)));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        group.MapDelete("/assignments/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.Assignments.Where(a => a.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization(Permissions.ProgramsWrite);
    }

    private static Task<TrainingProgram?> LoadProgramForCost(AppDbContext db, Guid programId) =>
        db.Programs.AsNoTracking()
            .Include(p => p.Sessions)
            .Include(p => p.Assignments).ThenInclude(a => a.Staff)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == programId);

    /// <summary>Salaries, fees and costs are shown / changed only with staff.compensation.</summary>
    private static bool Pay(ClaimsPrincipal me) => me.Can(Permissions.StaffCompensation);

    private static AssignmentDto ToDto(StaffAssignment a, TrainingProgram p, bool pay)
    {
        if (!pay)
            return new AssignmentDto(a.Id, a.ProgramId, a.StaffId, a.Staff.FullName, a.Role, a.StartDate, a.EndDate, null, null, null, null);
        var held = p.Sessions.Where(s => !s.IsCancelled).Select(s => s.Date).ToList();
        var months = FinanceCalculator.Months(a.StartDate, a.EndDate).ToList();
        var total = months.Sum(m => FinanceCalculator.AssignmentCost(a, a.Staff, held, m).Total);
        return new AssignmentDto(a.Id, a.ProgramId, a.StaffId, a.Staff.FullName, a.Role, a.StartDate, a.EndDate,
            a.SalaryAllocationPercent, a.PerLessonFeeOverride,
            months.Count == 0 ? 0 : Math.Round(total / months.Count, 2), total);
    }

    private static void Apply(StaffEntity s, StaffInput i, MediaService media, bool pay)
    {
        s.FullName = i.FullName.Trim();
        s.Title = i.Title?.Trim() ?? "";
        s.Email = i.Email?.Trim() ?? "";
        s.Phone = i.Phone?.Trim() ?? "";
        s.Bio = i.Bio?.Trim() ?? "";
        s.PhotoUrl = media.ToStored(i.PhotoUrl);
        s.ShowOnWebsite = i.ShowOnWebsite;
        s.IsActive = i.IsActive;
        // Without the compensation permission the stored pay is kept as it is.
        if (!pay) return;
        s.EmploymentType = i.EmploymentType ?? s.EmploymentType;
        s.MonthlyNetSalary = i.MonthlyNetSalary ?? 0;
        s.MonthlySgkPremium = i.MonthlySgkPremium ?? 0;
        s.MonthlyTaxAndOther = i.MonthlyTaxAndOther ?? 0;
        s.PerLessonFee = i.PerLessonFee ?? 0;
    }

    private static StaffDto ToDto(StaffEntity s, int activeAssignments, MediaService media, bool pay) => new(
        s.Id, s.FullName, s.Title, s.Email, s.Phone, s.Bio, media.ToPublic(s.PhotoUrl), s.ShowOnWebsite, s.IsActive,
        pay ? s.EmploymentType : null, pay ? s.MonthlyNetSalary : null, pay ? s.MonthlySgkPremium : null,
        pay ? s.MonthlyTaxAndOther : null, pay ? s.PerLessonFee : null, pay ? s.MonthlyFixedCost : null, activeAssignments);

    private static DateOnly? ParseMonth(string? value) =>
        DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
