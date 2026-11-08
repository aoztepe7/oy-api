using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Users;

public record RoleInput([Required, MaxLength(100)] string Name, List<string> Permissions);

public record RoleDto(Guid Id, string Name, IReadOnlyList<string> Permissions, int UserCount);

public record CreateUserInput(
    [Required, EmailAddress] string Email, [Required, MaxLength(200)] string FullName,
    [Required, MinLength(8)] string Password, Guid RoleId, bool IsActive = true);

public record UpdateUserInput([Required, MaxLength(200)] string FullName, Guid RoleId, bool IsActive, [MinLength(8)] string? Password);

public record UserDto(Guid Id, string Email, string FullName, Guid RoleId, string RoleName, bool IsActive, DateTime CreatedAt);

public static class UserEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("Users & Roles").RequireAuthorization(Permissions.UsersManage);

        group.MapGet("/permissions", () => Permissions.All.Select(p => new { key = p.Key, label = p.Value }));

        // Roles
        group.MapGet("/roles", async (AppDbContext db) =>
            await db.Roles.OrderBy(r => r.Name)
                .Select(r => new RoleDto(r.Id, r.Name, r.Permissions, r.Users.Count)).ToListAsync());

        group.MapPost("/roles", async (RoleInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } invalid) return invalid;
            if (await db.Roles.AnyAsync(r => r.Name == input.Name.Trim())) return ApiResults.Conflict("Bu isimde bir rol zaten var.");
            var role = new Role { Name = input.Name.Trim(), Permissions = input.Permissions.Distinct().ToList() };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/roles/{role.Id}", new RoleDto(role.Id, role.Name, role.Permissions, 0));
        });

        group.MapPut("/roles/{id:guid}", async (Guid id, RoleInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } invalid) return invalid;
            var role = await db.Roles.FindAsync(id);
            if (role is null) return Results.NotFound();
            role.Name = input.Name.Trim();
            role.Permissions = input.Permissions.Distinct().ToList();
            await db.SaveChangesAsync();
            return Results.Ok(new RoleDto(role.Id, role.Name, role.Permissions, await db.Users.CountAsync(u => u.RoleId == id)));
        });

        group.MapDelete("/roles/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            if (await db.Users.AnyAsync(u => u.RoleId == id)) return ApiResults.Conflict("Bu role atanmış kullanıcılar var.");
            return await db.Roles.Where(r => r.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        });

        // Users
        group.MapGet("/users", async (AppDbContext db) =>
            await db.Users.OrderBy(u => u.FullName)
                .Select(u => new UserDto(u.Id, u.Email, u.FullName, u.RoleId, u.Role.Name, u.IsActive, u.CreatedAt)).ToListAsync());

        group.MapPost("/users", async (CreateUserInput input, AppDbContext db, IPasswordHasher<User> hasher) =>
        {
            var email = input.Email.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.Email == email)) return ApiResults.Conflict("Bu e-posta ile kayıtlı kullanıcı var.");
            var role = await db.Roles.FindAsync(input.RoleId);
            if (role is null) return new Errors().Check(false, "roleId", "Rol bulunamadı.").ToResult();

            var user = new User { Email = email, FullName = input.FullName.Trim(), RoleId = role.Id, IsActive = input.IsActive };
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            db.Users.Add(user);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/users/{user.Id}",
                new UserDto(user.Id, user.Email, user.FullName, role.Id, role.Name, user.IsActive, user.CreatedAt));
        });

        group.MapPut("/users/{id:guid}", async (Guid id, UpdateUserInput input, AppDbContext db, IPasswordHasher<User> hasher, ClaimsPrincipal me) =>
        {
            var user = await db.Users.FindAsync(id);
            if (user is null) return Results.NotFound();
            var role = await db.Roles.FindAsync(input.RoleId);
            var errors = new Errors()
                .Check(role is not null, "roleId", "Rol bulunamadı.")
                .Check(!(id == me.UserId() && !input.IsActive), "isActive", "Kendi hesabınızı pasif yapamazsınız.");
            if (errors.Any) return errors.ToResult();

            user.FullName = input.FullName.Trim();
            user.RoleId = role!.Id;
            user.IsActive = input.IsActive;
            if (!string.IsNullOrEmpty(input.Password)) user.PasswordHash = hasher.HashPassword(user, input.Password);
            await db.SaveChangesAsync();
            return Results.Ok(new UserDto(user.Id, user.Email, user.FullName, role.Id, role.Name, user.IsActive, user.CreatedAt));
        });

        group.MapDelete("/users/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal me) =>
        {
            if (id == me.UserId()) return ApiResults.Conflict("Kendi hesabınızı silemezsiniz.");
            return await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound();
        });
    }

    private static IResult? Validate(RoleInput input)
    {
        var unknown = input.Permissions.Where(p => !Permissions.All.ContainsKey(p)).ToList();
        var errors = new Errors().Check(unknown.Count == 0, "permissions", $"Geçersiz yetki: {string.Join(", ", unknown)}");
        return errors.Any ? errors.ToResult() : null;
    }
}
