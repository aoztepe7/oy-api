using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;

namespace Oy.Api.Features.Guardians;

public record GuardianDto(
    Guid LinkId, Guid UserId, string FullName, string Email, string Phone, GuardianRelation Relation,
    bool IsActive, DateTime CreatedAt, int ChildCount);

/// <summary><c>Password</c> empty → a temporary one is generated and returned once.</summary>
public record GuardianInput(
    [Required, MaxLength(200)] string FullName,
    [Required, EmailAddress, MaxLength(256)] string Email,
    [MaxLength(50)] string? Phone,
    GuardianRelation Relation,
    [MinLength(8)] string? Password = null);

public record GuardianUpdate([Required, MaxLength(200)] string FullName, [MaxLength(50)] string? Phone, GuardianRelation Relation, bool IsActive);

/// <summary><c>TemporaryPassword</c> is shown once to hand to the parent; <c>LinkedExisting</c>: an existing guardian (sibling) was linked.</summary>
public record GuardianCreated(GuardianDto Guardian, string? TemporaryPassword, bool LinkedExisting);

public record PasswordReset(string TemporaryPassword);

/// <summary>Guardian (veli) logins of an approved enrollment: create, link a sibling's parent, reset, remove.</summary>
public static class GuardianAdminEndpoints
{
    public static void MapGuardianAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Guardians").RequireAuthorization(Permissions.GuardiansManage);

        group.MapGet("/enrollments/{id:guid}/guardians", async (Guid id, AppDbContext db) =>
            await Project(db.GuardianLinks.Where(l => l.EnrollmentId == id)).ToListAsync());

        group.MapPost("/enrollments/{id:guid}/guardians", async (Guid id, GuardianInput input, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var enrollment = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
            if (enrollment is null) return Results.NotFound();
            if (enrollment.Status is not (EnrollmentStatus.Active or EnrollmentStatus.Completed))
                return ApiResults.Conflict("Veli hesabı yalnızca onaylanmış (aktif) kayıtlara açılabilir.");

            var email = input.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is { Kind: UserKind.Staff })
                return new Errors().Check(false, "email", "Bu e-posta bir panel kullanıcısına ait; veli için farklı bir e-posta girin.").ToResult();

            string? temporary = null;
            var existing = user is not null;
            if (user is null)
            {
                temporary = string.IsNullOrEmpty(input.Password) ? NewPassword() : null;
                user = new User
                {
                    Email = email, FullName = input.FullName.Trim(), Phone = input.Phone?.Trim() ?? "", Kind = UserKind.Guardian,
                };
                user.PasswordHash = hasher.HashPassword(user, temporary ?? input.Password!);
                db.Users.Add(user);
            }
            else if (await db.GuardianLinks.AnyAsync(l => l.UserId == user.Id && l.EnrollmentId == id))
                return ApiResults.Conflict("Bu veli bu kayda zaten bağlı.");

            var link = new GuardianLink { UserId = user.Id, EnrollmentId = id, Relation = input.Relation };
            db.GuardianLinks.Add(link);
            await db.SaveChangesAsync();
            var dto = await Project(db.GuardianLinks.Where(l => l.Id == link.Id)).FirstAsync();
            return Results.Ok(new GuardianCreated(dto, temporary, existing));
        });

        group.MapPut("/guardian-links/{id:guid}", async (Guid id, GuardianUpdate input, AppDbContext db) =>
        {
            var link = await db.GuardianLinks.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
            if (link is null) return Results.NotFound();
            link.Relation = input.Relation;
            link.User.FullName = input.FullName.Trim();
            link.User.Phone = input.Phone?.Trim() ?? "";
            link.User.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            return Results.Ok(await Project(db.GuardianLinks.Where(l => l.Id == id)).FirstAsync());
        });

        group.MapPost("/guardian-links/{id:guid}/reset-password", async (Guid id, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var link = await db.GuardianLinks.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
            if (link is null) return Results.NotFound();
            var password = NewPassword();
            link.User.PasswordHash = hasher.HashPassword(link.User, password);
            await db.SaveChangesAsync();
            return Results.Ok(new PasswordReset(password));
        });

        // Removes the guardian from this child; an account left without children is deleted.
        group.MapDelete("/guardian-links/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var link = await db.GuardianLinks.FirstOrDefaultAsync(l => l.Id == id);
            if (link is null) return Results.NotFound();
            db.GuardianLinks.Remove(link);
            await db.SaveChangesAsync();
            if (!await db.GuardianLinks.AnyAsync(l => l.UserId == link.UserId))
                await db.Users.Where(u => u.Id == link.UserId && u.Kind == UserKind.Guardian).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    private static IQueryable<GuardianDto> Project(IQueryable<GuardianLink> q) =>
        q.OrderBy(l => l.CreatedAt).Select(l => new GuardianDto(
            l.Id, l.UserId, l.User.FullName, l.User.Email, l.User.Phone, l.Relation, l.User.IsActive, l.CreatedAt,
            l.User.GuardianLinks.Count));

    /// <summary>10 characters without look-alikes (0/O, 1/l/I), easy to read out on the phone.</summary>
    public static string NewPassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(10, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }
}
