using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Auth;

public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);

public record UserInfo(Guid Id, string Email, string FullName, string Role, IReadOnlyList<string> Permissions);

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
    }

    public static UserInfo ToInfo(User u) => new(u.Id, u.Email, u.FullName, u.Role.Name, u.Role.Permissions);
}
