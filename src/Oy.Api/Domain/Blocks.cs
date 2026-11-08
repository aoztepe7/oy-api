using System.Text.Json.Nodes;

namespace Oy.Api.Domain;

/// <summary>One section of a CMS page. Props follow the <see cref="BlockDefinition"/> of its type.</summary>
public class PageBlock
{
    /// <summary>Client-generated id, stable across edits (used as React key / anchor).</summary>
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    /// <summary>Hidden blocks stay in the page but are not rendered on the site.</summary>
    public bool Hidden { get; set; }
    public JsonObject Props { get; set; } = [];
    /// <summary>Style overrides; keys from <see cref="StyleCatalog.All"/>, "" = default.</summary>
    public JsonObject Style { get; set; } = [];
}

public record BlockOption(string Value, string Label);

/// <summary>
/// An editable property of a block. Types: text, textarea, markdown, image, link, select, boolean, number, items.
/// <c>items</c> is a repeater whose entries have <see cref="ItemFields"/>.
/// </summary>
public record BlockField(
    string Key,
    string Label,
    string Type = "text",
    string Group = "content",
    string? Hint = null,
    IReadOnlyList<BlockOption>? Options = null,
    IReadOnlyList<BlockField>? ItemFields = null,
    JsonNode? Default = null,
    int? Max = null);

public record BlockDefinition(string Type, string Label, string Description, string Icon, IReadOnlyList<BlockField> Fields);

/// <summary>
/// All block types the site can render. The panel builds its editor from this list,
/// the API uses it to sanitise input, oy-ui has one renderer per type.
/// </summary>
public static class BlockCatalog
{
    private static BlockField Text(string key, string label, string? hint = null, string? def = null) => new(key, label, Hint: hint, Default: def);
    private static BlockField Area(string key, string label, string? hint = null) => new(key, label, "textarea", Hint: hint);
    private static BlockField Markdown(string key, string label) =>
        new(key, label, "markdown", Hint: "Boş satır yeni paragraf. **kalın**, *italik*, [bağlantı](/iletisim), ## Ara başlık, - liste");
    private static BlockField Image(string key, string label) => new(key, label, "image");
    private static BlockField Link(string key, string label, string? def = null) => new(key, label, "link", Hint: "Örn. /programlar veya https://…", Default: def);
    private static BlockField Bool(string key, string label, bool def, string group = "content") => new(key, label, "boolean", group, Default: def);
    private static BlockField Number(string key, string label, int def, int max) => new(key, label, "number", Default: def, Max: max);

    private static BlockField Select(string key, string label, string group, params (string Value, string Label)[] options) =>
        new(key, label, "select", group, Options: [.. options.Select(o => new BlockOption(o.Value, o.Label))], Default: options[0].Value);

    private static BlockField Items(string key, string label, int max, params BlockField[] fields) =>
        new(key, label, "items", ItemFields: fields, Max: max);

    private static readonly (string, string)[] Icons =
        [("leaf", "Yaprak"), ("tree", "Ağaç"), ("mountain", "Dağ"), ("sun", "Güneş"), ("water", "Su"), ("fire", "Ateş"), ("compass", "Pusula"), ("heart", "Kalp")];

    /// <summary>Fields every block has. Colours, fonts and spacing live in <see cref="PageBlock.Style"/>.</summary>
    public static readonly IReadOnlyList<BlockField> CommonFields =
    [
        new("anchor", "Bağlantı adı (#)", Group: "layout", Hint: "Örn. sss — sayfa içi bağlantı için /sayfa#sss"),
    ];

