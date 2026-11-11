using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Programs;

public record ProgramInput(
    [Required, MaxLength(200)] string Title,
    [MaxLength(200)] string? Slug,
    ProgramType Type,
    [Range(0, 18)] int AgeMin,
    [Range(0, 18)] int AgeMax,
    DateOnly StartDate,
    DateOnly EndDate,
    int[] DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    [Range(1, 1000)] int Capacity,
    [Range(0, 10_000_000)] decimal Price,
    PricePeriod PricePeriod,
    [MaxLength(500)] string? Summary,
    string? Description,
    string? CoverImageUrl,
    [MaxLength(300)] string? Location,
    bool IsPublished,
    List<PriceOptionInput>? PriceOptions = null,
    /// <summary>null = leave unchanged (older clients).</summary>
    bool? EnrollmentEnabled = null,
    EnrollmentFormConfig? EnrollmentForm = null,
    /// <summary>null = leave unchanged (older clients).</summary>
    RegistrationFormConfig? RegistrationForm = null);

public record PriceOptionInput(
    [MaxLength(100)] string? Label,
    [Range(0, 10_000_000)] decimal Price,
    [MaxLength(150)] string? Note,
    bool IsAddon);

public record ProgramAdminDto(
    Guid Id, string Title, string Slug, ProgramType Type, int AgeMin, int AgeMax,
    DateOnly StartDate, DateOnly EndDate, int[] DaysOfWeek, TimeOnly StartTime, TimeOnly EndTime,
    /// <summary><c>Price</c> / <c>PriceOptions</c> are null without the programs.pricing permission.</summary>
    int Capacity, decimal? Price, PricePeriod PricePeriod, List<PriceOption>? PriceOptions, string Summary, string Description,
    string CoverImageUrl, string Location, bool IsPublished, int? FeaturedOrder,
    bool EnrollmentEnabled, EnrollmentFormConfig EnrollmentForm, RegistrationFormConfig RegistrationForm,
    int EnrolledCount, int SessionCount, DateTime CreatedAt, DateTime UpdatedAt,
    /// <summary>All enrollments of the program, any status (the "Kayıtlar" tab count).</summary>
    int EnrollmentTotal);

/// <summary>Home page slider order: first id is shown first; programs not listed are un-featured.</summary>
public record FeaturedInput(Guid[] ProgramIds);

public record SessionDto(
    Guid Id, DateOnly Date, TimeOnly StartTime, TimeOnly EndTime, bool IsCancelled, string Note,
    Guid? ThemeId, string? ThemeTitle, bool IsMakeup, DateOnly? MakeupForDate, DateOnly? MakeupDate,
    IReadOnlyList<string> SpecialDays, DateTime? StartedAt, DateTime? EndedAt);

public record SessionUpdate(bool IsCancelled, [MaxLength(500)] string? Note, Guid? ThemeId);

public record MakeupInput(DateOnly Date, TimeOnly? StartTime, TimeOnly? EndTime, [MaxLength(500)] string? Note);

public record CalendarItem(
    Guid SessionId, Guid ProgramId, string ProgramTitle, ProgramType ProgramType, DateOnly Date,
    TimeOnly StartTime, TimeOnly EndTime, bool IsCancelled, bool IsMakeup, string? ThemeTitle,
    string Location, IReadOnlyList<string> Staff);

