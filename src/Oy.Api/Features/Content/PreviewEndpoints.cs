using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;

namespace Oy.Api.Features.Content;

public record PreviewInput(
    string? Token,
    string? Slug,
    string? Title,
    List<PageBlock>? Blocks,
    SiteSettingsDto? Site);

/// <summary>
/// Unsaved editor state for the visual editor. The panel posts its current draft here (debounced);
/// oy-ui's preview route renders it with the real site components. Kept in memory for 2 hours.
/// </summary>
public static class PreviewEndpoints
{
    private static readonly MemoryCacheEntryOptions Lifetime = new() { SlidingExpiration = TimeSpan.FromHours(2), Size = 1 };

    private record Preview(string Slug, string Title, List<PageBlock> Blocks, SiteSettingsDto? Site);

    public static void MapPreviews(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/admin/previews", (PreviewInput input, IMemoryCache cache, MediaService media) =>
        {
            var errors = new Errors();
            var blocks = BlockContent.Sanitize(input.Blocks, media.ToStored, errors);
            if (errors.Any) return errors.ToResult();

            // Reuse the caller's token while it is valid so the preview iframe URL stays the same.
            var token = input.Token is { Length: 32 } t && cache.TryGetValue(Key(t), out _) ? t : NewToken();
            cache.Set(Key(token), new Preview(input.Slug ?? Page.HomeSlug, input.Title ?? "", blocks, input.Site), Lifetime);
            return Results.Ok(new { token });
        }).RequireAuthorization(Permissions.ContentWrite).WithTags("Pages");

        // Token is the secret (128-bit, short-lived); the site renders it with noindex and no caching.
        app.MapGet("/api/public/previews/{token}", (string token, IMemoryCache cache, MediaService media, AppDbContext db, HttpContext http) =>
        {
            if (!cache.TryGetValue(Key(token), out Preview? preview) || preview is null) return Task.FromResult(Results.NotFound());
            http.Response.Headers.CacheControl = "no-store";
            return Respond(preview, media, db);
        }).AllowAnonymous().WithTags("Public");
    }

    private static async Task<IResult> Respond(Preview p, MediaService media, AppDbContext db)
    {
        var site = p.Site ?? await PageEndpoints.SiteAsync(db);
        return Results.Ok(new
        {
            page = new
            {
                p.Slug, Path = PageEndpoints.PathOf(p.Slug), p.Title,
                Blocks = BlockContent.ToPublic(p.Blocks, media.ToPublic, includeHidden: true),
            },
            site = site with
            {
                HeaderStyle = BlockContent.CleanStyle(site.HeaderStyle, null, StyleCatalog.Layout),
                FooterStyle = BlockContent.CleanStyle(site.FooterStyle, null, StyleCatalog.Layout),
            },
        });
    }

    private static string Key(string token) => $"preview:{token}";

    private static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
