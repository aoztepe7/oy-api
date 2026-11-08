using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;

namespace Oy.Api.Features.Programs;

public record SpecialDayInput([Required, MaxLength(200)] string Name, DateOnly Date, SpecialDayKind Kind);

public record SpecialDayDto(Guid Id, string Name, DateOnly Date, SpecialDayKind Kind);

/// <summary>Holidays and observances shown while planning lessons (seeded, editable).</summary>
public static class SpecialDayEndpoints
{
    public static void MapSpecialDays(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/special-days").WithTags("Special days");

        group.MapGet("/", async (DateOnly from, DateOnly to, AppDbContext db) =>
        {
            if (to < from || to.DayNumber - from.DayNumber > 3 * 366)
                return new Errors().Check(false, "to", "Tarih aralığı en fazla 3 yıl olabilir.").ToResult();
            return Results.Ok(await db.SpecialDays.AsNoTracking()
                .Where(d => d.Date >= from && d.Date <= to).OrderBy(d => d.Date).ThenBy(d => d.Name)
                .Select(d => new SpecialDayDto(d.Id, d.Name, d.Date, d.Kind)).ToListAsync());
        }).RequireAuthorization(Permissions.ProgramsRead);

        group.MapPost("/", async (SpecialDayInput input, AppDbContext db) =>
        {
            var day = new SpecialDay { Name = "" };
            if (await ApplyAsync(day, input, db) is { } error) return error;
            db.SpecialDays.Add(day);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/special-days/{day.Id}", ToDto(day));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        group.MapPut("/{id:guid}", async (Guid id, SpecialDayInput input, AppDbContext db) =>
        {
            var day = await db.SpecialDays.FindAsync(id);
            if (day is null) return Results.NotFound();
            if (await ApplyAsync(day, input, db) is { } error) return error;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(day));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        group.MapDelete("/{id:guid}", async (Guid id, AppDbContext db) =>
            await db.SpecialDays.Where(d => d.Id == id).ExecuteDeleteAsync() == 0 ? Results.NotFound() : Results.NoContent())
            .RequireAuthorization(Permissions.ProgramsWrite);
    }

    private static async Task<IResult?> ApplyAsync(SpecialDay day, SpecialDayInput input, AppDbContext db)
    {
        var name = input.Name.Trim();
        if (await db.SpecialDays.AnyAsync(d => d.Date == input.Date && d.Name == name && d.Id != day.Id))
            return new Errors().Check(false, "name", "Bu tarihte aynı adla bir özel gün zaten var.").ToResult();
        day.Name = name;
        day.Date = input.Date;
        day.Kind = input.Kind;
        return null;
    }

    private static SpecialDayDto ToDto(SpecialDay d) => new(d.Id, d.Name, d.Date, d.Kind);
}
