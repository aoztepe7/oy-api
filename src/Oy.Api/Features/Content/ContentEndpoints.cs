using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;

namespace Oy.Api.Features.Content;

public record ThemeInput(ThemeKind Kind, [Required, MaxLength(200)] string Title, string? Description, string? ImageUrl, DateOnly StartDate, DateOnly EndDate);

public record ThemeDto(Guid Id, ThemeKind Kind, string Title, string Description, string ImageUrl, DateOnly StartDate, DateOnly EndDate);

public record MessageDto(Guid Id, string Name, string Email, string Phone, string Message, bool IsRead, DateTime CreatedAt);

public record MessagePatch(bool IsRead);

public static class ContentEndpoints
{
    public static void MapContent(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Content");

        // Themes
        admin.MapGet("/themes", async (AppDbContext db, MediaService media) =>
            (await db.Themes.AsNoTracking().OrderByDescending(t => t.StartDate).ToListAsync()).Select(t => ToDto(t, media)))
            .RequireAuthorization(Permissions.ProgramsRead);

        admin.MapPost("/themes", async (ThemeInput input, AppDbContext db, MediaService media, UiRevalidator ui) =>
        {
            if (input.EndDate < input.StartDate) return new Errors().Check(false, "endDate", "Bitiş tarihi başlangıçtan önce olamaz.").ToResult();
            var theme = new Theme { Title = "" };
            Apply(theme, input, media);
            db.Themes.Add(theme);
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Themes);
            return Results.Created($"/api/admin/themes/{theme.Id}", ToDto(theme, media));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        admin.MapPut("/themes/{id:guid}", async (Guid id, ThemeInput input, AppDbContext db, MediaService media, UiRevalidator ui) =>
        {
            if (input.EndDate < input.StartDate) return new Errors().Check(false, "endDate", "Bitiş tarihi başlangıçtan önce olamaz.").ToResult();
            var theme = await db.Themes.FindAsync(id);
            if (theme is null) return Results.NotFound();
            Apply(theme, input, media);
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Themes);
            return Results.Ok(ToDto(theme, media));
        }).RequireAuthorization(Permissions.ProgramsWrite);

        admin.MapDelete("/themes/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            if (await db.Themes.Where(t => t.Id == id).ExecuteDeleteAsync() == 0) return Results.NotFound();
            await ui.RevalidateAsync(CacheTags.Themes);
            return Results.NoContent();
        }).RequireAuthorization(Permissions.ProgramsWrite);

        // Contact messages
        admin.MapGet("/messages", async (AppDbContext db) =>
            await db.ContactMessages.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(500)
                .Select(m => new MessageDto(m.Id, m.Name, m.Email, m.Phone, m.Message, m.IsRead, m.CreatedAt)).ToListAsync())
            .RequireAuthorization(Permissions.EnrollmentsRead);

        admin.MapPatch("/messages/{id:guid}", async (Guid id, MessagePatch patch, AppDbContext db) =>
            await db.ContactMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.IsRead, patch.IsRead)) > 0
                ? Results.NoContent()
                : Results.NotFound()).RequireAuthorization(Permissions.EnrollmentsRead);

        // Media upload
        admin.MapPost("/media", async (IFormFile file, MediaService media, CancellationToken ct) =>
        {
            var stored = await media.SaveAsync(file, ct);
            return stored is null
                ? new Errors().Check(false, "file", "Sadece JPG, PNG veya WEBP (en fazla 5 MB) yüklenebilir.").ToResult()
                : Results.Ok(new { url = media.ToPublic(stored) });
        }).RequireAuthorization(Permissions.MediaUpload).DisableAntiforgery();
    }

    private static void Apply(Theme t, ThemeInput i, MediaService media)
    {
        t.Kind = i.Kind;
        t.Title = i.Title.Trim();
        t.Description = i.Description?.Trim() ?? "";
        t.ImageUrl = media.ToStored(i.ImageUrl);
        t.StartDate = i.StartDate;
        t.EndDate = i.EndDate;
    }

    public static ThemeDto ToDto(Theme t, MediaService media) =>
        new(t.Id, t.Kind, t.Title, t.Description, media.ToPublic(t.ImageUrl), t.StartDate, t.EndDate);
}
