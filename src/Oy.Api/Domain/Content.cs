namespace Oy.Api.Domain;

/// <summary>A CMS page made of blocks. Slug "home" is the front page ("/"); others live at "/{slug}".</summary>
public class Page : Entity
{
    public const string HomeSlug = "home";

    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string SeoTitle { get; set; } = "";
    public string SeoDescription { get; set; } = "";
    /// <summary>Built-in pages can be edited and unpublished but not deleted or re-slugged.</summary>
    public bool IsSystem { get; set; }
    public List<PageBlock> Draft { get; set; } = [];
    /// <summary>Live version; null until first publish or after unpublish.</summary>
    public List<PageBlock>? Published { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime DraftUpdatedAt { get; set; }

    public bool HasUnpublishedChanges => PublishedAt is null || DraftUpdatedAt > PublishedAt;
}

public record NavItem(string Label, string Href);

/// <summary>Site-wide settings (single row): identity, contact details and menus. Saved = live.</summary>
public class SiteSettings : Entity
{
    public string SiteName { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string FooterText { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string InstagramUrl { get; set; } = "";
    public string YoutubeUrl { get; set; } = "";
    public string HeaderCtaText { get; set; } = "";
    public string HeaderCtaHref { get; set; } = "";
    public List<NavItem> HeaderNav { get; set; } = [];
    public List<NavItem> FooterNav { get; set; } = [];
    /// <summary>Style overrides for the global header / footer (<see cref="StyleCatalog.Layout"/> keys).</summary>
    public System.Text.Json.Nodes.JsonObject HeaderStyle { get; set; } = [];
    public System.Text.Json.Nodes.JsonObject FooterStyle { get; set; } = [];
}

public class Theme : Entity
{
    public ThemeKind Kind { get; set; }
    public required string Title { get; set; }
    public string Description { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class ContactMessage : Entity
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string Phone { get; set; } = "";
    public required string Message { get; set; }
    public bool IsRead { get; set; }
}

/// <summary>Payment / e-invoice provider configuration. Secrets are encrypted at rest.</summary>
public class IntegrationConfig : Entity
{
    public IntegrationType Type { get; set; }
    public string Provider { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsTestMode { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = [];
    public string EncryptedSecrets { get; set; } = "";
}
