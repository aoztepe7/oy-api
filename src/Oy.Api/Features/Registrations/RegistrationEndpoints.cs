using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;
using Oy.Api.Infrastructure.Push;

namespace Oy.Api.Features.Registrations;

/// <summary>
/// Registration form as the parent sees it. Questions and values are only sent while the form is open
/// (<see cref="RegistrationStatus.Sent"/> / <see cref="RegistrationStatus.Opened"/>).
/// </summary>
public record PublicRegistration(
    RegistrationStatus Status, string ChildName, string ProgramTitle, string Intro, List<FormQuestion> Questions,
    Dictionary<string, string> Values, DateTime ExpiresAt, DateTime? SubmittedAt);

/// <summary>Answers by question id; MultiSelect values one option per line.</summary>
public record RegistrationSubmission(Dictionary<string, string>? Answers);

/// <summary>The registration form behind a parent's personal link (okulumyeryuzu.com/kayit/{token}).</summary>
public static class RegistrationEndpoints
{
    public static void MapRegistrations(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/public/registrations").WithTags("Public").AllowAnonymous();

        group.MapGet("/{token}", async (string token, AppDbContext db) =>
        {
            var r = await Find(db.Registrations.AsNoTracking(), token);
            if (r is null) return Results.NotFound();
            var status = r.StatusAt(DateTime.UtcNow);
            var open = status is RegistrationStatus.Sent or RegistrationStatus.Opened;
            return Results.Ok(new PublicRegistration(
                status, r.Enrollment.ChildName, r.Enrollment.Program.Title, open ? r.Intro : "",
                open ? r.Questions : [], open ? r.Values : [], r.ExpiresAt, r.SubmittedAt));
        });

        // Called by the page in the browser, so link previews (WhatsApp, e-mail scanners) do not count as "opened".
        group.MapPost("/{token}/opened", async (string token, AppDbContext db) =>
        {
            if (!RegistrationLinks.LooksLikeToken(token)) return Results.NotFound();
            await db.Registrations.Where(r => r.Token == token && r.OpenedAt == null && r.SubmittedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.OpenedAt, DateTime.UtcNow));
            return Results.NoContent();
        }).RequireRateLimiting("registration-forms");

        group.MapPost("/{token}", async (string token, RegistrationSubmission input, AppDbContext db, Notifier notifier) =>
        {
            var r = await Find(db.Registrations, token);
            if (r is null) return Results.NotFound();
            var now = DateTime.UtcNow;
            switch (r.StatusAt(now))
            {
                case RegistrationStatus.Submitted:
                    return ApiResults.Conflict("Bu form daha önce gönderildi.");
                case RegistrationStatus.Expired:
                    return ApiResults.Conflict("Bu formun süresi doldu. Lütfen bizimle iletişime geçin.");
            }

            var errors = new Errors();
            var values = new Dictionary<string, string>();
            var answers = EnrollmentForms.CheckAnswers(r.Questions, input.Answers, "answers.", values, (key, message) => errors.Check(false, key, message));
            if (errors.Any) return errors.ToResult();

            r.Values = values;
            r.Answers = answers;
            r.SubmittedAt = now;
            r.OpenedAt ??= now;
            await db.SaveChangesAsync();
            await notifier.SendAsync(await notifier.StaffWithAsync(Permissions.EnrollmentsRead), NotificationKind.RegistrationSubmitted,
                "Esas kayıt formu dolduruldu", $"{r.Enrollment.ChildName} · {r.Enrollment.Program.Title}", "/kayitlar?form=Submitted");
            return Results.Accepted();
        }).RequireRateLimiting("registration-forms");
    }

    private static async Task<RegistrationRequest?> Find(IQueryable<RegistrationRequest> query, string token) =>
        RegistrationLinks.LooksLikeToken(token)
            ? await query.Include(r => r.Enrollment).ThenInclude(e => e.Program).FirstOrDefaultAsync(r => r.Token == token)
            : null;
}