public static class ProgramEndpoints
{
    public static void MapPrograms(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Programs");
        var read = Permissions.ProgramsRead;
        var write = Permissions.ProgramsWrite;

        group.MapGet("/programs", async (ProgramType? type, bool? published, AppDbContext db, MediaService media, ClaimsPrincipal me) =>
        {
            var query = db.Programs.AsNoTracking();
            if (type is not null) query = query.Where(p => p.Type == type);
            if (published is not null) query = query.Where(p => p.IsPublished == published);
            var list = await Project(query.OrderByDescending(p => p.StartDate)).ToListAsync();
            return list.Select(p => Shape(p, me, media));
        }).RequireAuthorization(read);

        group.MapGet("/programs/{id:guid}", async (Guid id, AppDbContext db, MediaService media, ClaimsPrincipal me) =>
            await Project(db.Programs.AsNoTracking().Where(p => p.Id == id)).FirstOrDefaultAsync() is { } p
                ? Results.Ok(Shape(p, me, media))
                : Results.NotFound()).RequireAuthorization(read);

        group.MapPost("/programs", async (ProgramInput input, AppDbContext db, MediaService media, UiRevalidator ui, ClaimsPrincipal me) =>
        {
            var program = new TrainingProgram { Title = "", Slug = "" };
            return await SaveAsync(program, input, db, media, ui, me, isNew: true);
        }).RequireAuthorization(write);

        group.MapPut("/programs/{id:guid}", async (Guid id, ProgramInput input, AppDbContext db, MediaService media, UiRevalidator ui, ClaimsPrincipal me) =>
        {
            var program = await db.Programs.Include(p => p.Sessions).FirstOrDefaultAsync(p => p.Id == id);
            return program is null ? Results.NotFound() : await SaveAsync(program, input, db, media, ui, me, isNew: false);
        }).RequireAuthorization(write);

        group.MapPut("/programs/featured", async (FeaturedInput input, AppDbContext db, UiRevalidator ui) =>
        {
            var ids = (input.ProgramIds ?? []).Distinct().ToList();
            if (ids.Count > 20) return new Errors().Check(false, "programIds", "En fazla 20 eğitim öne çıkarılabilir.").ToResult();
            var programs = await db.Programs.Where(p => p.FeaturedOrder != null || ids.Contains(p.Id)).ToListAsync();
            if (ids.Except(programs.Select(p => p.Id)).Any()) return Results.NotFound();
            foreach (var p in programs)
            {
                var index = ids.IndexOf(p.Id);
                p.FeaturedOrder = index < 0 ? null : index + 1;
            }
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Programs);
            return Results.NoContent();
        }).RequireAuthorization(write);

