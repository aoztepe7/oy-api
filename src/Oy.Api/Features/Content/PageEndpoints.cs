using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Infrastructure;

namespace Oy.Api.Features.Content;

public record PageListItem(
    Guid Id, string Slug, string Path, string Title, bool IsSystem, bool IsPublished,
    DateTime UpdatedAt, DateTime? PublishedAt, bool HasUnpublishedChanges);

public record PageAdminDto(
    Guid Id, string Slug, string Path, string Title, string SeoTitle, string SeoDescription, bool IsSystem,
    List<PageBlock> Blocks, bool IsPublished, DateTime? PublishedAt, bool HasUnpublishedChanges);

public record PageCreateInput([Required, MaxLength(200)] string Title, [MaxLength(100)] string? Slug, string? Template);

public record PageSaveInput(
    [Required, MaxLength(200)] string Title,
    [MaxLength(100)] string? Slug,
    [MaxLength(200)] string? SeoTitle,
    [MaxLength(500)] string? SeoDescription,
    List<PageBlock>? Blocks);

public record SiteSettingsDto(
    [MaxLength(200)] string SiteName,
    [MaxLength(300)] string Tagline,
    [MaxLength(2000)] string FooterText,
    [MaxLength(50)] string Phone,
    [MaxLength(256)] string Email,
    [MaxLength(500)] string Address,
    [MaxLength(500)] string InstagramUrl,
    [MaxLength(500)] string YoutubeUrl,
    [MaxLength(100)] string HeaderCtaText,
    [MaxLength(500)] string HeaderCtaHref,
    List<NavItem> HeaderNav,
    List<NavItem> FooterNav,
    System.Text.Json.Nodes.JsonObject? HeaderStyle = null,
    System.Text.Json.Nodes.JsonObject? FooterStyle = null);

/// <summary>Block-based CMS pages, the block catalog and site settings (menus, contact details).</summary>
public static class PageEndpoints
{
    /// <summary>Slugs that would clash with site routes or files.</summary>
    private static readonly HashSet<string> ReservedSlugs =
        ["home", "onizleme", "api", "media", "admin", "panel", "sitemap-xml", "robots-txt", "icon-svg", "favicon-ico", "_next"];

    public static string PathOf(string slug) => slug == Page.HomeSlug ? "/" : $"/{slug}";

    public static void MapPages(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Pages").RequireAuthorization(Permissions.ContentWrite);

        admin.MapGet("/blocks", () => new
        {
            blocks = BlockCatalog.All, common = BlockCatalog.CommonFields, style = StyleCatalog.All, layoutStyle = StyleCatalog.Layout,
        });

        admin.MapGet("/pages", async (AppDbContext db) =>
            (await db.Pages.AsNoTracking().ToListAsync())
            .OrderByDescending(p => p.Slug == Page.HomeSlug).ThenByDescending(p => p.IsSystem).ThenBy(p => p.Title)
            .Select(p => new PageListItem(p.Id, p.Slug, PathOf(p.Slug), p.Title, p.IsSystem, p.Published != null,
                p.DraftUpdatedAt, p.PublishedAt, p.HasUnpublishedChanges)));

        admin.MapGet("/pages/{id:guid}", async (Guid id, AppDbContext db, MediaService media) =>
            await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id) is { } page
                ? Results.Ok(ToAdminDto(page, media))
                : Results.NotFound());

        admin.MapPost("/pages", async (PageCreateInput input, AppDbContext db, MediaService media) =>
        {
            var slug = Slug.From(string.IsNullOrWhiteSpace(input.Slug) ? input.Title : input.Slug);
            if (await SlugError(slug, null, db) is { } error) return error;
            var title = input.Title.Trim();
            var page = new Page
            {
                Slug = slug, Title = title, SeoTitle = title,
                Draft = input.Template == "blank" ? [] : PageTemplates.Content(title),
                DraftUpdatedAt = DateTime.UtcNow,
            };
            db.Pages.Add(page);
            await db.SaveChangesAsync();
            return Results.Created($"/api/admin/pages/{page.Id}", ToAdminDto(page, media));
        });

