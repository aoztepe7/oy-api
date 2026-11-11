using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;
using Oy.Api.Infrastructure.Push;

namespace Oy.Api.Features.Todos;

public record TodoPerson(Guid Id, string FullName);

public record TodoDto(
    Guid Id, string Title, string Notes, TodoVisibility Visibility, TodoPriority Priority, DateOnly? DueDate,
    TodoPerson? CreatedBy, DateTime CreatedAt, DateTime? CompletedAt, TodoPerson? CompletedBy, List<TodoPerson> Assignees,
    /// <summary>The current user created it (edit / delete).</summary>
    bool IsMine,
    /// <summary>Assigned to the current user.</summary>
    bool AssignedToMe,
    TodoRepeat Repeat, int CommentCount, DateTime? LastCommentAt);

public record TodoCommentDto(Guid Id, string Text, TodoPerson? Author, DateTime CreatedAt, bool IsMine);

public record TodoCommentInput([Required, MaxLength(2000)] string Text);

public record TodoInput(
    [Required, MaxLength(200)] string Title,
    [MaxLength(4000)] string? Notes,
    TodoVisibility Visibility,
    TodoPriority Priority,
    DateOnly? DueDate,
    List<Guid>? AssigneeIds,
    TodoRepeat Repeat = TodoRepeat.None);

public record TodoCompleteInput(bool Done);

/// <summary>To-dos of panel users: private, shared with everyone, or assigned to people.</summary>
public static class TodoEndpoints
{
    /// <summary>Completed to-dos stay in the list for this many days.</summary>
    private const int DoneDays = 30;

    public static void MapTodos(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/todos").WithTags("Todos").RequireAuthorization(Permissions.Staff);

        // Open to-dos the user can see, plus the ones completed in the last 30 days (or `doneDays`).
        group.MapGet("/", async (int? doneDays, AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(doneDays ?? DoneDays, 0, 365));
            var todos = await Project(Visible(db, userId).Where(t => t.CompletedAt == null || t.CompletedAt >= since), userId)
                .ToListAsync(ct);
            return todos.OrderBy(t => t.CompletedAt != null).ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(t => t.Priority).ThenByDescending(t => t.CreatedAt);
        });

        // Sidebar badge: open to-dos assigned to me, and my own ones due today or earlier.
        group.MapGet("/count", async (AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var count = await Visible(db, userId).CountAsync(t => t.CompletedAt == null &&
                (t.Assignees.Any(a => a.UserId == userId) || (t.CreatedById == userId && t.DueDate != null && t.DueDate <= today)), ct);
            return new { count };
        });

        // People a to-do can be assigned to: active panel users.
        group.MapGet("/people", async (AppDbContext db, CancellationToken ct) =>
            await db.Users.AsNoTracking().Where(u => u.Kind == UserKind.Staff && u.IsActive).OrderBy(u => u.FullName)
                .Select(u => new TodoPerson(u.Id, u.FullName)).ToListAsync(ct));

        group.MapPost("/", async (TodoInput input, AppDbContext db, ClaimsPrincipal me, Notifier notifier, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var t = new TodoItem { Title = "", CreatedById = userId };
            var error = await ApplyAsync(t, input, db, ct);
            if (error is not null) return error;
            db.Todos.Add(t);
            await db.SaveChangesAsync(ct);
            await NotifyAssignedAsync(t, t.Assignees.Select(a => a.UserId), me, notifier, ct);
            return Results.Created($"/api/admin/todos/{t.Id}", await Project(db.Todos.Where(x => x.Id == t.Id), userId).FirstAsync(ct));
        });

