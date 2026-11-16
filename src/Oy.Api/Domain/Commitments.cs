using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Oy.Api.Domain;

/// <summary>
/// A commitment text ("taahhütname") the parent signs. Kept in a library: a program picks the ones it needs and the
/// same text can be used by several programs. Placeholders are filled per enrollment (see <see cref="CommitmentRules.Render"/>).
/// Editing changes what is signed from then on; texts already signed are frozen in their <see cref="CommitmentSignature"/>.
/// </summary>
public class CommitmentTemplate : Entity
{
    public required string Title { get; set; }
    public string Body { get; set; } = "";
    public List<ProgramCommitment> Programs { get; set; } = [];
}

/// <summary>A commitment required from the parents of a program's enrollments.</summary>
public class ProgramCommitment
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public Guid TemplateId { get; set; }
    public CommitmentTemplate Template { get; set; } = null!;
    /// <summary>Display order within the program.</summary>
    public int Position { get; set; }
}

/// <summary>Where the parent signed.</summary>
public enum CommitmentChannel
{
    /// <summary>On a device of the school, with a staff member signed in (recorded as witness).</summary>
    Tablet,
    /// <summary>In the guardian app, signed in with their own guardian account.</summary>
    GuardianApp,
    /// <summary>Through the personal link sent to the parent.</summary>
    Link,
}

/// <summary>
/// The signature of one commitment for one enrollment. Created when its link is first needed (a staff member opens
/// it on a tablet or copies the link, or the guardian opens it in the app); filled once when the parent signs.
/// At signing, the exact text the parent saw is frozen together with its SHA-256 hash, the drawn signature (PNG),
/// time, IP address and device. Only the image of the signature is kept (no pen pressure / timing).
/// </summary>
public class CommitmentSignature : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public Guid TemplateId { get; set; }
    public CommitmentTemplate Template { get; set; } = null!;
    /// <summary>Random URL-safe key of the signing page (/imza/{token}); also opens the signed PDF.</summary>
    public string Token { get; set; } = "";
    public DateTime? OpenedAt { get; set; }

    public DateTime? SignedAt { get; set; }
    public string SignedTitle { get; set; } = "";
    public string SignedBody { get; set; } = "";
    /// <summary>SHA-256 (hex) of the signed title and text, printed on the PDF.</summary>
    public string BodyHash { get; set; } = "";
    /// <summary>Name the parent typed when signing.</summary>
    public string SignerName { get; set; } = "";
    public byte[]? SignatureImage { get; set; }
    public CommitmentChannel? Channel { get; set; }
    /// <summary>The signed-in user at signing: the guardian (app) or the staff member present (tablet).</summary>
    public Guid? SignedByUserId { get; set; }
    /// <summary>Staff member present when signed on a tablet.</summary>
    public string WitnessName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";
}

public static class CommitmentRules
{
    public const int TitleMax = 200;
    public const int BodyMax = 20_000;
    public const int SignerNameMax = 120;
    /// <summary>PNG of the drawn signature.</summary>
    public const int SignatureMaxBytes = 400 * 1024;

    /// <summary>Placeholders available in a commitment text.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Placeholders =
    [
        ("{veli}", "Velinin adı soyadı"),
        ("{cocuk}", "Çocuğun adı soyadı"),
        ("{egitim}", "Eğitimin adı"),
        ("{tarih}", "Bugünün tarihi"),
    ];

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>The text as the parent of this enrollment reads it on <paramref name="day"/>.</summary>
    public static string Render(string text, string parentName, string childName, string programTitle, DateOnly day) =>
        text.Replace("{veli}", parentName)
            .Replace("{cocuk}", childName)
            .Replace("{egitim}", programTitle)
            .Replace("{tarih}", day.ToString("d MMMM yyyy", Turkish));

    /// <summary>Fingerprint of what is signed; the page sends back the one it showed, so a text changed meanwhile is not signed unseen.</summary>
    public static string Hash(string title, string body) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title.Trim() + "\n\n" + body.Trim())));

    /// <summary>Whether the bytes are a PNG image (by its signature, not the client's word).</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    /// <summary>Decodes "data:image/png;base64,…" (or bare base64) into PNG bytes; null if it is not a PNG within the size limit.</summary>
    public static byte[]? DecodeSignature(string? dataUrl)
    {
        if (string.IsNullOrWhiteSpace(dataUrl)) return null;
        var comma = dataUrl.IndexOf(',');
        var base64 = comma >= 0 && dataUrl.StartsWith("data:", StringComparison.Ordinal) ? dataUrl[(comma + 1)..] : dataUrl;
        if (base64.Length > SignatureMaxBytes * 4 / 3 + 4) return null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length <= SignatureMaxBytes && IsPng(bytes) ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