        group.MapDelete("/programs/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            if (await db.Payments.AnyAsync(p => p.Enrollment.ProgramId == id))
                return ApiResults.Conflict("Ödemesi olan bir eğitim silinemez. Yayından kaldırabilirsiniz.");
            if (await db.ObservationNotes.AnyAsync(n => n.ProgramId == id))
                return ApiResults.Conflict("Gözlem notu yazılmış bir eğitim silinemez. Yayından kaldırabilirsiniz.");
            var deleted = await db.Programs.Where(p => p.Id == id).ExecuteDeleteAsync();
            if (deleted == 0) return Results.NotFound();
            await ui.RevalidateAsync(CacheTags.Programs);
            return Results.NoContent();
        }).RequireAuthorization(write);

        // Sessions
        group.MapGet("/programs/{id:guid}/sessions", async (Guid id, AppDbContext db) =>
            await SessionsAsync(db, db.Sessions.Where(s => s.ProgramId == id))).RequireAuthorization(read);

        group.MapPut("/sessions/{id:guid}", async (Guid id, SessionUpdate input, AppDbContext db) =>
        {
            var s = await db.Sessions.FindAsync(id);
            if (s is null) return Results.NotFound();
            if (s.IsMakeup && input.IsCancelled)
                return ApiResults.Conflict("Telafi dersi iptal edilmez; gerekirse silin.");
            if (input.IsCancelled && !s.IsCancelled && s.StartedAt is not null)
                return ApiResults.Conflict("Başlatılmış (yoklaması alınmış) ders iptal edilemez.");
            if (!input.IsCancelled && s.IsCancelled && await db.Sessions.AnyAsync(m => m.MakeupForSessionId == id))
                return ApiResults.Conflict("Bu dersin telafisi planlanmış. İptali geri almak için önce telafi dersini silin.");
            if (input.ThemeId is { } themeId && !await db.Themes.AnyAsync(t => t.Id == themeId))
                return new Errors().Check(false, "themeId", "Tema bulunamadı.").ToResult();

            s.IsCancelled = input.IsCancelled;
            s.Note = input.Note?.Trim() ?? "";
            s.ThemeId = input.ThemeId;
            await db.SaveChangesAsync();
            return Results.Ok((await SessionsAsync(db, db.Sessions.Where(x => x.Id == id)))[0]);
        }).RequireAuthorization(write);

        // Cancels the session (if not already) and schedules a make-up lesson on another day.
        group.MapPost("/sessions/{id:guid}/makeup", async (Guid id, MakeupInput input, AppDbContext db) =>
        {
            var s = await db.Sessions.FindAsync(id);
            if (s is null) return Results.NotFound();
            if (s.IsMakeup) return ApiResults.Conflict("Telafi dersi için yeniden telafi planlanamaz.");
            if (await db.Sessions.AnyAsync(m => m.MakeupForSessionId == id))
                return ApiResults.Conflict("Bu dersin telafisi zaten planlanmış.");

            var startTime = input.StartTime ?? s.StartTime;
            var endTime = input.EndTime ?? s.EndTime;
            var errors = new Errors()
                .Check(input.Date != s.Date, "date", "Telafi günü iptal edilen günden farklı olmalı.")
                .Check(endTime > startTime, "endTime", "Bitiş saati başlangıçtan sonra olmalı.");
            if (errors.Any) return errors.ToResult();
            if (await db.Sessions.AnyAsync(x => x.ProgramId == s.ProgramId && x.Date == input.Date))
                return new Errors().Check(false, "date", "Bu tarihte eğitimin zaten bir dersi var.").ToResult();

            s.IsCancelled = true;
            var makeup = new Session
            {
                ProgramId = s.ProgramId, Date = input.Date, StartTime = startTime, EndTime = endTime,
                IsMakeup = true, MakeupForSessionId = s.Id, ThemeId = s.ThemeId, Note = input.Note?.Trim() ?? "",
            };
            db.Sessions.Add(makeup);
            await db.SaveChangesAsync();
            var dto = (await SessionsAsync(db, db.Sessions.Where(x => x.Id == makeup.Id)))[0];
            return Results.Created($"/api/admin/sessions/{makeup.Id}", dto);
        }).RequireAuthorization(write);

        // Only make-up lessons can be deleted; regular sessions come from the schedule.
        group.MapDelete("/sessions/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var s = await db.Sessions.FindAsync(id);
            if (s is null) return Results.NotFound();
            if (!s.IsMakeup) return ApiResults.Conflict("Planlı dersler silinemez; iptal edebilirsiniz.");
            if (s.StartedAt is not null) return ApiResults.Conflict("Başlatılmış (yoklaması alınmış) ders silinemez.");
            db.Sessions.Remove(s);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization(write);

        group.MapGet("/calendar", async (DateOnly from, DateOnly to, AppDbContext db) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 92)
                return new Errors().Check(false, "to", "Tarih aralığı en fazla 3 ay olabilir.").ToResult();
            return Results.Ok(await CalendarAsync(db, from, to));
        }).RequireAuthorization(read);
    }

    private static async Task<List<SessionDto>> SessionsAsync(AppDbContext db, IQueryable<Session> query)
    {
        var rows = await query.AsNoTracking().OrderBy(s => s.Date)
            .Select(s => new
            {
                s.Id, s.Date, s.StartTime, s.EndTime, s.IsCancelled, s.Note, s.ThemeId,
                ThemeTitle = s.Theme != null ? s.Theme.Title : null,
                s.IsMakeup,
                MakeupForDate = s.MakeupFor != null ? s.MakeupFor.Date : (DateOnly?)null,
                MakeupDate = db.Sessions.Where(m => m.MakeupForSessionId == s.Id).Select(m => (DateOnly?)m.Date).FirstOrDefault(),
                s.StartedAt, s.EndedAt,
            }).ToListAsync();
        if (rows.Count == 0) return [];

        var (from, to) = (rows[0].Date, rows[^1].Date);
        var special = (await db.SpecialDays.AsNoTracking().Where(d => d.Date >= from && d.Date <= to)
                .Select(d => new { d.Date, d.Name }).ToListAsync())
            .ToLookup(d => d.Date, d => d.Name);

        return rows.Select(s => new SessionDto(
            s.Id, s.Date, s.StartTime, s.EndTime, s.IsCancelled, s.Note, s.ThemeId, s.ThemeTitle,
            s.IsMakeup, s.MakeupForDate, s.MakeupDate, special[s.Date].ToList(), s.StartedAt, s.EndedAt)).ToList();
    }

    public static async Task<List<CalendarItem>> CalendarAsync(AppDbContext db, DateOnly from, DateOnly to)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(s => s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date).ThenBy(s => s.StartTime)
            .Select(s => new
            {
                s.Id, s.ProgramId, s.Program.Title, s.Program.Type, s.Date, s.StartTime, s.EndTime, s.IsCancelled, s.IsMakeup,
                ThemeTitle = s.Theme != null ? s.Theme.Title : null, s.Program.Location,
            }).ToListAsync();

        var programIds = sessions.Select(s => s.ProgramId).Distinct().ToList();
        var assignments = await db.Assignments.AsNoTracking()
            .Where(a => programIds.Contains(a.ProgramId) && a.StartDate <= to && a.EndDate >= from)
            .Select(a => new { a.ProgramId, a.StartDate, a.EndDate, a.Staff.FullName }).ToListAsync();

        return sessions.Select(s => new CalendarItem(
            s.Id, s.ProgramId, s.Title, s.Type, s.Date, s.StartTime, s.EndTime, s.IsCancelled, s.IsMakeup, s.ThemeTitle, s.Location,
            assignments.Where(a => a.ProgramId == s.ProgramId && a.StartDate <= s.Date && a.EndDate >= s.Date)
                .Select(a => a.FullName).ToList())).ToList();
    }

    /// <summary>Public cover URL; prices only for users who may see them.</summary>
    private static ProgramAdminDto Shape(ProgramAdminDto p, ClaimsPrincipal me, MediaService media) =>
        p with
        {
            CoverImageUrl = media.ToPublic(p.CoverImageUrl),
            Price = me.Can(Permissions.ProgramsPricing) ? p.Price : null,
            PriceOptions = me.Can(Permissions.ProgramsPricing) ? p.PriceOptions : null,
        };

    private static async Task<IResult> SaveAsync(
        TrainingProgram p, ProgramInput input, AppDbContext db, MediaService media, UiRevalidator ui, ClaimsPrincipal me, bool isNew)
    {
        // Without the pricing permission prices are neither shown nor changed: the stored ones are kept.
        var canPrice = me.Can(Permissions.ProgramsPricing);
        var days = (input.DaysOfWeek ?? []).Distinct().Order().ToArray();
        // Regular groups are planned and priced by month: whole calendar months, monthly fee.
        var monthly = input.Type == ProgramType.WeeklyClass;
        var startDate = monthly ? FinanceCalculator.MonthStart(input.StartDate) : input.StartDate;
        var endDate = monthly ? FinanceCalculator.MonthEnd(input.EndDate) : input.EndDate;
        var slug = Slug.From(string.IsNullOrWhiteSpace(input.Slug) ? input.Title : input.Slug);
        var errors = new Errors()
            .Check(endDate >= startDate, "endDate", "Bitiş tarihi başlangıçtan önce olamaz.")
            .Check(endDate.DayNumber - startDate.DayNumber <= 3 * 366, "endDate", "Eğitim süresi en fazla 3 yıl olabilir.")
            .Check(input.AgeMax >= input.AgeMin, "ageMax", "Üst yaş alt yaştan küçük olamaz.")
            .Check(input.EndTime > input.StartTime, "endTime", "Bitiş saati başlangıçtan sonra olmalı.")
            .Check(days.Length > 0 && days.All(d => d is >= 1 and <= 7), "daysOfWeek", "En az bir geçerli gün seçin.")
            .Check(slug.Length > 0, "slug", "Geçerli bir adres (slug) girin.");
        // Empty rows from the editor are ignored; a row with a price needs a label.
        var options = (canPrice ? input.PriceOptions ?? [] : [])
            .Where(o => !string.IsNullOrWhiteSpace(o.Label) || o.Price > 0)
            .Select(o => new PriceOption(o.Label?.Trim() ?? "", o.Price, o.Note?.Trim() ?? "", o.IsAddon))
            .ToList();
        errors
            .Check(options.Count <= 20, "priceOptions", "En fazla 20 fiyat seçeneği girilebilir.")
            .Check(options.All(o => o.Label.Length > 0), "priceOptions", "Her fiyat seçeneğinin bir adı olmalı (örn. 1 Yetişkin + 1 Çocuk).")
            .Check(options.Count == 0 || options.Any(o => !o.IsAddon), "priceOptions", "Ek ücretlerin yanında en az bir paket fiyatı girin.");
        var (form, formErrors) = EnrollmentForms.Sanitize(input.EnrollmentForm ?? p.EnrollmentForm);
        foreach (var (field, message) in formErrors) errors.Check(false, field, message);
        var registrationErrors = new List<(string, string)>();
        var registrationInput = input.RegistrationForm ?? p.RegistrationForm;
        var intro = (registrationInput.Intro ?? "").Trim();
        var registration = new RegistrationFormConfig
        {
            Intro = intro.Length > 2000 ? intro[..2000] : intro,
            Questions = EnrollmentForms.SanitizeQuestions(registrationInput.Questions, "registrationForm", perChild: false, registrationErrors),
        };
        foreach (var (field, message) in registrationErrors) errors.Check(false, field, message);
        if (errors.Any) return errors.ToResult();
        if (await db.Programs.AnyAsync(x => x.Slug == slug && x.Id != p.Id))
            return new Errors().Check(false, "slug", "Bu adres başka bir eğitimde kullanılıyor.").ToResult();

        p.Title = input.Title.Trim();
        p.Slug = slug;
        p.Type = input.Type;
        p.AgeMin = input.AgeMin;
        p.AgeMax = input.AgeMax;
        p.StartDate = startDate;
        p.EndDate = endDate;
        p.DaysOfWeek = days;
        p.StartTime = input.StartTime;
        p.EndTime = input.EndTime;
        p.Capacity = input.Capacity;
        if (canPrice)
        {
            p.PriceOptions = options;
            // With a price table the base price is the cheapest package ("… den başlayan").
            p.Price = options.Any(o => !o.IsAddon) ? options.Where(o => !o.IsAddon).Min(o => o.Price) : input.Price;
        }
        p.PricePeriod = monthly ? PricePeriod.Monthly : input.PricePeriod;
        p.Summary = input.Summary?.Trim() ?? "";
        p.Description = input.Description?.Trim() ?? "";
        p.CoverImageUrl = media.ToStored(input.CoverImageUrl);
        p.Location = input.Location?.Trim() ?? "";
        p.IsPublished = input.IsPublished;
        p.EnrollmentEnabled = input.EnrollmentEnabled ?? p.EnrollmentEnabled;
        p.EnrollmentForm = form;
        p.RegistrationForm = registration;

        if (isNew) db.Programs.Add(p);
        SyncSessions(p, db);
        await db.SaveChangesAsync();
        await ui.RevalidateAsync(CacheTags.Programs);

        var dto = Shape(await Project(db.Programs.AsNoTracking().Where(x => x.Id == p.Id)).FirstAsync(), me, media);
        return isNew ? Results.Created($"/api/admin/programs/{p.Id}", dto) : Results.Ok(dto);
    }

    /// <summary>
    /// Makes regular sessions match the schedule; existing dates keep their cancel flag, note and theme.
    /// Make-up lessons are left as they are (one on a newly scheduled date stands in for that lesson).
    /// </summary>
    private static void SyncSessions(TrainingProgram p, AppDbContext db)
    {
        var dates = p.ScheduledDates().ToHashSet();
        // A started lesson holds attendance and notes: it stays even when the schedule no longer has its date.
        foreach (var s in p.Sessions.Where(s => !s.IsMakeup && s.StartedAt == null && !dates.Contains(s.Date)).ToList())
        {
            p.Sessions.Remove(s);
            db.Sessions.Remove(s);
        }
        var existing = p.Sessions.ToDictionary(s => s.Date);
        foreach (var date in dates)
        {
            if (existing.TryGetValue(date, out var s))
            {
                if (s.IsMakeup) continue;
                s.StartTime = p.StartTime;
                s.EndTime = p.EndTime;
            }
            else
            {
                p.Sessions.Add(new Session { ProgramId = p.Id, Date = date, StartTime = p.StartTime, EndTime = p.EndTime });
            }
        }
    }

    private static IQueryable<ProgramAdminDto> Project(IQueryable<TrainingProgram> query) =>
        query.Select(p => new ProgramAdminDto(
            p.Id, p.Title, p.Slug, p.Type, p.AgeMin, p.AgeMax, p.StartDate, p.EndDate, p.DaysOfWeek,
            p.StartTime, p.EndTime, p.Capacity, p.Price, p.PricePeriod, p.PriceOptions, p.Summary, p.Description,
            p.CoverImageUrl, p.Location, p.IsPublished, p.FeaturedOrder, p.EnrollmentEnabled, p.EnrollmentForm, p.RegistrationForm,
            p.Enrollments.Count(e => e.Status == EnrollmentStatus.Active),
            p.Sessions.Count, p.CreatedAt, p.UpdatedAt, p.Enrollments.Count));
}
