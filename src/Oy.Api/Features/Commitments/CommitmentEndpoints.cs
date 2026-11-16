using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features.Classroom;
using Oy.Api.Infrastructure.Auth;
using Oy.Api.Infrastructure.Push;

namespace Oy.Api.Features.Commitments;

public record ProgramRef(Guid Id, string Title);

/// <summary>A commitment text of the library, with the programs that use it and how many parents signed it.</summary>
public record CommitmentTemplateDto(Guid Id, string Title, string Body, DateTime UpdatedAt, List<ProgramRef> Programs, int SignedCount);

public record CommitmentInput([Required, MaxLength(CommitmentRules.TitleMax)] string Title, [MaxLength(CommitmentRules.BodyMax)] string? Body);

/// <summary>The program's commitments, in order (ids from the library).</summary>
public record ProgramCommitmentsInput(List<Guid>? TemplateIds);

/// <summary>
/// One commitment of one enrollment. <c>Required</c> false: signed earlier, but the program no longer asks for it.
/// <c>Token</c> is null until a link was needed.
/// </summary>
public record CommitmentStatusDto(
    Guid TemplateId, string Title, bool Required, string? Token, DateTime? OpenedAt, DateTime? SignedAt, string SignerName,
    CommitmentChannel? Channel, string WitnessName);

public record EnrollmentCommitmentsDto(Guid EnrollmentId, string ChildName, string ParentName, EnrollmentStatus Status, List<CommitmentStatusDto> Items);

public record ProgramCommitmentsDto(List<CommitmentTemplateDto> Templates, List<EnrollmentCommitmentsDto> Enrollments);

public record CommitmentLinkDto(string Token);

/// <summary>
/// The signing page. <c>Mode</c>: how a signature would be recorded right now — "Tablet" (a staff member is signed in;
/// <c>WitnessName</c>), "GuardianApp" (the parent's own account) or "Link".
/// </summary>
public record PublicCommitment(
    bool Signed, string Title, string Body, string BodyHash, string ChildName, string ProgramTitle, string ParentName,
    DateTime? SignedAt, string SignerName, string Mode, string WitnessName);

public record SignInput(string? SignerName, bool Accepted, string? BodyHash, string? Signature);

/// <summary>A commitment in the guardian app.</summary>
public record GuardianCommitment(
    Guid EnrollmentId, string ChildName, string ProgramTitle, Guid TemplateId, string Title, string Token, DateTime? SignedAt);

/// <summary>
/// Commitments ("taahhütname") signed by a parent with a finger, pen or signature tablet: the text library and
/// each program's choice (programs.write), the signatures of an enrollment (enrollments.read), the public signing page
/// behind a personal token (also used on the school's tablet and in the guardian app), and the signed PDF.
/// </summary>
public static class CommitmentEndpoints
{
    /// <summary>Enrollments whose parents are asked to sign.</summary>
    private static readonly EnrollmentStatus[] Signing = [EnrollmentStatus.Active, EnrollmentStatus.Completed];

