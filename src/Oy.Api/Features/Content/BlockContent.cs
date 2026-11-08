using System.Text.Json;
using System.Text.Json.Nodes;
using Oy.Api.Domain;

namespace Oy.Api.Features.Content;

/// <summary>
/// Normalises block props against the <see cref="BlockCatalog"/>: unknown keys are dropped, values are coerced to
/// the field type, missing values get defaults, and image URLs are converted (relative when stored, absolute when served).
/// </summary>
public static class BlockContent
{
    public const int MaxBlocks = 60;
    private const int MaxText = 20_000;

    /// <summary>Validates and cleans blocks coming from the panel. Errors go to <paramref name="errors"/>.</summary>
    public static List<PageBlock> Sanitize(IEnumerable<PageBlock>? blocks, Func<string, string> storeImage, Errors errors)
    {
        var list = (blocks ?? []).ToList();
        errors.Check(list.Count <= MaxBlocks, "blocks", $"Bir sayfada en fazla {MaxBlocks} blok olabilir.");
        var result = new List<PageBlock>();
        var ids = new HashSet<string>();
        foreach (var block in list.Take(MaxBlocks))
        {
            var def = BlockCatalog.Find(block.Type ?? "");
            if (def is null)
            {
                errors.Check(false, "blocks", $"Bilinmeyen blok türü: {block.Type}");
                continue;
            }
            var id = !string.IsNullOrWhiteSpace(block.Id) && block.Id.Length <= 64 && ids.Add(block.Id) ? block.Id : NewId(ids);
            result.Add(new PageBlock
            {
                Id = id, Type = def.Type, Hidden = block.Hidden,
                Props = Map(block.Props, Fields(def), storeImage), Style = CleanStyle(block.Style, block.Props, StyleCatalog.All),
            });
        }
        return result;
    }

    /// <summary>Blocks as served (panel draft or public page): defaults filled, image URLs absolute.</summary>
    public static List<PageBlock> ToPublic(IEnumerable<PageBlock> blocks, Func<string, string> publicImage, bool includeHidden) =>
        blocks.Where(b => includeHidden || !b.Hidden)
            .Select(b => BlockCatalog.Find(b.Type) is { } def
                ? new PageBlock
                {
                    Id = b.Id, Type = b.Type, Hidden = b.Hidden,
                    Props = Map(b.Props, Fields(def), publicImage), Style = CleanStyle(b.Style, b.Props, StyleCatalog.All),
                }
                : null)
            .OfType<PageBlock>()
            .ToList();

    /// <summary>A block of the given type with default props (used by seeding and page templates).</summary>
    public static PageBlock Create(string type, object? props = null)
    {
        var def = BlockCatalog.Find(type) ?? throw new ArgumentException($"Unknown block type {type}");
        var input = props is null ? new JsonObject() : JsonSerializer.SerializeToNode(props)!.AsObject();
        return new PageBlock
        {
            Id = Guid.NewGuid().ToString("N")[..12], Type = type,
            Props = Map(input, Fields(def), s => s), Style = CleanStyle(null, input, StyleCatalog.All),
        };
    }

    /// <summary>
    /// Keeps valid style values (brand token or allowed custom value). Blocks saved before styles existed had
    /// <c>background</c>/<c>spacing</c> props; those are carried over when the style has no value yet.
    /// </summary>
    public static JsonObject CleanStyle(JsonObject? style, JsonObject? legacyProps, IReadOnlyList<StyleField> fields)
    {
        var output = new JsonObject();
        foreach (var field in fields)
            output[field.Key] = field.Type == "boolean"
                ? Bool(style?[field.Key]) ?? false
                : StyleCatalog.Clean(field, Str(style?[field.Key]));

        if (legacyProps is null) return output;
        if (output["background"]?.GetValue<string>() == "" && Str(legacyProps["background"]) is "paper" or "sage" or "forest")
            output["background"] = Str(legacyProps["background"]);
        var padding = Str(legacyProps["spacing"]) switch { "compact" => "sm", "spacious" => "xl", "none" => "none", _ => "" };
        foreach (var key in new[] { "paddingTop", "paddingBottom" })
            if (padding != "" && output[key]?.GetValue<string>() == "") output[key] = padding;
        return output;
    }

    private static IEnumerable<BlockField> Fields(BlockDefinition def) => def.Fields.Concat(BlockCatalog.CommonFields);

    private static string NewId(HashSet<string> ids)
    {
        string id;
        do id = Guid.NewGuid().ToString("N")[..12]; while (!ids.Add(id));
        return id;
    }

    private static JsonObject Map(JsonObject? input, IEnumerable<BlockField> fields, Func<string, string> image)
    {
        var output = new JsonObject();
        foreach (var field in fields)
        {
            var node = input?[field.Key];
            output[field.Key] = field.Type switch
            {
                "boolean" => Bool(node) ?? Bool(field.Default) ?? false,
                "number" => Math.Clamp(Int(node) ?? Int(field.Default) ?? 0, 0, field.Max ?? 1000),
                "select" => Str(node) is { } v && field.Options!.Any(o => o.Value == v) ? v : Str(field.Default) ?? field.Options![0].Value,
                "image" => image(Str(node) ?? ""),
                "items" => Items(node, field, image),
                _ => Truncate(Str(node) ?? Str(field.Default) ?? ""),
            };
        }
        return output;
    }

    private static JsonArray Items(JsonNode? node, BlockField field, Func<string, string> image)
    {
        var array = new JsonArray();
        if (node is not JsonArray items) return array;
        foreach (var item in items.OfType<JsonObject>().Take(field.Max ?? 50))
            array.Add(Map(item, field.ItemFields!, image));
        return array;
    }

    private static string Truncate(string s) => s.Length > MaxText ? s[..MaxText] : s;

    private static string? Str(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) ? s
        : n is JsonValue num && num.TryGetValue<double>(out var d) ? d.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : null;

    private static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    private static int? Int(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<double>(out var d) ? (int)d
        : n is JsonValue s && s.TryGetValue<string>(out var str) && int.TryParse(str, out var i) ? i
        : null;
}
