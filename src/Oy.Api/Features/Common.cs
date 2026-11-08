using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Oy.Api.Features;

public static partial class Slug
{
    private static readonly Dictionary<char, string> TurkishMap = new()
    {
        ['ç'] = "c", ['ğ'] = "g", ['ı'] = "i", ['İ'] = "i", ['ö'] = "o", ['ş'] = "s", ['ü'] = "u",
        ['Ç'] = "c", ['Ğ'] = "g", ['Ö'] = "o", ['Ş'] = "s", ['Ü'] = "u",
    };

    public static string From(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text.Trim())
            sb.Append(TurkishMap.TryGetValue(c, out var r) ? r : char.ToLowerInvariant(c).ToString());
        var ascii = new string(sb.ToString().Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return NonAlphanumeric().Replace(ascii, "-").Trim('-');
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}

/// <summary>Collects business-rule errors and returns a standard 400 ValidationProblem.</summary>
public sealed class Errors
{
    private readonly Dictionary<string, List<string>> _errors = [];

    public Errors Check(bool ok, string field, string message)
    {
        if (!ok) (_errors.TryGetValue(field, out var list) ? list : _errors[field] = []).Add(message);
        return this;
    }

    public bool Any => _errors.Count > 0;

    public IResult ToResult() => Results.ValidationProblem(
        _errors.ToDictionary(e => e.Key, e => e.Value.ToArray()), title: "Girilen bilgiler geçersiz.");
}

public static class ApiResults
{
    public static IResult Conflict(string message) =>
        Results.Problem(title: message, statusCode: StatusCodes.Status409Conflict);
}
