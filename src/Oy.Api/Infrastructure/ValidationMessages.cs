using System.Text.RegularExpressions;

namespace Oy.Api.Infrastructure;

/// Translates the default (English) DataAnnotations messages produced by AddValidation() into Turkish.
public static partial class ValidationMessages
{
    public static string Translate(string message)
    {
        if (Required().IsMatch(message)) return "Bu alan zorunludur.";
        if (MaxLen().Match(message) is { Success: true } max) return $"En fazla {max.Groups["n"].Value} karakter olabilir.";
        if (MinLen().Match(message) is { Success: true } min) return $"En az {min.Groups["n"].Value} karakter olmalıdır.";
        if (Range().Match(message) is { Success: true } r) return $"{r.Groups["min"].Value} ile {r.Groups["max"].Value} arasında olmalıdır.";
        if (Email().IsMatch(message)) return "Geçerli bir e-posta adresi girin.";
        if (Phone().IsMatch(message)) return "Geçerli bir telefon numarası girin.";
        if (Url().IsMatch(message)) return "Geçerli bir adres (URL) girin.";
        return message; // already Turkish (Errors.Check) or unknown: unchanged
    }

    [GeneratedRegex(@"^The .+ field is required\.$")] private static partial Regex Required();
    [GeneratedRegex(@"maximum length of '?(?<n>\d+)'?")] private static partial Regex MaxLen();
    [GeneratedRegex(@"minimum length of '?(?<n>\d+)'?")] private static partial Regex MinLen();
    [GeneratedRegex(@"^The field .+ must be between (?<min>\S+) and (?<max>\S+)\.$")] private static partial Regex Range();
    [GeneratedRegex(@"is not a valid e-mail address\.$")] private static partial Regex Email();
    [GeneratedRegex(@"is not a valid phone number\.$")] private static partial Regex Phone();
    [GeneratedRegex(@"is not a valid fully-qualified http, https, or ftp URL\.$")] private static partial Regex Url();
}
