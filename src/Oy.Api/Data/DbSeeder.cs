using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Domain;

namespace Oy.Api.Data;

public class SeedOptions
{
    public string AdminEmail { get; set; } = "admin@okulumyeryuzu.com";
    public string AdminPassword { get; set; } = "Admin123!";
    /// <summary>Adds dummy programs, staff and themes on an empty database.</summary>
    public bool SampleData { get; set; } = true;
}

/// <summary>Idempotent seed: roles, admin, CMS pages; optional demo data.</summary>
public static class DbSeeder
{
    private const string Lorem =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer posuere erat a ante venenatis dapibus posuere velit aliquet. " +
        "Cras mattis consectetur purus sit amet fermentum.";

    public static async Task SeedAsync(AppDbContext db, IPasswordHasher<User> hasher, SeedOptions options)
    {
        await SeedRolesAndAdminAsync(db, hasher, options);
        await SeedPagesAsync(db);
        if (!await db.SpecialDays.AnyAsync())
        {
            db.SpecialDays.AddRange(SpecialDayCatalog.Build(2025, 2030));
            await db.SaveChangesAsync();
        }
        if (options.SampleData && !await db.Programs.AnyAsync()) await SeedSampleAsync(db);
    }

    private static async Task SeedRolesAndAdminAsync(AppDbContext db, IPasswordHasher<User> hasher, SeedOptions o)
    {
        var roles = new Dictionary<string, List<string>>
        {
            ["Yönetici"] = [.. Permissions.All.Keys],
            ["Eğitim Koordinatörü"] =
            [
                Permissions.ProgramsRead, Permissions.ProgramsWrite, Permissions.StaffRead,
                Permissions.EnrollmentsRead, Permissions.EnrollmentsWrite,
            ],
            ["Muhasebe"] =
            [
                Permissions.ProgramsRead, Permissions.StaffRead, Permissions.EnrollmentsRead,
                Permissions.FinanceRead, Permissions.FinanceWrite,
            ],
            ["İçerik Editörü"] = [Permissions.ContentWrite, Permissions.ProgramsRead],
        };
        foreach (var (name, perms) in roles)
        {
            if (!await db.Roles.AnyAsync(r => r.Name == name)) db.Roles.Add(new Role { Name = name, Permissions = perms });
        }
        await db.SaveChangesAsync();

        // Admin role always gets newly added permissions.
        var admin = await db.Roles.FirstAsync(r => r.Name == "Yönetici");
        if (Permissions.All.Keys.Except(admin.Permissions).Any())
        {
            admin.Permissions = [.. Permissions.All.Keys];
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            var user = new User { Email = o.AdminEmail.ToLowerInvariant(), FullName = "Sistem Yöneticisi", RoleId = admin.Id };
            user.PasswordHash = hasher.HashPassword(user, o.AdminPassword);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Creates missing built-in pages (published) and the site settings row.</summary>
    private static async Task SeedPagesAsync(AppDbContext db)
    {
        var existing = await db.Pages.Select(p => p.Slug).ToListAsync();
        foreach (var (slug, title, blocks) in PageTemplates.SystemPages().Where(p => !existing.Contains(p.Slug)))
        {
            db.Pages.Add(new Page
            {
                Slug = slug, Title = title, SeoTitle = slug == Page.HomeSlug ? "Okulum Yeryüzü — Doğada büyüyen çocuklar" : title, SeoDescription = Lorem[..120], IsSystem = true,
                Draft = blocks, Published = [.. blocks], PublishedAt = DateTime.UtcNow, DraftUpdatedAt = DateTime.UtcNow,
            });
        }
        if (!await db.SiteSettings.AnyAsync()) db.SiteSettings.Add(PageTemplates.Settings());
        await db.SaveChangesAsync();
    }

    private static async Task SeedSampleAsync(AppDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var seasonStart = new DateOnly(today.Month >= 7 ? today.Year : today.Year - 1, 9, 7);

        var ayse = new Staff
        {
            FullName = "Ayşe Yılmaz", Title = "Orman Okulu Öğretmeni", Bio = Lorem, ShowOnWebsite = true,
            EmploymentType = EmploymentType.Salaried, MonthlyNetSalary = 32000, MonthlySgkPremium = 9500, MonthlyTaxAndOther = 6500,
        };
        var mehmet = new Staff
        {
            FullName = "Mehmet Demir", Title = "Doğa Rehberi", Bio = Lorem, ShowOnWebsite = true,
            EmploymentType = EmploymentType.PerLesson, PerLessonFee = 900,
        };
        var zeynep = new Staff
        {
            FullName = "Zeynep Kaya", Title = "Kamp Koordinatörü", Bio = Lorem, ShowOnWebsite = true,
            EmploymentType = EmploymentType.Mixed, MonthlyNetSalary = 18000, MonthlySgkPremium = 6000, MonthlyTaxAndOther = 3000, PerLessonFee = 500,
        };
        db.Staff.AddRange(ayse, mehmet, zeynep);

        TrainingProgram Weekly(string title, int ageMin, int ageMax, int[] days) => new()
        {
            Title = title, Slug = Features.Slug.From(title), Type = ProgramType.WeeklyClass, AgeMin = ageMin, AgeMax = ageMax,
            StartDate = seasonStart, EndDate = seasonStart.AddMonths(10).AddDays(-7), DaysOfWeek = days,
            StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(13, 0), Capacity = 12, Price = 6500,
            PricePeriod = PricePeriod.Monthly, Summary = Lorem[..110], Description = $"{Lorem}\n\n{Lorem}",
            Location = "Belgrad Ormanı, İstanbul", IsPublished = true,
        };

        var forest34 = Weekly("3-4 Yaş Orman Okulu", 3, 4, [1, 3]);
        var forest56 = Weekly("5-6 Yaş Orman Okulu", 5, 6, [2, 4]);
        var summerCampStart = new DateOnly(seasonStart.Year + 1, 7, 6);
        var camp = new TrainingProgram
        {
            Title = "Yaz Doğa Kampı", Slug = "yaz-doga-kampi", Type = ProgramType.Camp, AgeMin = 7, AgeMax = 11,
            StartDate = summerCampStart, EndDate = summerCampStart.AddDays(4), DaysOfWeek = [1, 2, 3, 4, 5],
            StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(17, 0), Capacity = 20, Price = 14500,
            PricePeriod = PricePeriod.OneTime, Summary = Lorem[..110], Description = Lorem, Location = "Kazdağları", IsPublished = true,
        };
        var meetupDate = today.AddDays(14 + (6 - (int)today.DayOfWeek + 7) % 7);
        var meetup = new TrainingProgram
        {
            Title = "Aile Doğa Buluşması", Slug = "aile-doga-bulusmasi", Type = ProgramType.Event, AgeMin = 3, AgeMax = 12,
            StartDate = meetupDate, EndDate = meetupDate, DaysOfWeek = [meetupDate.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)meetupDate.DayOfWeek],
            StartTime = new TimeOnly(10, 30), EndTime = new TimeOnly(14, 0), Capacity = 40, Price = 750,
            PricePeriod = PricePeriod.OneTime, Summary = Lorem[..110], Description = Lorem, Location = "Polonezköy", IsPublished = true,
        };
        TrainingProgram[] programs = [forest34, forest56, camp, meetup];
        foreach (var p in programs)
            p.Sessions = p.ScheduledDates().Select(d => new Session { ProgramId = p.Id, Date = d, StartTime = p.StartTime, EndTime = p.EndTime }).ToList();
        db.Programs.AddRange(programs);

        db.Assignments.AddRange(
            new StaffAssignment { ProgramId = forest34.Id, StaffId = ayse.Id, Role = AssignmentRole.Lead, StartDate = forest34.StartDate, EndDate = forest34.EndDate, SalaryAllocationPercent = 50 },
            new StaffAssignment { ProgramId = forest56.Id, StaffId = ayse.Id, Role = AssignmentRole.Lead, StartDate = forest56.StartDate, EndDate = forest56.EndDate, SalaryAllocationPercent = 50 },
            new StaffAssignment { ProgramId = forest34.Id, StaffId = mehmet.Id, Role = AssignmentRole.Assistant, StartDate = forest34.StartDate, EndDate = forest34.EndDate, SalaryAllocationPercent = 0 },
            new StaffAssignment { ProgramId = camp.Id, StaffId = zeynep.Id, Role = AssignmentRole.Lead, StartDate = camp.StartDate, EndDate = camp.EndDate, SalaryAllocationPercent = 25 });

        string[] children = ["Deniz", "Ada", "Can", "Ela", "Kuzey", "Defne", "Aras"];
        foreach (var (child, i) in children.Select((c, i) => (c, i)))
        {
            db.Enrollments.Add(new Enrollment
            {
                ProgramId = i < 5 ? forest34.Id : forest56.Id, ChildName = $"{child} Örnek", ParentName = "Veli Örnek",
                ParentPhone = "+90 555 111 22 33", ParentEmail = "veli@example.com", Fee = 6500, FeePeriod = PricePeriod.Monthly,
                StartDate = seasonStart, Status = EnrollmentStatus.Active,
            });
        }

        db.Expenses.Add(new Expense { ProgramId = forest34.Id, Date = seasonStart.AddDays(2), Category = "Malzeme", Description = "Sonbahar etkinlik malzemeleri", Amount = 1850 });

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        db.Themes.AddRange(
            new Theme { Kind = ThemeKind.Monthly, Title = "Ayın Teması: Toprak ve Tohumlar", Description = Lorem, StartDate = monthStart, EndDate = monthStart.AddMonths(1).AddDays(-1) },
            new Theme { Kind = ThemeKind.Weekly, Title = "Haftanın Teması: Yaprakların Dili", Description = Lorem, StartDate = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), EndDate = today.AddDays(6 - (((int)today.DayOfWeek + 6) % 7)) });

        await db.SaveChangesAsync();
    }
}
