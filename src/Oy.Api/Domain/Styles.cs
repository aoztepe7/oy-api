using System.Text.RegularExpressions;

namespace Oy.Api.Domain;

/// <summary>
/// A style property of a block (or header/footer). The value is "" (default), one of <see cref="Tokens"/>
/// (brand scale), or — when <see cref="Custom"/> is set — a free value: "color" = "#rgb"/"#rrggbb"/"#rrggbbaa",
/// "px" = "NNpx" within <see cref="Min"/>..<see cref="Max"/>. Type "boolean" is a plain true/false flag.
/// </summary>
public record StyleField(
    string Key,
    string Label,
    string Group,
    IReadOnlyList<BlockOption> Tokens,
    string? Custom = null,
    int? Min = null,
    int? Max = null,
    string Type = "token");

/// <summary>Brand-constrained style options with optional free overrides. Rendered by oy-ui, edited in oy-panel.</summary>
public static partial class StyleCatalog
{
    private static IReadOnlyList<BlockOption> T(params (string Value, string Label)[] t) =>
        [new("", "Varsayılan"), .. t.Select(x => new BlockOption(x.Value, x.Label))];

    private static readonly IReadOnlyList<BlockOption> Space =
        T(("none", "Yok"), ("xs", "XS"), ("sm", "S"), ("md", "M"), ("lg", "L"), ("xl", "XL"), ("2xl", "2XL"));

    public static readonly IReadOnlyList<StyleField> All =
    [
        // Renk
        new("background", "Arka plan", "color",
            T(("paper", "Açık"), ("white", "Beyaz"), ("sand", "Kum"), ("sage", "Adaçayı"), ("forest", "Orman yeşili"), ("ochre", "Toprak")),
            Custom: "color"),
        new("textColor", "Yazı rengi", "color",
            T(("forest", "Orman"), ("ink", "Koyu"), ("bark", "Kahve"), ("ochre", "Toprak"), ("light", "Açık")),
            Custom: "color"),
        new("accentColor", "Vurgu (buton, bağlantı)", "color",
            T(("ochre", "Toprak"), ("forest", "Orman"), ("moss", "Yosun")),
            Custom: "color"),
        // Tipografi
        new("headingFont", "Başlık fontu", "typography", T(("serif", "Fraunces (serif)"), ("sans", "Nunito Sans"))),
        new("bodyFont", "Metin fontu", "typography", T(("sans", "Nunito Sans"), ("serif", "Fraunces (serif)"))),
        new("headingSize", "Başlık boyutu", "typography",
            T(("sm", "S"), ("md", "M"), ("lg", "L"), ("xl", "XL"), ("2xl", "2XL")), Custom: "px", Min: 14, Max: 160),
        new("textSize", "Metin boyutu", "typography",
            T(("sm", "S"), ("md", "M"), ("lg", "L"), ("xl", "XL")), Custom: "px", Min: 10, Max: 40),
        new("align", "Hizalama", "typography", T(("left", "Sola"), ("center", "Ortaya"), ("right", "Sağa"))),
        // Boşluk ve yerleşim
        new("paddingTop", "Üst boşluk", "spacing", Space, Custom: "px", Min: 0, Max: 400),
        new("paddingBottom", "Alt boşluk", "spacing", Space, Custom: "px", Min: 0, Max: 400),
        new("width", "İçerik genişliği", "spacing", T(("narrow", "Dar"), ("wide", "Geniş"), ("full", "Tam ekran")), Custom: "px", Min: 320, Max: 2000),
        new("radius", "Köşe yuvarlaklığı", "spacing", T(("none", "Yok"), ("sm", "S"), ("md", "M"), ("lg", "L")), Custom: "px", Min: 0, Max: 64),
        // Görünürlük
        new("hideOnMobile", "Telefonda gizle", "visibility", [], Type: "boolean"),
        new("hideOnDesktop", "Masaüstünde gizle", "visibility", [], Type: "boolean"),
    ];

    /// <summary>Fields that apply to the global header and footer.</summary>
    public static readonly IReadOnlyList<StyleField> Layout =
        [.. All.Where(f => f.Key is "background" or "textColor" or "accentColor" or "paddingTop" or "paddingBottom" or "width")];

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
    private static partial Regex HexColor();

    [GeneratedRegex("^([0-9]{1,4})px$")]
    private static partial Regex Pixels();

    /// <summary>Returns the value if valid for the field, otherwise "" (default).</summary>
    public static string Clean(StyleField field, string? value)
    {
        value = value?.Trim() ?? "";
        if (value.Length == 0 || field.Tokens.Any(t => t.Value == value)) return value;
        return field.Custom switch
        {
            "color" when HexColor().IsMatch(value) => value.ToLowerInvariant(),
            "px" when Pixels().Match(value) is { Success: true } m
                && int.Parse(m.Groups[1].Value) is var px && px >= (field.Min ?? 0) && px <= (field.Max ?? 9999) => value,
            _ => "",
        };
    }
}
