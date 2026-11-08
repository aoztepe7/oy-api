using System.Text.Json.Nodes;
using Oy.Api.Data;
using Oy.Api.Domain;
using Oy.Api.Features;
using Oy.Api.Features.Content;

namespace Oy.Api.Tests;

public class BlockContentTests
{
    private static PageBlock Block(string type, string props, string id = "b1", bool hidden = false) =>
        new() { Id = id, Type = type, Hidden = hidden, Props = JsonNode.Parse(props)!.AsObject() };

    [Fact]
    public void Drops_unknown_keys_and_fills_defaults()
    {
        var errors = new Errors();
        var result = BlockContent.Sanitize([Block("faq", """{ "title": "SSS", "bogus": 1, "items": [{ "question": "Q", "junk": true }] }""")], s => s, errors);

        Assert.False(errors.Any);
        var props = result.Single().Props;
        Assert.Null(props["bogus"]);
        Assert.Null(props["spacing"]);                           // moved to style
        Assert.Equal("", (string?)props["anchor"]);
        var item = props["items"]!.AsArray().Single()!.AsObject();
        Assert.Equal("Q", (string?)item["question"]);
        Assert.Equal("", (string?)item["answer"]);
        Assert.Null(item["junk"]);
    }

    [Fact]
    public void Coerces_values_to_field_types()
    {
        var result = BlockContent.Sanitize(
            [Block("programs", """{ "limit": 999, "featuredFirst": "yes", "layout": "carousel", "programType": "Camp" }""")], s => s, new Errors());
        var props = result.Single().Props;

        Assert.Equal(50, (int)props["limit"]!);                 // clamped to max
        Assert.True((bool)props["featuredFirst"]!);             // invalid -> default (true)
        Assert.Equal("slider", (string?)props["layout"]);       // unknown option -> default
        Assert.Equal("Camp", (string?)props["programType"]);    // valid option kept
    }

    [Fact]
    public void Converts_image_urls_including_nested_items()
    {
        var result = BlockContent.Sanitize(
            [Block("gallery", """{ "images": [{ "image": "http://x/media/a.jpg" }] }""")], s => s.Replace("http://x", ""), new Errors());

        Assert.Equal("/media/a.jpg", (string?)result.Single().Props["images"]![0]!["image"]);
    }

    [Fact]
    public void Rejects_unknown_block_types()
    {
        var errors = new Errors();
        BlockContent.Sanitize([Block("nope", "{}")], s => s, errors);
        Assert.True(errors.Any);
    }

    [Fact]
    public void Replaces_duplicate_ids()
    {
        var result = BlockContent.Sanitize([Block("text", "{}", "same"), Block("text", "{}", "same")], s => s, new Errors());
        Assert.Equal(2, result.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void Public_view_skips_hidden_blocks()
    {
        var blocks = new List<PageBlock> { Block("text", "{}"), Block("cta", "{}", "b2", hidden: true) };
        Assert.Single(BlockContent.ToPublic(blocks, s => s, includeHidden: false));
        Assert.Equal(2, BlockContent.ToPublic(blocks, s => s, includeHidden: true).Count);
    }

    [Fact]
    public void Seeded_pages_use_only_known_blocks()
    {
        foreach (var (_, _, blocks) in PageTemplates.SystemPages())
            Assert.All(blocks, b => Assert.NotNull(BlockCatalog.Find(b.Type)));
    }
}