    public static void MapCommitments(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Commitments");

        // ---------- Library ----------
        admin.MapGet("/commitments", async (AppDbContext db) => Results.Ok(await TemplatesAsync(db, db.CommitmentTemplates)))
            .RequireAuthorization(Permissions.ProgramsRead);

        admin.MapPost("/commitments", async (CommitmentInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } invalid) return invalid;
            var t = new CommitmentTemplate { Title = input.Title.Trim(), Body = input.Body?.Trim() ?? "" };
            db.CommitmentTemplates.Add(t);
            await db.SaveChangesAsync();
            return Results.Ok((await TemplatesAsync(db, db.CommitmentTemplates.Where(x => x.Id == t.Id))).Single());
        }).RequireAuthorization(Permissions.ProgramsWrite);

        admin.MapPut("/commitments/{id:guid}", async (Guid id, CommitmentInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } invalid) return invalid;
            var t = await db.CommitmentTemplates.FindAsync(id);
            if (t is null) return Results.NotFound();
            t.Title = input.Title.Trim();
            t.Body = input.Body?.Trim() ?? "";
            await db.SaveChangesAsync();
            return Results.Ok((await TemplatesAsync(db, db.CommitmentTemplates.Where(x => x.Id == id))).Single());
        }).RequireAuthorization(Permissions.ProgramsWrite);

        admin.MapDelete("/commitments/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            if (await db.CommitmentSignatures.AnyAsync(s => s.TemplateId == id && s.SignedAt != null))
                return ApiResults.Conflict("İmzalanmış bir taahhütname silinemez. Eğitimlerden kaldırabilirsiniz.");
            await db.CommitmentSignatures.Where(s => s.TemplateId == id).ExecuteDeleteAsync();
            return await db.CommitmentTemplates.Where(t => t.Id == id).ExecuteDeleteAsync() == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization(Permissions.ProgramsWrite);

        // ---------- A program's commitments ----------
        admin.MapGet("/programs/{id:guid}/commitments", async (Guid id, AppDbContext db) =>
            await db.Programs.AnyAsync(p => p.Id == id) ? Results.Ok(await ProgramAsync(db, id)) : Results.NotFound())
            .RequireAuthorization(Permissions.ProgramsRead);

        admin.MapPut("/programs/{id:guid}/commitments", async (Guid id, ProgramCommitmentsInput input, AppDbContext db, Notifier notifier) =>
        {
            var program = await db.Programs.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (program is null) return Results.NotFound();
            var ids = (input.TemplateIds ?? []).Distinct().ToList();
            if (await db.CommitmentTemplates.CountAsync(t => ids.Contains(t.Id)) != ids.Count)
                return new Errors().Check(false, "templateIds", "Taahhütname bulunamadı.").ToResult();

            var current = await db.ProgramCommitments.Where(p => p.ProgramId == id).ToListAsync();
            var added = ids.Except(current.Select(c => c.TemplateId)).ToList();
            db.ProgramCommitments.RemoveRange(current.Where(c => !ids.Contains(c.TemplateId)));
            for (var i = 0; i < ids.Count; i++)
            {
                var row = current.FirstOrDefault(c => c.TemplateId == ids[i]);
                if (row is null) db.ProgramCommitments.Add(new ProgramCommitment { ProgramId = id, TemplateId = ids[i], Position = i });
                else row.Position = i;
            }
            // Links not signed yet are only for required commitments.
            var removed = current.Select(c => c.TemplateId).Except(ids).ToList();
            await db.CommitmentSignatures
                .Where(s => s.Enrollment.ProgramId == id && removed.Contains(s.TemplateId) && s.SignedAt == null)
                .ExecuteDeleteAsync();
            await db.SaveChangesAsync();

            if (added.Count > 0)
                await notifier.SendAsync(await notifier.GuardiansOfProgramAsync(id), NotificationKind.CommitmentRequested,
                    "İmzanızı bekleyen taahhütname", $"{program.Title} için imzalamanız gereken bir belge var.", "/veli/belgeler");
            return Results.Ok(await ProgramAsync(db, id));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        // ---------- An enrollment's commitments ----------
        admin.MapGet("/enrollments/{id:guid}/commitments", async (Guid id, AppDbContext db) =>
        {
            var e = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            return e is null ? Results.NotFound() : Results.Ok((await StatusesAsync(db, [e]))[0].Items);
        }).RequireAuthorization(Permissions.EnrollmentsRead);

        // The personal link (created on first use). Staff open it on a tablet or send it to the parent.
        admin.MapPost("/enrollments/{id:guid}/commitments/{templateId:guid}/link", async (Guid id, Guid templateId, AppDbContext db) =>
        {
            var e = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return Results.NotFound();
            if (!Signing.Contains(e.Status)) return ApiResults.Conflict("Taahhütname yalnızca onaylı kayıtlar için imzalatılabilir.");
            if (!await db.ProgramCommitments.AnyAsync(p => p.ProgramId == e.ProgramId && p.TemplateId == templateId))
                return ApiResults.Conflict("Bu taahhütname kaydın eğitiminde istenmiyor.");
            var s = await EnsureAsync(db, id, templateId);
            return Results.Ok(new CommitmentLinkDto(s.Token));
        }).RequireAuthorization(Permissions.EnrollmentsRead);

        // ---------- Guardian app ----------
        app.MapGet("/api/guardian/commitments", async (ClaimsPrincipal me, AppDbContext db) =>
        {
            var uid = me.UserId();
            var enrollments = await db.GuardianLinks.AsNoTracking()
                .Where(l => l.UserId == uid && Signing.Contains(l.Enrollment.Status))
                .Select(l => l.Enrollment).ToListAsync();
            var rows = new List<GuardianCommitment>();
            foreach (var group in await StatusesAsync(db, enrollments))
            {
                var e = enrollments.First(x => x.Id == group.EnrollmentId);
                foreach (var item in group.Items)
                {
                    // Every required commitment gets its link, so the guardian can open it right away.
                    var token = item.Token ?? (await EnsureAsync(db, e.Id, item.TemplateId)).Token;
                    rows.Add(new GuardianCommitment(e.Id, e.ChildName, group.ProgramTitle, item.TemplateId, item.Title, token, item.SignedAt));
                }
            }
            return Results.Ok(rows.OrderBy(r => r.SignedAt != null).ThenBy(r => r.ChildName).ToList());
        }).WithTags("Guardian app").RequireAuthorization(Permissions.Guardian);

        // ---------- Signing page (personal token; also the tablet and the guardian app) ----------
        var pub = app.MapGroup("/api/public/commitments").WithTags("Public").AllowAnonymous();

        pub.MapGet("/{token}", async (string token, ClaimsPrincipal me, AppDbContext db) =>
        {
            var s = await FindAsync(db.CommitmentSignatures.AsNoTracking(), token);
            if (s is null) return Results.NotFound();
            var e = s.Enrollment;
            if (s.SignedAt is not null)
                return Results.Ok(new PublicCommitment(true, s.SignedTitle, s.SignedBody, s.BodyHash, e.ChildName, e.Program.Title,
                    e.ParentName, s.SignedAt, s.SignerName, s.Channel?.ToString() ?? "", s.WitnessName));
            var (title, body, hash) = Render(s);
            var (mode, witness, _) = await SignerAsync(me, db, s);
            return Results.Ok(new PublicCommitment(false, title, body, hash, e.ChildName, e.Program.Title, e.ParentName,
                null, "", mode.ToString(), witness));
        });

        // Called by the page in the browser, so link previews do not count as "opened".
        pub.MapPost("/{token}/opened", async (string token, AppDbContext db) =>
        {
            if (!RegistrationLinks.LooksLikeToken(token)) return Results.NotFound();
            await db.CommitmentSignatures.Where(s => s.Token == token && s.OpenedAt == null)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.OpenedAt, DateTime.UtcNow));
            return Results.NoContent();
        }).RequireRateLimiting("registration-forms");

        pub.MapPost("/{token}", async (string token, SignInput input, ClaimsPrincipal me, HttpContext http, AppDbContext db, Notifier notifier) =>
        {
            var s = await FindAsync(db.CommitmentSignatures, token);
            if (s is null) return Results.NotFound();
            if (s.SignedAt is not null) return ApiResults.Conflict("Bu taahhütname daha önce imzalandı.");
            if (!Signing.Contains(s.Enrollment.Status) ||
                !await db.ProgramCommitments.AnyAsync(p => p.ProgramId == s.Enrollment.ProgramId && p.TemplateId == s.TemplateId))
                return ApiResults.Conflict("Bu taahhütname artık imzaya açık değil. Lütfen bizimle iletişime geçin.");

            var (title, body, hash) = Render(s);
            if (input.BodyHash != hash)
                return ApiResults.Conflict("Taahhütname metni siz okurken güncellendi. Lütfen sayfayı yenileyip yeni metni okuyun.");
            var name = input.SignerName?.Trim() ?? "";
            var image = CommitmentRules.DecodeSignature(input.Signature);
            var errors = new Errors()
                .Check(name.Length >= 3, "signerName", "Adınızı ve soyadınızı yazın.")
                .Check(name.Length <= CommitmentRules.SignerNameMax, "signerName", "Ad soyad çok uzun.")
                .Check(input.Accepted, "accepted", "Metni okuyup kabul ettiğinizi işaretleyin.")
                .Check(image is not null, "signature", "İmza alanına imzanızı atın.");
            if (errors.Any) return errors.ToResult();

            var (mode, witness, userId) = await SignerAsync(me, db, s);
            s.SignedAt = DateTime.UtcNow;
            s.OpenedAt ??= s.SignedAt;
            s.SignedTitle = title;
            s.SignedBody = body;
            s.BodyHash = hash;
            s.SignerName = name;
            s.SignatureImage = image;
            s.Channel = mode;
            s.WitnessName = witness;
            s.SignedByUserId = userId;
            s.IpAddress = http.Connection.RemoteIpAddress?.ToString() ?? "";
            var agent = http.Request.Headers.UserAgent.ToString();
            s.UserAgent = agent.Length > 500 ? agent[..500] : agent;
            await db.SaveChangesAsync();

            await notifier.SendAsync(await notifier.StaffWithAsync(Permissions.EnrollmentsRead), NotificationKind.CommitmentSigned,
                "Taahhütname imzalandı", $"{s.Enrollment.ChildName} · {title}", $"/kayitlar?kayit={s.EnrollmentId}");
            return Results.NoContent();
        }).RequireRateLimiting("registration-forms");

        // The signed document; whoever holds the personal token (parent, staff, guardian app) can download it.
        pub.MapGet("/{token}/pdf", async (string token, AppDbContext db) =>
        {
            var s = await FindAsync(db.CommitmentSignatures.AsNoTracking(), token);
            if (s?.SignedAt is null) return Results.NotFound();
            var pdf = CommitmentPdf.Build(s, s.Enrollment.ChildName, s.Enrollment.Program.Title);
            return Results.File(pdf, "application/pdf", CommitmentPdf.FileName(s, s.Enrollment.ChildName));
        }).RequireRateLimiting("registration-forms");
    }

    // ---------- helpers ----------

    private static IResult? Validate(CommitmentInput input)
    {
        var errors = new Errors()
            .Check(!string.IsNullOrWhiteSpace(input.Title), "title", "Başlık yazın.")
            .Check((input.Title?.Trim().Length ?? 0) <= CommitmentRules.TitleMax, "title", "Başlık çok uzun.")
            .Check(!string.IsNullOrWhiteSpace(input.Body), "body", "Taahhütname metnini yazın.")
            .Check((input.Body?.Trim().Length ?? 0) <= CommitmentRules.BodyMax, "body", "Metin çok uzun.");
        return errors.Any ? errors.ToResult() : null;
    }

    private static Task<List<CommitmentTemplateDto>> TemplatesAsync(AppDbContext db, IQueryable<CommitmentTemplate> query) =>
        query.AsNoTracking().OrderBy(t => t.Title)
            .Select(t => new CommitmentTemplateDto(t.Id, t.Title, t.Body, t.UpdatedAt,
                t.Programs.OrderBy(p => p.Program.Title).Select(p => new ProgramRef(p.ProgramId, p.Program.Title)).ToList(),
                db.CommitmentSignatures.Count(s => s.TemplateId == t.Id && s.SignedAt != null)))
            .ToListAsync();

    private static async Task<ProgramCommitmentsDto> ProgramAsync(AppDbContext db, Guid programId)
    {
        var order = await db.ProgramCommitments.AsNoTracking().Where(p => p.ProgramId == programId)
            .OrderBy(p => p.Position).Select(p => p.TemplateId).ToListAsync();
        var templates = await TemplatesAsync(db, db.CommitmentTemplates.Where(t => order.Contains(t.Id)));
        var enrollments = await db.Enrollments.AsNoTracking()
            .Where(e => e.ProgramId == programId && Signing.Contains(e.Status))
            .OrderBy(e => e.ChildName).ToListAsync();
        var statuses = await StatusesAsync(db, enrollments);
        return new ProgramCommitmentsDto(
            templates.OrderBy(t => order.IndexOf(t.Id)).ToList(),
            statuses.Select(s => new EnrollmentCommitmentsDto(s.EnrollmentId, s.ChildName, s.ParentName, s.Status, s.Items)).ToList());
    }

    private sealed record EnrollmentStatuses(
        Guid EnrollmentId, string ChildName, string ParentName, EnrollmentStatus Status, string ProgramTitle, List<CommitmentStatusDto> Items);

    /// <summary>For each enrollment: the commitments its program asks for (in order), then any signed earlier and no longer asked for.</summary>
    private static async Task<List<EnrollmentStatuses>> StatusesAsync(AppDbContext db, List<Enrollment> enrollments)
    {
        var ids = enrollments.Select(e => e.Id).ToList();
        var programIds = enrollments.Select(e => e.ProgramId).Distinct().ToList();
        var required = await db.ProgramCommitments.AsNoTracking().Where(p => programIds.Contains(p.ProgramId))
            .OrderBy(p => p.Position).Select(p => new { p.ProgramId, p.TemplateId, p.Template.Title })
            .ToListAsync();
        var programTitles = await db.Programs.AsNoTracking().Where(p => programIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Title);
        var signatures = await db.CommitmentSignatures.AsNoTracking().Where(s => ids.Contains(s.EnrollmentId))
            .Select(s => new
            {
                s.EnrollmentId, s.TemplateId, TemplateTitle = s.Template.Title, s.Token, s.OpenedAt, s.SignedAt, s.SignedTitle,
                s.SignerName, s.Channel, s.WitnessName,
            })
            .ToListAsync();

        return enrollments.Select(e =>
        {
            var mine = signatures.Where(s => s.EnrollmentId == e.Id).ToList();
            var asked = required.Where(r => r.ProgramId == e.ProgramId).ToList();
            var items = asked.Select(r =>
            {
                var s = mine.FirstOrDefault(x => x.TemplateId == r.TemplateId);
                return new CommitmentStatusDto(r.TemplateId, s?.SignedAt is not null ? s.SignedTitle : r.Title, true, s?.Token,
                    s?.OpenedAt, s?.SignedAt, s?.SignerName ?? "", s?.Channel, s?.WitnessName ?? "");
            }).ToList();
            items.AddRange(mine.Where(s => s.SignedAt is not null && asked.All(r => r.TemplateId != s.TemplateId))
                .Select(s => new CommitmentStatusDto(s.TemplateId, s.SignedTitle, false, s.Token, s.OpenedAt, s.SignedAt, s.SignerName,
                    s.Channel, s.WitnessName)));
            return new EnrollmentStatuses(e.Id, e.ChildName, e.ParentName, e.Status, programTitles.GetValueOrDefault(e.ProgramId, ""), items);
        }).ToList();
    }

    /// <summary>The link of an enrollment's commitment, created on first use.</summary>
    private static async Task<CommitmentSignature> EnsureAsync(AppDbContext db, Guid enrollmentId, Guid templateId)
    {
        var existing = await db.CommitmentSignatures.FirstOrDefaultAsync(s => s.EnrollmentId == enrollmentId && s.TemplateId == templateId);
        if (existing is not null) return existing;
        var s = new CommitmentSignature { EnrollmentId = enrollmentId, TemplateId = templateId, Token = RegistrationLinks.NewToken() };
        db.CommitmentSignatures.Add(s);
        try
        {
            await db.SaveChangesAsync();
            return s;
        }
        catch (DbUpdateException)
        {
            // Created at the same moment by another request (e.g. two staff members): use that one.
            db.Entry(s).State = EntityState.Detached;
            return await db.CommitmentSignatures.FirstAsync(x => x.EnrollmentId == enrollmentId && x.TemplateId == templateId);
        }
    }

    private static async Task<CommitmentSignature?> FindAsync(IQueryable<CommitmentSignature> query, string token) =>
        RegistrationLinks.LooksLikeToken(token)
            ? await query.Include(s => s.Template).Include(s => s.Enrollment).ThenInclude(e => e.Program).FirstOrDefaultAsync(s => s.Token == token)
            : null;

    /// <summary>Title, text and fingerprint as the parent reads it today.</summary>
    private static (string Title, string Body, string Hash) Render(CommitmentSignature s)
    {
        var e = s.Enrollment;
        var today = ClassroomEndpoints.Today();
        var title = CommitmentRules.Render(s.Template.Title, e.ParentName, e.ChildName, e.Program.Title, today).Trim();
        var body = CommitmentRules.Render(s.Template.Body, e.ParentName, e.ChildName, e.Program.Title, today).Trim();
        return (title, body, CommitmentRules.Hash(title, body));
    }

    /// <summary>
    /// How the signature is recorded: a signed-in staff member means the school's tablet (they are the witness); the
    /// guardian's own account linked to this child means the app; otherwise the personal link.
    /// </summary>
    private static async Task<(CommitmentChannel Mode, string Witness, Guid? UserId)> SignerAsync(ClaimsPrincipal me, AppDbContext db, CommitmentSignature s)
    {
        if (me.UserId() is not { } uid) return (CommitmentChannel.Link, "", null);
        var user = await db.Users.AsNoTracking().Where(u => u.Id == uid && u.IsActive).Select(u => new { u.Kind, u.FullName }).FirstOrDefaultAsync();
        if (user?.Kind == UserKind.Staff) return (CommitmentChannel.Tablet, user.FullName, uid);
        if (user?.Kind == UserKind.Guardian && await db.GuardianLinks.AnyAsync(l => l.UserId == uid && l.EnrollmentId == s.EnrollmentId))
            return (CommitmentChannel.GuardianApp, "", uid);
        return (CommitmentChannel.Link, "", null);
    }
}
