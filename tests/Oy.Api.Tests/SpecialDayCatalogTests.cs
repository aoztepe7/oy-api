using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class SpecialDayCatalogTests
{
    private static readonly List<SpecialDay> Days2026 = SpecialDayCatalog.Build(2026, 2026).ToList();

    [Theory]
    [InlineData("Anneler Günü", 2026, 5, 10)]
    [InlineData("Babalar Günü", 2026, 6, 21)]
    [InlineData("Cumhuriyet Bayramı", 2026, 10, 29)]
    [InlineData("Ramazan Bayramı 1. Gün", 2026, 3, 20)]
    [InlineData("Kurban Bayramı 4. Gün", 2026, 5, 30)]
    public void Builds_expected_dates(string name, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Days2026.Single(x => x.Name == name).Date);

    [Fact]
    public void Names_are_unique_per_date() =>
        Assert.Equal(Days2026.Count, Days2026.Select(x => (x.Date, x.Name)).Distinct().Count());
}
