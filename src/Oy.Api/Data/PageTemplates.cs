using Oy.Api.Domain;
using Oy.Api.Features.Content;

namespace Oy.Api.Data;

/// <summary>Starting content: built-in pages (seeded once) and the template for new pages.</summary>
public static class PageTemplates
{
    private const string Lorem =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer posuere erat a ante venenatis dapibus posuere velit aliquet. " +
        "Cras mattis consectetur purus sit amet fermentum.";

    private static readonly string LoremShort = Lorem[..110];

    private static PageBlock B(string type, object props) => BlockContent.Create(type, props);

    /// <summary>A new page: header + text block to start typing into.</summary>
    public static List<PageBlock> Content(string title) =>
    [
        B("pageHeader", new { title, intro = "" }),
        B("text", new { body = "" }),
    ];

    public static SiteSettings Settings() => new()
    {
        SiteName = "Okulum Yeryüzü",
        Tagline = "Doğada büyüyen çocuklar",
        FooterText = LoremShort,
        Phone = "+90 555 000 00 00",
        Email = "merhaba@okulumyeryuzu.com",
        Address = "Lorem Mah. Ipsum Sok. No: 1, İstanbul",
        InstagramUrl = "https://instagram.com/okulumyeryuzu",
        HeaderCtaText = "Ön kayıt",
        HeaderCtaHref = "/programlar",
        HeaderNav =
        [
            new("Programlar", "/programlar"), new("Kamp ve etkinlikler", "/etkinlikler"),
            new("Hakkımızda", "/hakkimizda"), new("Ekibimiz", "/ekibimiz"), new("İletişim", "/iletisim"),
        ],
        FooterNav =
        [
            new("Programlar", "/programlar"), new("Kamp ve etkinlikler", "/etkinlikler"),
            new("Hakkımızda", "/hakkimizda"), new("Ekibimiz", "/ekibimiz"), new("İletişim", "/iletisim"),
        ],
    };

    /// <summary>Built-in pages, matching the original site design.</summary>
    public static IEnumerable<(string Slug, string Title, List<PageBlock> Blocks)> SystemPages()
    {
        yield return (Page.HomeSlug, "Ana Sayfa",
        [
            B("hero", new
            {
                eyebrow = "Doğa temelli eğitim", title = "Doğa en güzel sınıftır", subtitle = Lorem,
                primaryText = "Programları keşfet", primaryHref = "/programlar",
                secondaryText = "Bize ulaşın", secondaryHref = "/iletisim", layout = "split",
            }),
            B("imageText", new { title = "Lorem ipsum dolor sit amet", body = $"{Lorem}\n\n{Lorem}", buttonText = "Hakkımızda", buttonHref = "/hakkimizda" }),
            B("features", new
            {
                background = "paper",
                items = new[]
                {
                    new { icon = "leaf", title = "Lorem ipsum", text = LoremShort },
                    new { icon = "tree", title = "Dolor sit amet", text = LoremShort },
                    new { icon = "mountain", title = "Consectetur", text = LoremShort },
                },
            }),
            B("programs", new { title = "Yaklaşan programlar", background = "sage", layout = "slider", featuredFirst = true, limit = 12 }),
            B("team", new { title = "Ekibimiz", intro = LoremShort, limit = 4, layout = "compact", linkText = "Tüm ekip", linkHref = "/ekibimiz" }),
            B("themes", new { title = "Güncel temalar", limit = 2 }),
            B("cta", new { title = "Lorem ipsum dolor sit amet", text = LoremShort, buttonText = "Programları keşfet", buttonHref = "/programlar", background = "forest" }),
        ]);

        yield return ("hakkimizda", "Hakkımızda",
        [
            B("pageHeader", new { eyebrow = "Biz kimiz", title = "Hakkımızda", intro = Lorem }),
            B("imageText", new { title = "Hikayemiz", body = $"{Lorem}\n\n{Lorem}" }),
            B("features", new
            {
                columns = "2", background = "paper",
                items = new[]
                {
                    new { icon = "compass", title = "Misyonumuz", text = Lorem },
                    new { icon = "sun", title = "Vizyonumuz", text = Lorem },
                },
            }),
            B("cta", new { title = "Birlikte doğaya çıkalım", text = LoremShort, buttonText = "Programları keşfet", buttonHref = "/programlar", background = "forest" }),
        ]);

        yield return ("ekibimiz", "Ekibimiz",
        [
            B("pageHeader", new { eyebrow = "Rehberlerimiz", title = "Ekibimiz", intro = Lorem }),
            B("team", new { layout = "grid" }),
        ]);

        yield return ("programlar", "Programlar",
        [
            B("pageHeader", new { eyebrow = "Orman okulu ve atölyeler", title = "Programlar", intro = Lorem }),
            B("programs", new { programType = "all", layout = "grid", limit = 50, featuredFirst = false, showFilters = true, linkText = "", linkHref = "" }),
        ]);

        yield return ("etkinlikler", "Kamp ve Etkinlikler",
        [
            B("pageHeader", new { eyebrow = "Doğada buluşalım", title = "Kamp ve Etkinlikler", intro = Lorem }),
            B("programs", new
            {
                programType = "events", layout = "grid", limit = 50, featuredFirst = false, linkText = "", linkHref = "",
                emptyText = "Şu an planlanmış bir kamp ya da etkinlik yok. Yakında burada olacak.",
            }),
        ]);

        yield return ("iletisim", "İletişim",
        [
            B("contact", new { title = "İletişim", intro = Lorem, workingHours = "Hafta içi 09:00 - 18:00", showForm = true }),
        ]);
    }
}
