using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Notifications;

public record NotificationDto(Guid Id, NotificationKind Kind, string Title, string Body, string Url, DateTime CreatedAt, bool Read);

public record NotificationPage(List<NotificationDto> Items, int Unread, bool HasMore);

public record NotificationSetting(NotificationKind Kind, string Label, bool Enabled);

public record NotificationSettingInput(NotificationKind Kind, bool Enabled);

/// <summary>The signed-in user's notification inbox (staff and guardians alike) and which kinds they receive.</summary>
public static class NotificationEndpoints
{
    private const int PageSize = 30;

    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization();

        // Newest first; `before` = CreatedAt of the last item already shown.
        group.MapGet("/", async (DateTime? before, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var query = db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
            if (before is { } b) query = query.Where(n => n.CreatedAt < b.ToUniversalTime());
            var items = await query.OrderByDescending(n => n.CreatedAt).Take(PageSize + 1)
                .Select(n => new NotificationDto(n.Id, n.Kind, n.Title, n.Body, n.Url, n.CreatedAt, n.ReadAt != null))
                .ToListAsync(ct);
            var unread = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct);
            return new NotificationPage(items.Take(PageSize).ToList(), unread, items.Count > PageSize);
        });

        group.MapGet("/unread", async (ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            return new { count = await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct) };
        });

        group.MapPost("/{id:guid}/read", async (Guid id, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var now = DateTime.UtcNow;
            await db.Notifications.Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });

        group.MapPost("/read-all", async (ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var now = DateTime.UtcNow;
            await db.Notifications.Where(n => n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return Results.NoContent();
        });

        group.MapGet("/settings", async (ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var muted = await db.NotificationMutes.Where(m => m.UserId == userId).Select(m => m.Kind).ToListAsync(ct);
            return KindsFor(me).Select(k => new NotificationSetting(k, NotificationKinds.Label(k), !muted.Contains(k))).ToList();
        });

        group.MapPut("/settings", async (NotificationSettingInput input, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            if (!KindsFor(me).Contains(input.Kind)) return Results.BadRequest();
            var existing = await db.NotificationMutes.FirstOrDefaultAsync(m => m.UserId == userId && m.Kind == input.Kind, ct);
            if (input.Enabled && existing is not null) db.NotificationMutes.Remove(existing);
            else if (!input.Enabled && existing is null) db.NotificationMutes.Add(new NotificationMute { UserId = userId, Kind = input.Kind });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Kinds this user can receive at all (guardian kinds, or the staff kinds their permissions allow).</summary>
    private static IEnumerable<NotificationKind> KindsFor(ClaimsPrincipal me)
    {
        if (me.FindFirstValue(AuthSetup.KindClaim) == nameof(UserKind.Guardian)) return NotificationKinds.Guardian;
        return NotificationKinds.Staff.Where(k => k switch
        {
            NotificationKind.PreRegistration or NotificationKind.RegistrationSubmitted or NotificationKind.ContactMessage =>
                me.Can(Permissions.EnrollmentsRead),
            NotificationKind.PaymentDigest => me.Can(Permissions.FinanceRead),
            _ => true,
        });
    }
}
