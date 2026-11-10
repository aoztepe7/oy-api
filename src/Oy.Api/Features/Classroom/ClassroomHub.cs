using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;

namespace Oy.Api.Features.Classroom;

/// <summary>
/// Live classroom: clients watch a lesson (or a day's lesson list) and get a <c>sessionChanged</c> push whenever
/// attendance, notes or the lesson state change. The push carries only ids; clients reload through the REST API,
/// so every field stays subject to the caller's own permissions.
/// </summary>
[Authorize(Policy = Permissions.Classroom)]
public class ClassroomHub(AppDbContext db) : Hub
{
    public const string Path = "/hubs/classroom";

    public static string SessionGroup(Guid sessionId) => $"session:{sessionId}";
    public static string DayGroup(DateOnly date) => $"day:{date:yyyy-MM-dd}";

    /// <summary>Joins a lesson's live updates; only for lessons the user may open.</summary>
    public async Task WatchSession(Guid sessionId)
    {
        var viewer = await ClassroomEndpoints.ViewerAsync(Context.User!, db);
        if (!await ClassroomEndpoints.Visible(db, viewer).AnyAsync(s => s.Id == sessionId))
            throw new HubException("Ders bulunamadı.");
        await Groups.AddToGroupAsync(Context.ConnectionId, SessionGroup(sessionId));
    }

    public Task UnwatchSession(Guid sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, SessionGroup(sessionId));

    /// <summary>
    /// Joins the "something changed on this day" signal for the lesson list. It reveals no data: the list is
    /// reloaded through the API, which filters by the user's assignments.
    /// </summary>
    public Task WatchDay(DateOnly date) => Groups.AddToGroupAsync(Context.ConnectionId, DayGroup(date));

    public Task UnwatchDay(DateOnly date) => Groups.RemoveFromGroupAsync(Context.ConnectionId, DayGroup(date));
}

/// <summary>Payload of <c>sessionChanged</c>.</summary>
public record SessionChanged(Guid SessionId, DateOnly Date);

public class ClassroomNotifier(IHubContext<ClassroomHub> hub, ILogger<ClassroomNotifier> log)
{
    public async Task SessionChangedAsync(Guid sessionId, DateOnly date)
    {
        try
        {
            await hub.Clients.Groups(ClassroomHub.SessionGroup(sessionId), ClassroomHub.DayGroup(date))
                .SendAsync("sessionChanged", new SessionChanged(sessionId, date));
        }
        catch (Exception ex)
        {
            // A failed push must not fail the change itself; clients also reload when they reconnect.
            log.LogWarning(ex, "Classroom push failed for session {SessionId}", sessionId);
        }
    }
}
