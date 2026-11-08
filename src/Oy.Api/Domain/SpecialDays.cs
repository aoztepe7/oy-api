namespace Oy.Api.Domain;

/// <summary>A holiday or observance day ("özel gün") shown while planning lessons.</summary>
public class SpecialDay : Entity
{
    public required string Name { get; set; }
    public DateOnly Date { get; set; }
    public SpecialDayKind Kind { get; set; }
}

/// <summary>Built-in Turkish holidays and nature-related observances, used to seed the table.</summary>
public static class SpecialDayCatalog
{
    private static readonly (int Month, int Day, string Name, SpecialDayKind Kind)[] Fixed =
    [
        (1, 1, "Yılbaşı", SpecialDayKind.PublicHoliday),
        (3, 3, "Dünya Yaban Hayatı Günü", SpecialDayKind.Observance),
        (3, 21, "Dünya Ormancılık Günü", SpecialDayKind.Observance),
        (3, 22, "Dünya Su Günü", SpecialDayKind.Observance),
        (4, 22, "Dünya Günü", SpecialDayKind.Observance),
        (4, 23, "Ulusal Egemenlik ve Çocuk Bayramı", SpecialDayKind.PublicHoliday),
        (5, 1, "Emek ve Dayanışma Günü", SpecialDayKind.PublicHoliday),
        (5, 19, "Atatürk'ü Anma, Gençlik ve Spor Bayramı", SpecialDayKind.PublicHoliday),
        (5, 20, "Dünya Arı Günü", SpecialDayKind.Observance),
        (6, 5, "Dünya Çevre Günü", SpecialDayKind.Observance),
        (7, 15, "Demokrasi ve Millî Birlik Günü", SpecialDayKind.PublicHoliday),
        (8, 30, "Zafer Bayramı", SpecialDayKind.PublicHoliday),
        (10, 4, "Dünya Hayvanları Koruma Günü", SpecialDayKind.Observance),
        (10, 29, "Cumhuriyet Bayramı", SpecialDayKind.PublicHoliday),
        (11, 11, "Millî Ağaçlandırma Günü", SpecialDayKind.Observance),
        (11, 20, "Dünya Çocuk Hakları Günü", SpecialDayKind.Observance),
        (11, 24, "Öğretmenler Günü", SpecialDayKind.Observance),
        (12, 5, "Dünya Toprak Günü", SpecialDayKind.Observance),
    ];

    /// <summary>Religious holidays follow the lunar calendar; later years are added from the panel.</summary>
    private static readonly (string Name, DateOnly Eve, int Days)[] Religious =
    [
        ("Ramazan Bayramı", new(2025, 3, 29), 3),
        ("Kurban Bayramı", new(2025, 6, 5), 4),
        ("Ramazan Bayramı", new(2026, 3, 19), 3),
        ("Kurban Bayramı", new(2026, 5, 26), 4),
        ("Ramazan Bayramı", new(2027, 3, 8), 3),
        ("Kurban Bayramı", new(2027, 5, 15), 4),
    ];

    public static IEnumerable<SpecialDay> Build(int fromYear, int toYear)
    {
        for (var year = fromYear; year <= toYear; year++)
        {
            foreach (var (month, day, name, kind) in Fixed)
                yield return new SpecialDay { Name = name, Date = new DateOnly(year, month, day), Kind = kind };
            yield return new SpecialDay { Name = "Anneler Günü", Date = NthSunday(year, 5, 2), Kind = SpecialDayKind.Observance };
            yield return new SpecialDay { Name = "Babalar Günü", Date = NthSunday(year, 6, 3), Kind = SpecialDayKind.Observance };
        }

        foreach (var (name, eve, days) in Religious.Where(r => r.Eve.Year >= fromYear && r.Eve.Year <= toYear))
        {
            yield return new SpecialDay { Name = $"{name} Arifesi", Date = eve, Kind = SpecialDayKind.ReligiousHoliday };
            for (var i = 1; i <= days; i++)
                yield return new SpecialDay { Name = $"{name} {i}. Gün", Date = eve.AddDays(i), Kind = SpecialDayKind.ReligiousHoliday };
        }
    }

    private static DateOnly NthSunday(int year, int month, int n)
    {
        var first = new DateOnly(year, month, 1);
        var offset = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }
}
