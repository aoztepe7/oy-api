using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Oy.Api.Domain;

namespace Oy.Api.Infrastructure.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "okulumyeryuzu";
    public string Audience { get; set; } = "okulumyeryuzu-panel";
    public string Key { get; set; } = "";
    public int ExpiryHours { get; set; } = 8;
}

public class TokenService(Microsoft.Extensions.Options.IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expires = DateTime.UtcNow.AddHours(_o.ExpiryHours);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new("name", user.FullName),
            new("role", user.Role.Name),
        };
        claims.AddRange(user.Role.Permissions.Select(p => new Claim(AuthSetup.PermissionClaim, p)));

        var token = new JwtSecurityToken(
            _o.Issuer, _o.Audience, claims, expires: expires,
            signingCredentials: new SigningCredentials(AuthSetup.SigningKey(_o.Key), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

public static class AuthSetup
{
    public const string PermissionClaim = "perm";

    public static SymmetricSecurityKey SigningKey(string key) => new(Encoding.UTF8.GetBytes(key));

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection("Jwt");
        services.Configure<JwtOptions>(section);
        var jwt = section.Get<JwtOptions>() ?? new();
        if (jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");

        services.AddSingleton<TokenService>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = SigningKey(jwt.Key),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                    ClockSkew = TimeSpan.FromMinutes(1),
                };
                // Permissions are read from the DB on each request, so role changes and
                // deactivated users take effect immediately (not when the token expires).
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var db = ctx.HttpContext.RequestServices.GetRequiredService<Data.AppDbContext>();
                        var id = ctx.Principal!.UserId();
                        var user = await db.Users.AsNoTracking()
                            .Where(u => u.Id == id && u.IsActive)
                            .Select(u => new { u.Role.Name, u.Role.Permissions })
                            .FirstOrDefaultAsync();
                        if (user is null)
                        {
                            ctx.Fail("User inactive or deleted.");
                            return;
                        }
                        var claims = ctx.Principal!.Claims.Where(c => c.Type is not (PermissionClaim or "role")).ToList();
                        claims.Add(new Claim("role", user.Name));
                        claims.AddRange(user.Permissions.Select(p => new Claim(PermissionClaim, p)));
                        ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, ctx.Scheme.Name, "name", "role"));
                    },
                };
            });

        services.AddAuthorization(o =>
        {
            foreach (var permission in Permissions.All.Keys)
                o.AddPolicy(permission, p => p.RequireClaim(PermissionClaim, permission));
            o.AddPolicy(Permissions.MediaUpload, p => p.RequireAssertion(ctx => ctx.User.HasClaim(c =>
                c.Type == PermissionClaim &&
                c.Value is Permissions.ProgramsWrite or Permissions.StaffWrite or Permissions.ContentWrite)));
        });
        return services;
    }

    public static Guid? UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
}
