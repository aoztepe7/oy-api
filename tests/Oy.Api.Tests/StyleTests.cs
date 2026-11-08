using System.Text.Json.Nodes;
using Oy.Api.Domain;
using Oy.Api.Features.Content;

namespace Oy.Api.Tests;

public class StyleTests
{
    private static StyleField Field(string key) => StyleCatalog.All.Single(f => f.Key == key);

    [Theory]
    [InlineData("background", "forest", "forest")]       // brand token
    [InlineData("background", "#1A2B3C", "#1a2b3c")]     // custom colour, normalised
    [InlineData("background", "#abc", "#abc")]
    [InlineData("background", "red", "")]                // not a token, not hex
    [InlineData("background", "url(x)", "")]
    [InlineData("headingSize", "72px", "72px")]          // custom px within range
    [InlineData("headingSize", "500px", "")]             // above max
    [InlineData("headingSize", "4rem", "")]              // only px
    [InlineData("headingFont", "#fff", "")]              // no custom allowed
    [InlineData("paddingTop", "0px", "0px")]
    [InlineData("width", "", "")]
    public void Cleans_values(string key, string input, string expected) =>
        Assert.Equal(expected, StyleCatalog.Clean(Field(key), input));

    [Fact]
    public void Fills_every_key_and_handles_booleans()
    {
        var style = BlockContent.CleanStyle(JsonNode.Parse("""{ "hideOnMobile": true, "bogus": "x" }""")!.AsObject(), null, StyleCatalog.All);

        Assert.Equal(StyleCatalog.All.Count, style.Count);
        Assert.True((bool)style["hideOnMobile"]!);
        Assert.False((bool)style["hideOnDesktop"]!);
        Assert.Null(style["bogus"]);
    }

    [Fact]
    public void Carries_over_legacy_background_and_spacing()
    {
        var legacy = JsonNode.Parse("""{ "background": "sage", "spacing": "compact" }""")!.AsObject();
        var style = BlockContent.CleanStyle(null, legacy, StyleCatalog.All);

        Assert.Equal("sage", (string?)style["background"]);
        Assert.Equal("sm", (string?)style["paddingTop"]);
        Assert.Equal("sm", (string?)style["paddingBottom"]);
    }

    [Fact]
    public void Explicit_style_wins_over_legacy_props()
    {
        var style = BlockContent.CleanStyle(
            JsonNode.Parse("""{ "background": "#000000" }""")!.AsObject(),
            JsonNode.Parse("""{ "background": "sage" }""")!.AsObject(),
            StyleCatalog.All);

        Assert.Equal("#000000", (string?)style["background"]);
    }
}
