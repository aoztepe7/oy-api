using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Features.Finance;

public record GeneralExpenseInput(DateOnly? Date, [Required, MaxLength(100)] string Category,
    [MaxLength(500)] string? Description, [Range(0.01, 10_000_000)] decimal Amount);

public record GeneralExpenseDto(Guid Id, DateOnly Date, string Category, string Description, decimal Amount,
    string AuthorName, DateTime CreatedAt,
    /// <summary>The current user entered it, or may write finance.</summary>
    bool CanDelete);

/// <summary>
/// Quick overhead-expense entry ("genel gider"): not tied to any program or event. Every panel user enters one and sees
/// their own entries; users who read finance see everyone's. Only the author — or someone who writes finance — deletes one.
/// </summary>
public static class GeneralExpenseEndpoints
{
    public const int ListMax = 200;

    public static void MapGeneralExpenses(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/general-expenses").WithTags("General expenses").RequireAuthorization(Permissions.Staff);

        group.MapGet("/", async (ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.GeneralExpenses.AsNoTracking();
            if (!me.Can(Permissions.FinanceRead)) { var uid = me.UserId(); q = q.Where(x => x.AuthorUserId == uid); }
            var canWrite = me.Can(Permissions.FinanceWrite);
            var myId = me.UserId();
            return await q.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Take(ListMax)
                .Select(x => new GeneralExpenseDto(x.Id, x.Date, x.Category, x.Description, x.Amount, x.AuthorName, x.CreatedAt,
                    canWrite || x.AuthorUserId == myId))
                .ToListAsync(ct);
        });

        group.MapPost("/", async (GeneralExpenseInput input, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var e = new GeneralExpense
            {
                Date = input.Date ?? DateOnly.FromDateTime(DateTime.Now), Category = input.Category.Trim(),
                Description = input.Description?.Trim() ?? "", Amount = input.Amount,
                AuthorUserId = me.UserId(), AuthorName = me.FindFirstValue("name") ?? "",
            };
            if (e.Category.Length == 0) return new Errors().Check(false, "category", "Kategori girin.").ToResult();
            db.GeneralExpenses.Add(e);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/admin/general-expenses/{e.Id}",
                new GeneralExpenseDto(e.Id, e.Date, e.Category, e.Description, e.Amount, e.AuthorName, e.CreatedAt, true));
        });

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal me, AppDbContext db, CancellationToken ct) =>
        {
            var e = await db.GeneralExpenses.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return Results.NotFound();
            if (e.AuthorUserId != me.UserId() && !me.Can(Permissions.FinanceWrite)) return Results.Forbid();
            db.GeneralExpenses.Remove(e);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
