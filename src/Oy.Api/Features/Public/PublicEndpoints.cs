using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Content;
using Oy.Api.Features.Programs;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Public;

public record PublicProgram(
    Guid Id, string Slug, string Title, ProgramType Type, int AgeMin, int AgeMax, DateOnly StartDate, DateOnly EndDate,
    int[] DaysOfWeek, TimeOnly StartTime, TimeOnly EndTime, int Capacity, int EnrolledCount, decimal Price,
    PricePeriod PricePeriod, List<PriceOption> PriceOptions, string Summary, string CoverImageUrl, string Location, string? Description, int? FeaturedOrder,
    bool EnrollmentEnabled,
    /// <summary>Pre-registration form definition (detail only).</summary>
    EnrollmentFormConfig? EnrollmentForm);

public record PublicStaff(Guid Id, string FullName, string Title, string Bio, string PhotoUrl);

/// <summary>
/// Pre-registration of one family. Several children → one enrollment each (capacity counts children);
/// <c>ChildName</c>/<c>ChildBirthDate</c> are the single-child shorthand.
/// </summary>
public record PreRegistrationInput(
    Guid ProgramId,
    [MaxLength(200)] string? ChildName,
    DateOnly? ChildBirthDate,
    [Required, MaxLength(200)] string ParentName,
    [MaxLength(50)] string? ParentPhone,
    [MaxLength(256)] string? ParentEmail,
    [MaxLength(2000)] string? Note,
    List<ChildInput>? Children = null,
    /// <summary>Label of the chosen price table package, e.g. "2 Yetişkin + 2 Çocuk".</summary>
    [MaxLength(100)] string? PriceOption = null,
    /// <summary>Answers to family-level custom questions: question id → value.</summary>
    Dictionary<string, string>? Answers = null);

/// <summary>A child with answers to the per-child custom questions (question id → value).</summary>
public record ChildInput([MaxLength(200)] string? Name, DateOnly? BirthDate, Dictionary<string, string>? Answers = null);

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
            // Lists don't need the form definition.
            return list.Select(p => p with { CoverImageUrl = media.ToPublic(p.CoverImageUrl), EnrollmentForm = null });
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
            if (!program.EnrollmentEnabled)
                return new Errors().Check(false, "programId", "Bu eğitim için ön kayıt alınmıyor; lütfen bizimle iletişime geçin.").ToResult();

            var form = program.EnrollmentForm;
            var submitted = (input.Children is { Count: > 0 } ? input.Children : [new ChildInput(input.ChildName, input.ChildBirthDate)])
                .Select(c => new SubmittedChild(
                    c.Name?.Trim() ?? "",
                    form.ChildBirthDate == FieldMode.Hidden ? null : c.BirthDate,
                    c.Answers ?? []))
                .ToList();
            var (answers, formErrors) = EnrollmentForms.Validate(form, submitted, input.ParentPhone, input.ParentEmail, input.Note, input.Answers);
            var children = submitted.Select(c => (Name: c.Name, c.BirthDate)).ToList();
            var packages = program.PriceOptions.Where(o => !o.IsAddon).ToList();
            var package = packages.FirstOrDefault(o => o.Label == input.PriceOption?.Trim());
            var errors = new Errors().Check(packages.Count == 0 || package is not null, "priceOption", "Katılım seçeneğini seçin.");
            foreach (var (key, message) in formErrors) errors.Check(false, key, message);
            if (errors.Any) return errors.ToResult();

            // A package is priced per family: its price goes on the first child, siblings get 0 so revenue is not counted twice.
            var family = children.Count > 1 || package is not null
                ? string.Join(" · ", new[]
                {
                    children.Count > 1 ? $"Aile kaydı ({children.Count} çocuk): {string.Join(", ", children.Select(c => c.Name))}" : null,
                    package is not null ? $"Katılım seçeneği: {package.Label} ({package.Price.ToString("N0", TurkishCulture)} ₺)" : null,
                }.Where(s => s is not null))
                : "";
            var ownNote = form.Note == FieldMode.Hidden ? "" : input.Note?.Trim() ?? "";
            var note = string.Join("\n", new[] { family, ownNote }.Where(s => s.Length > 0));

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var enrollments = children.Select((c, i) => new Enrollment
            {
                ProgramId = program.Id, ChildName = c.Name, ChildBirthDate = c.BirthDate,
                ParentName = input.ParentName.Trim(),
                ParentPhone = form.ParentPhone == FieldMode.Hidden ? "" : input.ParentPhone?.Trim() ?? "",
                ParentEmail = form.ParentEmail == FieldMode.Hidden ? "" : input.ParentEmail?.Trim() ?? "",
                Answers = answers[i],
                Note = Truncate(package is not null && i > 0 ? $"Ücret aile paketinde, {children[0].Name} kaydında.\n{note}" : note, 2000),
                Status = EnrollmentStatus.Pending,
                Fee = package is null ? program.Price : i == 0 ? package.Price : 0,
                FeePeriod = program.PricePeriod,
                StartDate = program.StartDate > today ? program.StartDate : today,
            }).ToList();
            db.Enrollments.AddRange(enrollments);
            await db.SaveChangesAsync();
            return Results.Accepted(value: new { id = enrollments[0].Id, ids = enrollments.Select(e => e.Id) });
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

    private static readonly System.Globalization.CultureInfo TurkishCulture = new("tr-TR");

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static IQueryable<PublicProgram> Project(IQueryable<TrainingProgram> q, bool withDescription) =>
        q.Select(p => new PublicProgram(
            p.Id, p.Slug, p.Title, p.Type, p.AgeMin, p.AgeMax, p.StartDate, p.EndDate, p.DaysOfWeek, p.StartTime, p.EndTime,
            p.Capacity, p.Enrollments.Count(e => e.Status == EnrollmentStatus.Active), p.Price, p.PricePeriod, p.PriceOptions,
            p.Summary, p.CoverImageUrl, p.Location, withDescription ? p.Description : null, p.FeaturedOrder,
            p.EnrollmentEnabled, p.EnrollmentForm));
}

public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/dashboard", async (AppDbContext db, ClaimsPrincipal me) =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            decimal? revenue = null, cost = null;
            // Money only for dashboard.finance (and not even computed otherwise).
            if (me.Can(Permissions.DashboardFinance))
            {
                var monthStart = FinanceCalculator.MonthStart(today);
                var monthEnd = FinanceCalculator.MonthEnd(today);
                var programs = await db.Programs.AsNoTracking()
                    .Where(p => p.StartDate <= monthEnd && p.EndDate >= monthStart)
                    .Include(p => p.Enrollments).Include(p => p.Sessions)
                    .Include(p => p.Expenses.Where(e => e.Date >= monthStart && e.Date <= monthEnd))
                    .Include(p => p.Assignments).ThenInclude(a => a.Staff)
                    .AsSplitQuery().ToListAsync();

                revenue = 0;
                cost = 0;
                foreach (var p in programs)
                {
                    var held = p.Sessions.Where(s => !s.IsCancelled).Select(s => s.Date).ToList();
                    revenue += p.Enrollments.Sum(e => FinanceCalculator.EnrollmentRevenue(e, p.EndDate, monthStart));
                    cost += p.Assignments.Sum(a => FinanceCalculator.AssignmentCost(a, a.Staff, held, monthStart).Total);
                    cost += p.Expenses.Sum(e => e.Amount);
                }
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
        }).WithTags("Dashboard").RequireAuthorization(Permissions.DashboardView);
    }
}
