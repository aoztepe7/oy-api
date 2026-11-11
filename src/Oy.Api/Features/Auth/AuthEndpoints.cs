using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Auth;

/// <summary><c>Device</c> is sent by the mobile app: it gets a long-lived session (refresh token) for that device.</summary>
public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password, DeviceInfo? Device = null);

public record DeviceInfo(DevicePlatform Platform, [MaxLength(100)] string? Name, [MaxLength(30)] string? AppVersion);

public record RefreshRequest([Required, MaxLength(100)] string RefreshToken);

public record PushTokenInput([Required, MaxLength(512)] string Token);

public record SessionDto(Guid Id, DevicePlatform Platform, string DeviceName, string AppVersion, DateTime CreatedAt, DateTime LastUsedAt, bool IsCurrent);

/// <summary><c>Kind</c> decides the app: Staff → panel, Guardian → the guardians' feed.</summary>
public record UserInfo(Guid Id, string Email, string FullName, string Role, IReadOnlyList<string> Permissions, UserKind Kind);

public record ChangePasswordInput([Required] string CurrentPassword, [Required, MinLength(8)] string NewPassword);

/// <summary><c>RefreshToken</c> only for app sessions: store it securely and exchange it at /api/auth/refresh.</summary>
public record LoginResponse(string AccessToken, DateTime ExpiresAt, UserInfo User, string? RefreshToken = null, Guid? SessionId = null);

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

            if (req.Device is { Platform: not DevicePlatform.Web } device)
            {
                var now = DateTime.UtcNow;
                var refresh = Sessions.NewRefreshToken();
                var session = new UserSession
                {
                    UserId = user.Id, Platform = device.Platform, DeviceName = device.Name?.Trim() ?? "", AppVersion = device.AppVersion?.Trim() ?? "",
                    TokenHash = Sessions.Hash(refresh), ExpiresAt = now + Sessions.Lifetime, LastUsedAt = now,
                };
                db.UserSessions.Add(session);
                await db.SaveChangesAsync();
                var (sessionToken, sessionExpires) = tokens.Create(user, session.Id);
                return Results.Ok(new LoginResponse(sessionToken, sessionExpires, ToInfo(user), refresh, session.Id));
            }

            var (token, expires) = tokens.Create(user);
            return Results.Ok(new LoginResponse(token, expires, ToInfo(user)));
        }).RequireRateLimiting("login").AllowAnonymous();

        // App sessions: a new access token (and a new refresh token) for the current refresh token.
        group.MapPost("/refresh", async (RefreshRequest req, AppDbContext db, TokenService tokens) =>
        {
            var now = DateTime.UtcNow;
            var hash = Sessions.Hash(req.RefreshToken);
            var session = await db.UserSessions.Include(s => s.User).ThenInclude(u => u.Role).FirstOrDefaultAsync(s => s.TokenHash == hash);
            if (session is null)
            {
                // An already-rotated token used again: someone else has it. Close that session.
                await db.UserSessions.Where(s => s.PreviousTokenHash == hash && s.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.PushToken, ""));
                return SessionEnded();
            }
            if (!session.IsActive(now) || !session.User.IsActive) return SessionEnded();

            var refresh = Sessions.NewRefreshToken();
            session.PreviousTokenHash = session.TokenHash;
            session.TokenHash = Sessions.Hash(refresh);
            session.LastUsedAt = now;
            session.ExpiresAt = now + Sessions.Lifetime;
            await db.SaveChangesAsync();
            var (token, expires) = tokens.Create(session.User, session.Id);
            return Results.Ok(new LoginResponse(token, expires, ToInfo(session.User), refresh, session.Id));
        }).RequireRateLimiting("login").AllowAnonymous();

        // Sign out of the app: the session ends and the device stops receiving notifications.
        group.MapPost("/logout", async (RefreshRequest req, AppDbContext db) =>
        {
            var hash = Sessions.Hash(req.RefreshToken);
            await db.UserSessions.Where(s => s.TokenHash == hash && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow).SetProperty(x => x.PushToken, ""));
            return Results.NoContent();
        }).AllowAnonymous();

        // The app registers (or renews) its push notification token for the current session.
        group.MapPut("/push-token", async (PushTokenInput input, ClaimsPrincipal principal, AppDbContext db) =>
        {
            if (principal.SessionId() is not { } sid) return Results.BadRequest();
            var token = input.Token.Trim();
            // A token belongs to one device: after reinstalling or switching accounts, older sessions must not get its notifications.
            await db.UserSessions.Where(s => s.PushToken == token && s.Id != sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.PushToken, ""));
            await db.UserSessions.Where(s => s.Id == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.PushToken, token));
            return Results.NoContent();
        }).RequireAuthorization();

        // Devices signed in to the account (account page): sign out one remotely.
        group.MapGet("/sessions", async (ClaimsPrincipal principal, AppDbContext db) =>
        {
            var id = principal.UserId();
            var current = principal.SessionId();
            var now = DateTime.UtcNow;
            return await db.UserSessions.AsNoTracking().Where(s => s.UserId == id && s.RevokedAt == null && s.ExpiresAt > now)
                .OrderByDescending(s => s.LastUsedAt)
                .Select(s => new SessionDto(s.Id, s.Platform, s.DeviceName, s.AppVersion, s.CreatedAt, s.LastUsedAt, s.Id == current))
                .ToListAsync();
        }).RequireAuthorization();

        group.MapDelete("/sessions/{sessionId:guid}", async (Guid sessionId, ClaimsPrincipal principal, AppDbContext db) =>
        {
            var id = principal.UserId();
            return await db.UserSessions.Where(s => s.Id == sessionId && s.UserId == id && s.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow).SetProperty(x => x.PushToken, "")) > 0
                ? Results.NoContent()
                : Results.NotFound();
        }).RequireAuthorization();

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
            // A new password signs out the other devices.
            var current = principal.SessionId();
            await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null && s.Id != current)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow).SetProperty(x => x.PushToken, ""));
            return Results.NoContent();
        }).RequireAuthorization().RequireRateLimiting("login");
    }

    private static IResult SessionEnded() =>
        Results.Problem(title: "Oturumunuz sona erdi. Lütfen yeniden giriş yapın.", statusCode: StatusCodes.Status401Unauthorized);

    public static UserInfo ToInfo(User u) => u.Kind == UserKind.Guardian || u.Role is null
        ? new(u.Id, u.Email, u.FullName, "Veli", [], u.Kind)
        : new(u.Id, u.Email, u.FullName, u.Role.Name, u.Role.Permissions, u.Kind);
}
