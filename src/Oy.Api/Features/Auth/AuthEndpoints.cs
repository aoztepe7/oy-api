using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

/// <summary><c>Kind</c> decides the app: Staff → panel, Guardian → the guardians' feed.</summary>
public record UserInfo(Guid Id, string Email, string FullName, string Role, IReadOnlyList<string> Permissions, UserKind Kind);

public record ChangePasswordInput([Required] string CurrentPassword, [Required, MinLength(8)] string NewPassword);

public record LoginResponse(string AccessToken, DateTime ExpiresAt, UserInfo User);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest req, AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) =>
        {
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Email == email);
            if (user is null || !user.IsActive ||
                hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password) == PasswordVerificationResult.Failed)
                return Results.Problem(title: "E-posta veya şifre hatalı.", statusCode: StatusCodes.Status401Unauthorized);

            var (token, expires) = tokens.Create(user);
            return Results.Ok(new LoginResponse(token, expires, ToInfo(user)));
        }).RequireRateLimiting("login").AllowAnonymous();

        group.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var id = principal.UserId();
            var user = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
            return user is null ? Results.Unauthorized() : Results.Ok(ToInfo(user));
        }).RequireAuthorization();

        // Any signed-in user (guardians replace the temporary password they were given).
        group.MapPut("/password", async (ChangePasswordInput input, ClaimsPrincipal principal, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var id = principal.UserId();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id && u.IsActive);
            if (user is null) return Results.Unauthorized();
            if (hasher.VerifyHashedPassword(user, user.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
                return new Errors().Check(false, "currentPassword", "Mevcut şifre hatalı.").ToResult();
            user.PasswordHash = hasher.HashPassword(user, input.NewPassword);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("login");
    }

    public static UserInfo ToInfo(User u) => u.Kind == UserKind.Guardian || u.Role is null
        ? new(u.Id, u.Email, u.FullName, "Veli", [], u.Kind)
        : new(u.Id, u.Email, u.FullName, u.Role.Name, u.Role.Permissions, u.Kind);
}