        // Saves the draft. Slug changes of a published page are live at once (old URL stops working).
        admin.MapPut("/pages/{id:guid}", async (Guid id, PageSaveInput input, AppDbContext db, MediaService media, UiRevalidator ui) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (page is null) return Results.NotFound();

            var slug = page.IsSystem || string.IsNullOrWhiteSpace(input.Slug) ? page.Slug : Slug.From(input.Slug);
            if (slug != page.Slug && await SlugError(slug, page.Id, db) is { } slugError) return slugError;

            var errors = new Errors();
            var blocks = BlockContent.Sanitize(input.Blocks, media.ToStored, errors);
            if (errors.Any) return errors.ToResult();

            var oldSlug = page.Slug;
            page.Slug = slug;
            page.Title = input.Title.Trim();
            page.SeoTitle = input.SeoTitle?.Trim() ?? "";
            page.SeoDescription = input.SeoDescription?.Trim() ?? "";
            page.Draft = blocks;
            page.DraftUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            if (oldSlug != slug && page.Published is not null)
                await ui.RevalidateAsync(CacheTags.Page(oldSlug), CacheTags.Page(slug), CacheTags.Pages, CacheTags.Site);
            return Results.Ok(ToAdminDto(page, media));
        });

        admin.MapPost("/pages/{id:guid}/publish", async (Guid id, AppDbContext db, MediaService media, UiRevalidator ui) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (page is null) return Results.NotFound();
            page.Published = [.. page.Draft];
            page.PublishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Page(page.Slug), CacheTags.Pages);
            return Results.Ok(ToAdminDto(page, media));
        });

        admin.MapPost("/pages/{id:guid}/unpublish", async (Guid id, AppDbContext db, MediaService media, UiRevalidator ui) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (page is null) return Results.NotFound();
            if (page.Slug == Page.HomeSlug) return ApiResults.Conflict("Ana sayfa yayından kaldırılamaz.");
            page.Published = null;
            page.PublishedAt = null;
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Page(page.Slug), CacheTags.Pages);
            return Results.Ok(ToAdminDto(page, media));
        });

        // Throws away unpublished edits.
        admin.MapPost("/pages/{id:guid}/discard", async (Guid id, AppDbContext db, MediaService media) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (page is null) return Results.NotFound();
            if (page.Published is null || page.PublishedAt is null) return ApiResults.Conflict("Sayfa hiç yayınlanmadı; geri dönülecek sürüm yok.");
            page.Draft = [.. page.Published];
            page.DraftUpdatedAt = page.PublishedAt.Value;
            await db.SaveChangesAsync();
            return Results.Ok(ToAdminDto(page, media));
        });

        admin.MapDelete("/pages/{id:guid}", async (Guid id, AppDbContext db, UiRevalidator ui) =>
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == id);
            if (page is null) return Results.NotFound();
            if (page.IsSystem) return ApiResults.Conflict("Sistem sayfaları silinemez; yayından kaldırabilirsiniz.");
            db.Pages.Remove(page);
            await db.SaveChangesAsync();
            if (page.Published is not null) await ui.RevalidateAsync(CacheTags.Page(page.Slug), CacheTags.Pages);
            return Results.NoContent();
        });

        // Site settings (single row)
        admin.MapGet("/site-settings", async (AppDbContext db) => await SiteAsync(db));

        admin.MapPut("/site-settings", async (SiteSettingsDto input, AppDbContext db, UiRevalidator ui) =>
        {
            var errors = new Errors()
                .Check((input.HeaderNav?.Count ?? 0) <= 12, "headerNav", "Üst menüde en fazla 12 bağlantı olabilir.")
                .Check((input.FooterNav?.Count ?? 0) <= 20, "footerNav", "Alt menüde en fazla 20 bağlantı olabilir.");
            if (errors.Any) return errors.ToResult();

            var s = await SettingsAsync(db);
            s.SiteName = input.SiteName?.Trim() ?? "";
            s.Tagline = input.Tagline?.Trim() ?? "";
            s.FooterText = input.FooterText?.Trim() ?? "";
            s.Phone = input.Phone?.Trim() ?? "";
            s.Email = input.Email?.Trim() ?? "";
            s.Address = input.Address?.Trim() ?? "";
            s.InstagramUrl = input.InstagramUrl?.Trim() ?? "";
            s.YoutubeUrl = input.YoutubeUrl?.Trim() ?? "";
            s.HeaderCtaText = input.HeaderCtaText?.Trim() ?? "";
            s.HeaderCtaHref = input.HeaderCtaHref?.Trim() ?? "";
            s.HeaderNav = CleanNav(input.HeaderNav);
            s.FooterNav = CleanNav(input.FooterNav);
            s.HeaderStyle = BlockContent.CleanStyle(input.HeaderStyle, null, StyleCatalog.Layout);
            s.FooterStyle = BlockContent.CleanStyle(input.FooterStyle, null, StyleCatalog.Layout);
            await db.SaveChangesAsync();
            await ui.RevalidateAsync(CacheTags.Site);
            return Results.Ok(ToDto(s));
        });
    }

    public static void MapPublicPages(this RouteGroupBuilder group)
    {
        // Published pages (sitemap, static params).
        group.MapGet("/pages", async (AppDbContext db) =>
        {
            var pages = await db.Pages.AsNoTracking().Where(p => p.Published != null).OrderBy(p => p.Slug)
                .Select(p => new { p.Slug, p.Title, p.PublishedAt }).ToListAsync();
            return pages.Select(p => new { p.Slug, Path = PathOf(p.Slug), p.Title, UpdatedAt = p.PublishedAt });
        });

        group.MapGet("/pages/{slug}", async (string slug, AppDbContext db, MediaService media) =>
            await db.Pages.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.Published != null) is { } page
                ? Results.Ok(new
                {
                    page.Slug, Path = PathOf(page.Slug), page.Title, page.SeoTitle, page.SeoDescription,
                    Blocks = BlockContent.ToPublic(page.Published!, media.ToPublic, includeHidden: false),
                    page.PublishedAt,
                })
                : Results.NotFound());

        group.MapGet("/site", async (AppDbContext db) => await SiteAsync(db));
    }

    private static async Task<IResult?> SlugError(string slug, Guid? id, AppDbContext db)
    {
        var errors = new Errors()
            .Check(slug.Length > 0, "slug", "Geçerli bir adres girin.")
            .Check(!ReservedSlugs.Contains(slug), "slug", "Bu adres sistem tarafından kullanılıyor.");
        if (!errors.Any && await db.Pages.AnyAsync(p => p.Slug == slug && p.Id != id))
            errors.Check(false, "slug", "Bu adreste başka bir sayfa var.");
        return errors.Any ? errors.ToResult() : null;
    }

    private static PageAdminDto ToAdminDto(Page p, MediaService media) => new(
        p.Id, p.Slug, PathOf(p.Slug), p.Title, p.SeoTitle, p.SeoDescription, p.IsSystem,
        BlockContent.ToPublic(p.Draft, media.ToPublic, includeHidden: true),
        p.Published != null, p.PublishedAt, p.HasUnpublishedChanges);

    public static async Task<SiteSettingsDto> SiteAsync(AppDbContext db) => ToDto(await SettingsAsync(db));

    private static async Task<SiteSettings> SettingsAsync(AppDbContext db)
    {
        var settings = await db.SiteSettings.FirstOrDefaultAsync();
        if (settings is not null) return settings;
        settings = new SiteSettings();
        db.SiteSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    private static List<NavItem> CleanNav(List<NavItem>? items) =>
        (items ?? []).Where(i => !string.IsNullOrWhiteSpace(i.Label) && !string.IsNullOrWhiteSpace(i.Href))
            .Select(i => new NavItem(i.Label.Trim()[..Math.Min(60, i.Label.Trim().Length)], i.Href.Trim()[..Math.Min(500, i.Href.Trim().Length)]))
            .ToList();

    private static SiteSettingsDto ToDto(SiteSettings s) => new(
        s.SiteName, s.Tagline, s.FooterText, s.Phone, s.Email, s.Address, s.InstagramUrl, s.YoutubeUrl,
        s.HeaderCtaText, s.HeaderCtaHref, s.HeaderNav, s.FooterNav,
        BlockContent.CleanStyle(s.HeaderStyle, null, StyleCatalog.Layout), BlockContent.CleanStyle(s.FooterStyle, null, StyleCatalog.Layout));
}