    public static readonly IReadOnlyList<BlockDefinition> All =
    [
        new("hero", "Kapak (Hero)", "Sayfanın en üstündeki büyük başlık, görsel ve butonlar.", "image",
        [
            Text("eyebrow", "Üst başlık"),
            Text("title", "Başlık"),
            Area("subtitle", "Alt başlık"),
            Image("image", "Görsel"),
            Text("primaryText", "Ana buton metni"), Link("primaryHref", "Ana buton bağlantısı"),
            Text("secondaryText", "İkinci buton metni"), Link("secondaryHref", "İkinci buton bağlantısı"),
            Select("layout", "Yerleşim", "layout", ("split", "Metin solda, görsel sağda"), ("cover", "Görsel arka planda"), ("centered", "Ortalanmış, görselsiz")),
        ]),
        new("pageHeader", "Sayfa başlığı", "İç sayfaların üstündeki sade başlık ve giriş metni.", "file",
        [
            Text("eyebrow", "Üst başlık"),
            Text("title", "Başlık"),
            Area("intro", "Giriş metni"),
            Select("align", "Hizalama", "layout", ("left", "Sola"), ("center", "Ortaya")),
        ]),
        new("text", "Metin", "Başlık ve serbest metin (paragraf, liste, bağlantı).", "edit",
        [
            Text("title", "Başlık"),
            Markdown("body", "Metin"),
            Select("width", "Genişlik", "layout", ("narrow", "Dar (okunaklı)"), ("wide", "Geniş")),
            Select("align", "Hizalama", "layout", ("left", "Sola"), ("center", "Ortaya")),
        ]),
        new("imageText", "Görsel + metin", "Yan yana görsel ve metin; isteğe bağlı buton.", "image",
        [
            Text("eyebrow", "Üst başlık"),
            Text("title", "Başlık"),
            Markdown("body", "Metin"),
            Image("image", "Görsel"),
            Text("buttonText", "Buton metni"), Link("buttonHref", "Buton bağlantısı"),
            Select("imagePosition", "Görsel konumu", "layout", ("right", "Sağda"), ("left", "Solda")),
        ]),
        new("features", "Özellikler / değerler", "İkonlu kısa maddeler (örn. değerlerimiz).", "leaf",
        [
            Text("title", "Başlık"),
            Area("intro", "Giriş"),
            Items("items", "Maddeler", 12,
                new BlockField("icon", "İkon", "select", Options: [.. Icons.Select(i => new BlockOption(i.Item1, i.Item2))], Default: "leaf"),
                Text("title", "Başlık"), Area("text", "Metin")),
            Select("columns", "Sütun", "layout", ("3", "3 sütun"), ("2", "2 sütun"), ("4", "4 sütun")),
        ]),
        new("programs", "Eğitimler", "Yayındaki eğitimler; slider veya liste, türe göre filtreli.", "calendar",
        [
            Text("title", "Başlık"),
            Area("intro", "Giriş"),
            Select("programType", "Gösterilecek eğitimler", "content",
                ("all", "Tümü"), ("WeeklyClass", "Orman okulu (düzenli gruplar)"), ("events", "Kamp, etkinlik ve atölyeler"),
                ("Camp", "Sadece kamplar"), ("Event", "Sadece etkinlikler"), ("Workshop", "Sadece atölyeler")),
            Number("limit", "En fazla", 12, 50),
            Bool("featuredFirst", "Öne çıkanlar başta", true),
            Bool("showFilters", "Tür ve yaş filtresi göster", false),
            Text("linkText", "Bağlantı metni", def: "Tüm programlar"), Link("linkHref", "Bağlantı", "/programlar"),
            Text("emptyText", "Eğitim yokken metin", def: "Yeni programlarımız çok yakında burada olacak."),
            Select("layout", "Görünüm", "layout", ("slider", "Slider"), ("grid", "Izgara")),
        ]),
        new("themes", "Güncel temalar", "Bu hafta/ay işlenen temalar.", "sun",
        [
            Text("title", "Başlık"),
            Area("intro", "Giriş"),
            Number("limit", "En fazla", 2, 12),
        ]),
        new("team", "Ekip", "Sitede gösterilmesi seçilen personel.", "users",
        [
            Text("title", "Başlık"),
            Area("intro", "Giriş"),
            Number("limit", "En fazla", 0, 50),
            Text("linkText", "Bağlantı metni"), Link("linkHref", "Bağlantı"),
            Select("layout", "Görünüm", "layout", ("grid", "Kartlar"), ("compact", "Kompakt (görsel + liste)")),
        ]),
        new("cta", "Çağrı (CTA)", "Dikkat çeken kısa çağrı ve buton.", "mail",
        [
            Text("title", "Başlık"),
            Area("text", "Metin"),
            Text("buttonText", "Buton metni"), Link("buttonHref", "Buton bağlantısı"),
        ]),
        new("gallery", "Galeri", "Görsel ızgarası.", "image",
        [
            Text("title", "Başlık"),
            Items("images", "Görseller", 30, Image("image", "Görsel"), Text("caption", "Açıklama")),
            Select("columns", "Sütun", "layout", ("3", "3 sütun"), ("2", "2 sütun"), ("4", "4 sütun")),
        ]),
        new("faq", "Sıkça sorulan sorular", "Açılır soru-cevap listesi.", "info",
        [
            Text("title", "Başlık"),
            Items("items", "Sorular", 40, Text("question", "Soru"), Area("answer", "Cevap")),
        ]),
        new("testimonials", "Veli yorumları", "Alıntılar.", "users",
        [
            Text("title", "Başlık"),
            Items("items", "Yorumlar", 20, Area("quote", "Yorum"), Text("author", "Ad"), Text("role", "Açıklama (örn. 4 yaş velisi)")),
        ]),
        new("contact", "İletişim", "İletişim bilgileri (site ayarlarından), form ve harita.", "mail",
        [
            Text("title", "Başlık"),
            Area("intro", "Giriş"),
            Text("workingHours", "Çalışma saatleri"),
            Text("mapEmbedUrl", "Harita embed URL", "Google Haritalar → Paylaş → Harita yerleştir → src adresi"),
            Image("image", "Görsel"),
            Bool("showForm", "İletişim formu", true),
        ]),
        new("embed", "Video / harita", "YouTube, Vimeo veya Google Haritalar bağlantısı.", "plug",
        [
            Text("title", "Başlık"),
            Text("url", "Bağlantı", "YouTube/Vimeo video adresi veya Google Haritalar embed adresi"),
            Text("caption", "Açıklama"),
        ]),
    ];

    public static BlockDefinition? Find(string type) => All.FirstOrDefault(b => b.Type == type);
}
