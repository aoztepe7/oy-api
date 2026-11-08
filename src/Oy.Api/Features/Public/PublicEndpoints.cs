using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Content;
using Oy.Api.Features.Programs;
using Oy.Api.Infrastructure;

namespace Oy.Api.Features.Public;

public record PublicProgram(
    Guid Id, string Slug, string Title, ProgramType Type, int AgeMin, int AgeMax, DateOnly StartDate, DateOnly EndDate,
    int[] DaysOfWeek, TimeOnly StartTime, TimeOnly EndTime, int Capacity, int EnrolledCount, decimal Price,
    PricePeriod PricePeriod, string Summary, string CoverImageUrl, string Location, string? Description, int? FeaturedOrder);

public record PublicStaff(Guid Id, string FullName, string Title, string Bio, string PhotoUrl);

public record PreRegistrationInput(
    Guid ProgramId,
    [Required, MaxLength(200)] string ChildName,
    DateOnly? ChildBirthDate,
    [Required, MaxLength(200)] string ParentName,
    [Required, MaxLength(50)] string ParentPhone,
    [Required, EmailAddress, MaxLength(256)] string ParentEmail,
    [MaxLength(2000)] string? Note);

public record ContactInput(
    [Required, MaxLength(200)] string Name,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [MaxLength(50)] string? Phone,
    [Required, MaxLength(5000)] string Message);

public static class PublicEndpoints
{
    public static void MapPublic(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public").WithTags("Public").AllowAnonymous();

        group.MapPublicPages();

        group.MapGet("/programs", async (ProgramType? type, int? age, AppDbContext db, MediaService media) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var q = db.Programs.AsNoTracking().Where(p => p.IsPublished && p.EndDate >= today);
            if (type is not null) q = q.Where(p => p.Type == type);
            if (age is not null) q = q.Where(p => p.AgeMin <= age && p.AgeMax >= age);
            var list = await Project(q.OrderBy(p => p.StartDate), withDescription: false).ToListAsync();
            return list.Select(p => p with { CoverImageUrl = media.ToPublic(p.CoverImageUrl) });
        });

        group.MapGet("/programs/{slug}", async (string slug, AppDbContext db, MediaService media) =>
            await Project(db.Programs.AsNoTracking().Where(p => p.IsPublished && p.Slug == slug), withDescription: true)
                .FirstOrDefaultAsync() is { } p
                ? Results.Ok(p with { CoverImageUrl = media.ToPublic(p.CoverImageUrl) })
                : Results.NotFound());

        group.MapGet("/themes", async (AppDbContext db, MediaService media) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var themes = await db.Themes.AsNoTracking().Where(t => t.EndDate >= today).OrderBy(t => t.StartDate).Take(50).ToListAsync();
            return themes.Select(t => ContentEndpoints.ToDto(t, media));
        });

        group.MapGet("/team", async (AppDbContext db, MediaService media) =>
        {
            var staff = await db.Staff.AsNoTracking().Where(s => s.IsActive && s.ShowOnWebsite).OrderBy(s => s.FullName).ToListAsync();
            return staff.Select(s => new PublicStaff(s.Id, s.FullName, s.Title, s.Bio, media.ToPublic(s.PhotoUrl)));
        });

        group.MapPost("/enrollments", async (PreRegistrationInput input, AppDbContext db) =>
        {
            var program = await db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == input.ProgramId && p.IsPublished);
            if (program is null) return new Errors().Check(false, "programId", "Eğitim bulunamadı.").ToResult();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var enrollment = new Enrollment
            {
                ProgramId = program.Id, ChildName = input.ChildName.Trim(), ChildBirthDate = input.ChildBirthDate,
                ParentName = input.ParentName.Trim(), ParentPhone = input.ParentPhone.Trim(), ParentEmail = input.ParentEmail.Trim(),
                Note = input.Note?.Trim() ?? "", Status = EnrollmentStatus.Pending,
                Fee = program.Price, FeePeriod = program.PricePeriod,
                StartDate = program.StartDate > today ? program.StartDate : today,
            };
            db.Enrollments.Add(enrollment);
            await db.SaveChangesAsync();
            return Results.Accepted(value: new { id = enrollment.Id });
        }).RequireRateLimiting("public-forms");

        group.MapPost("/contact", async (ContactInput input, AppDbContext db) =>
        {
            db.ContactMessages.Add(new ContactMessage
            {
                Name = input.Name.Trim(), Email = input.Email.Trim(), Phone = input.Phone?.Trim() ?? "", Message = input.Message.Trim(),
            });
            await db.SaveChangesAsync();
            return Results.Accepted();
        }).RequireRateLimiting("public-forms");
    }

    private static IQueryable<PublicProgram> Project(IQueryable<TrainingProgram> q, bool withDescription) =>
        q.Select(p => new PublicProgram(
            p.Id, p.Slug, p.Title, p.Type, p.AgeMin, p.AgeMax, p.StartDate, p.EndDate, p.DaysOfWeek, p.StartTime, p.EndTime,
            p.Capacity, p.Enrollments.Count(e => e.Status == EnrollmentStatus.Active), p.Price, p.PricePeriod,
            p.Summary, p.CoverImageUrl, p.Location, withDescription ? p.Description : null, p.FeaturedOrder));
}

public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", async (AppDbContext db) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var monthStart = FinanceCalculator.MonthStart(today);
            var monthEnd = FinanceCalculator.MonthEnd(today);

            var programs = await db.Programs.AsNoTracking()
                .Where(p => p.StartDate <= monthEnd && p.EndDate >= monthStart)
                .Include(p => p.Enrollments).Include(p => p.Sessions)
                .Include(p => p.Expenses.Where(e => e.Date >= monthStart && e.Date <= monthEnd))
                .Include(p => p.Assignments).ThenInclude(a => a.Staff)
                .AsSplitQuery().ToListAsync();

            decimal revenue = 0, cost = 0;
            foreach (var p in programs)
            {
                var held = p.Sessions.Where(s => !s.IsCancelled).Select(s => s.Date).ToList();
                revenue += p.Enrollments.Sum(e => FinanceCalculator.EnrollmentRevenue(e, p.EndDate, monthStart));
                cost += p.Assignments.Sum(a => FinanceCalculator.AssignmentCost(a, a.Staff, held, monthStart).Total);
                cost += p.Expenses.Sum(e => e.Amount);
            }

            return new
            {
                activePrograms = await db.Programs.CountAsync(p => p.StartDate <= today && p.EndDate >= today),
                activeEnrollments = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Active),
                pendingEnrollments = await db.Enrollments.CountAsync(e => e.Status == EnrollmentStatus.Pending),
                unreadMessages = await db.ContactMessages.CountAsync(m => !m.IsRead),
                monthRevenue = revenue,
                monthCost = cost,
                upcomingSessions = await ProgramEndpoints.CalendarAsync(db, today, today.AddDays(7)),
            };
        }).WithTags("Dashboard").RequireAuthorization();
    }
}