        group.MapPut("/{id:guid}", async (Guid id, TodoInput input, AppDbContext db, ClaimsPrincipal me, Notifier notifier, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var t = await db.Todos.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || !CanSee(t, userId)) return Results.NotFound();
            if (t.CreatedById != userId) return Forbidden("Yalnızca işi oluşturan kişi düzenleyebilir.");
            var before = t.Assignees.Select(a => a.UserId).ToHashSet();
            var error = await ApplyAsync(t, input, db, ct);
            if (error is not null) return error;
            await db.SaveChangesAsync(ct);
            await NotifyAssignedAsync(t, t.Assignees.Select(a => a.UserId).Where(u => !before.Contains(u)), me, notifier, ct);
            return Results.Ok(await Project(db.Todos.Where(x => x.Id == t.Id), userId).FirstAsync(ct));
        });

        group.MapPost("/{id:guid}/complete", async (Guid id, TodoCompleteInput input, AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var t = await db.Todos.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || !CanSee(t, userId)) return Results.NotFound();
            if (input.Done && t.CompletedAt is null)
            {
                t.CompletedAt = DateTime.UtcNow;
                t.CompletedById = userId;
                // A repeating to-do comes back on its next date.
                if (t.Repeat != TodoRepeat.None && t.DueDate is { } due && t.NextId is null)
                {
                    var next = new TodoItem
                    {
                        Title = t.Title, Notes = t.Notes, Visibility = t.Visibility, Priority = t.Priority,
                        Repeat = t.Repeat, RepeatDay = t.RepeatDay, CreatedById = t.CreatedById,
                        DueDate = TodoItem.NextDue(t.Repeat, due, t.RepeatDay, DateOnly.FromDateTime(DateTime.UtcNow)),
                    };
                    next.Assignees = t.Assignees.Select(a => new TodoAssignee { TodoId = next.Id, UserId = a.UserId }).ToList();
                    db.Todos.Add(next);
                    t.NextId = next.Id;
                }
            }
            else if (!input.Done && t.CompletedAt is not null)
            {
                t.CompletedAt = null;
                t.CompletedById = null;
                // Reopened: the follow-up created on completion goes away unless someone already worked on it.
                if (t.NextId is { } nextId)
                {
                    var next = await db.Todos.FirstOrDefaultAsync(x => x.Id == nextId, ct);
                    if (next is not null && next.CompletedAt is null && !await db.TodoComments.AnyAsync(c => c.TodoId == nextId, ct))
                        db.Todos.Remove(next);
                    t.NextId = null;
                }
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(await Project(db.Todos.Where(x => x.Id == t.Id), userId).FirstAsync(ct));
        });

        // ----- Comments -----
        group.MapGet("/{id:guid}/comments", async (Guid id, AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            if (!await Visible(db, userId).AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            return Results.Ok(await db.TodoComments.AsNoTracking().Where(c => c.TodoId == id).OrderBy(c => c.CreatedAt)
                .Select(c => new TodoCommentDto(c.Id, c.Text, c.Author == null ? null : new TodoPerson(c.Author.Id, c.Author.FullName),
                    c.CreatedAt, c.AuthorId == userId))
                .ToListAsync(ct));
        });

        group.MapPost("/{id:guid}/comments", async (Guid id, TodoCommentInput input, AppDbContext db, ClaimsPrincipal me, Notifier notifier, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            if (!await Visible(db, userId).AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var text = input.Text.Trim();
            if (text.Length == 0) return new Errors().Check(false, "text", "Yorumu yazın.").ToResult();
            var c = new TodoComment { TodoId = id, AuthorId = userId, Text = text };
            db.TodoComments.Add(c);
            await db.SaveChangesAsync(ct);
            var author = await db.Users.Where(u => u.Id == userId).Select(u => new TodoPerson(u.Id, u.FullName)).FirstAsync(ct);

            // The people the to-do belongs to (owner, assignees) and earlier commenters, except the author.
            var todo = await db.Todos.AsNoTracking().Where(t => t.Id == id)
                .Select(t => new { t.Title, t.CreatedById, Assignees = t.Assignees.Select(a => a.UserId).ToList() }).FirstAsync(ct);
            var commenters = await db.TodoComments.Where(x => x.TodoId == id && x.AuthorId != null).Select(x => x.AuthorId!.Value).Distinct().ToListAsync(ct);
            var people = todo.Assignees.Concat(commenters).Append(todo.CreatedById ?? Guid.Empty).Where(u => u != Guid.Empty && u != userId);
            await notifier.SendAsync(people, NotificationKind.TodoComment, $"{author.FullName} yorum yazdı",
                $"{todo.Title}: {text.ReplaceLineEndings(" ")}", $"/yapilacaklar?is={id}", ct: ct);
            return Results.Created($"/api/admin/todos/{id}/comments/{c.Id}", new TodoCommentDto(c.Id, c.Text, author, c.CreatedAt, true));
        });

        group.MapDelete("/{id:guid}/comments/{commentId:guid}", async (Guid id, Guid commentId, AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var c = await db.TodoComments.FirstOrDefaultAsync(x => x.Id == commentId && x.TodoId == id, ct);
            if (c is null) return Results.NotFound();
            if (c.AuthorId != userId) return Forbidden("Yalnızca yorumu yazan kişi silebilir.");
            db.TodoComments.Remove(c);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ClaimsPrincipal me, CancellationToken ct) =>
        {
            var userId = me.UserId()!.Value;
            var t = await db.Todos.Include(x => x.Assignees).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null || !CanSee(t, userId)) return Results.NotFound();
            if (t.CreatedById != userId) return Forbidden("Yalnızca işi oluşturan kişi silebilir.");
            db.Todos.Remove(t);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static IResult Forbidden(string message) => Results.Problem(title: message, statusCode: StatusCodes.Status403Forbidden);

    /// <summary>To-dos the user may see: own ones, shared with everyone, or assigned to them.</summary>
    private static IQueryable<TodoItem> Visible(AppDbContext db, Guid userId) =>
        db.Todos.AsNoTracking().Where(t =>
            t.CreatedById == userId
            || t.Visibility == TodoVisibility.Everyone
            || (t.Visibility == TodoVisibility.Assigned && t.Assignees.Any(a => a.UserId == userId)));

    private static bool CanSee(TodoItem t, Guid userId) =>
        t.CreatedById == userId
        || t.Visibility == TodoVisibility.Everyone
        || (t.Visibility == TodoVisibility.Assigned && t.Assignees.Any(a => a.UserId == userId));

    private static async Task NotifyAssignedAsync(TodoItem t, IEnumerable<Guid> users, ClaimsPrincipal me, Notifier notifier, CancellationToken ct)
    {
        var userId = me.UserId();
        var name = me.FindFirstValue("name") ?? "";
        var due = t.DueDate is { } d ? $" · son gün {ReminderWorker.Date(d)}" : "";
        await notifier.SendAsync(users.Where(u => u != userId), NotificationKind.TodoAssigned,
            name.Length > 0 ? $"{name} size bir iş atadı" : "Size bir iş atandı", t.Title + due, $"/yapilacaklar?is={t.Id}", ct: ct);
    }

    /// <summary>Copies the input onto the to-do; returns a 400 result when invalid.</summary>
    private static async Task<IResult?> ApplyAsync(TodoItem t, TodoInput input, AppDbContext db, CancellationToken ct)
    {
        var ids = input.Visibility == TodoVisibility.Assigned ? (input.AssigneeIds ?? []).Distinct().Take(20).ToList() : [];
        var valid = ids.Count == 0
            ? []
            : await db.Users.Where(u => ids.Contains(u.Id) && u.Kind == UserKind.Staff && u.IsActive).Select(u => u.Id).ToListAsync(ct);
        var errors = new Errors()
            .Check(input.Title.Trim().Length > 0, "title", "İşi yazın.")
            .Check(input.Visibility != TodoVisibility.Assigned || valid.Count > 0, "assigneeIds", "En az bir kişi seçin.")
            .Check(valid.Count == ids.Count, "assigneeIds", "Seçilen kişilerden biri bulunamadı ya da pasif.")
            .Check(input.Repeat == TodoRepeat.None || input.DueDate is not null, "dueDate", "Tekrarlayan iş için ilk tarihi seçin.");
        if (errors.Any) return errors.ToResult();

        t.Title = input.Title.Trim();
        t.Notes = input.Notes?.Trim() ?? "";
        t.Visibility = input.Visibility;
        t.Priority = input.Priority;
        t.DueDate = input.DueDate;
        t.Repeat = Enum.IsDefined(input.Repeat) ? input.Repeat : TodoRepeat.None;
        t.RepeatDay = t.Repeat == TodoRepeat.Monthly && input.DueDate is { } d ? d.Day : 0;
        t.Assignees.RemoveAll(a => !valid.Contains(a.UserId));
        foreach (var id in valid.Where(id => t.Assignees.All(a => a.UserId != id)))
            t.Assignees.Add(new TodoAssignee { TodoId = t.Id, UserId = id });
        return null;
    }

    private static IQueryable<TodoDto> Project(IQueryable<TodoItem> q, Guid userId) => q.Select(t => new TodoDto(
        t.Id, t.Title, t.Notes, t.Visibility, t.Priority, t.DueDate,
        t.CreatedBy == null ? null : new TodoPerson(t.CreatedBy.Id, t.CreatedBy.FullName), t.CreatedAt, t.CompletedAt,
        t.CompletedBy == null ? null : new TodoPerson(t.CompletedBy.Id, t.CompletedBy.FullName),
        t.Assignees.OrderBy(a => a.User.FullName).Select(a => new TodoPerson(a.UserId, a.User.FullName)).ToList(),
        t.CreatedById == userId, t.Assignees.Any(a => a.UserId == userId),
        t.Repeat, t.Comments.Count, t.Comments.Max(c => (DateTime?)c.CreatedAt)));
}
