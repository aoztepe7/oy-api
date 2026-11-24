using Oy.Api.Domain;
using Oy.Api.Infrastructure.Integrations;

namespace Oy.Api.Tests;

public class MetaPixelTests
{
    private static Dictionary<string, string> Settings(string pixelId, string code = "") =>
        new() { [MetaPixel.PixelIdKey] = pixelId, [MetaPixel.DomainVerificationKey] = code };

    [Fact]
    public void Pixel_id_accepts_digits_only() => Assert.True(MetaPixel.IsValidPixelId("1234567890123456"));

    [Theory]
    [InlineData("")]
    [InlineData("12ab")]
    [InlineData("123")]
    [InlineData("<script>")]
    [InlineData("123 456")]
    public void Pixel_id_rejects_everything_else(string id) => Assert.False(MetaPixel.IsValidPixelId(id));

    [Fact]
    public void Active_without_pixel_id_is_an_error() =>
        Assert.Contains("settings.pixelId", MetaPixel.Validate(true, Settings("")).Fields);

    [Fact]
    public void Inactive_without_pixel_id_is_fine() => Assert.False(MetaPixel.Validate(false, Settings("")).Any);

    [Fact]
    public void Invalid_pixel_id_is_an_error_even_when_inactive() =>
        Assert.Contains("settings.pixelId", MetaPixel.Validate(false, Settings("abc")).Fields);

    [Fact]
    public void Pasted_meta_tag_is_rejected_as_domain_verification() =>
        Assert.Contains("settings.domainVerification",
            MetaPixel.Validate(true, Settings("1234567890123456", "<meta name=\"facebook-domain-verification\" content=\"abc\">")).Fields);

    [Fact]
    public void Valid_settings_pass() => Assert.False(MetaPixel.Validate(true, Settings("1234567890123456", "abc123")).Any);

    [Fact]
    public void Catalog_knows_the_pixel_provider() =>
        Assert.NotNull(IntegrationCatalog.Find(IntegrationType.Tracking, "metapixel"));
}
