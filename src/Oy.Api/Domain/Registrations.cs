using System.Security.Cryptography;

namespace Oy.Api.Domain;

/// <summary>
/// Registration form of a program ("esas kayıt formu"): detailed questions sent to the parent of an approved
/// enrollment through a personal link. The child is already known, so there are only custom questions.
/// </summary>
public class RegistrationFormConfig
{
    public string Intro { get; set; } = "";
    public List<FormQuestion> Questions { get; set; } = [];
}

public enum RegistrationStatus
{
    /// <summary>Link created, not opened yet.</summary>
    Sent,
    Opened,
    Submitted,
    /// <summary>Not submitted before <see cref="RegistrationRequest.ExpiresAt"/>.</summary>
    Expired,
}

/// <summary>
/// The registration form link of one enrollment (one per enrollment). The form is copied when the link is created,
/// so later edits of the program's form do not change what the parent is answering.
/// </summary>
public class RegistrationRequest : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    /// <summary>Random URL-safe key of the link (okulumyeryuzu.com/kayit/{token}).</summary>
    public string Token { get; set; } = "";
    public string Intro { get; set; } = "";
    public List<FormQuestion> Questions { get; set; } = [];
    /// <summary>When the current link was created (a renewed link gets a new token and date).</summary>
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    /// <summary>Submitted values by question id (fills the form again when the link is renewed).</summary>
    public Dictionary<string, string> Values { get; set; } = [];
    /// <summary>Answers as shown in the panel.</summary>
    public List<EnrollmentAnswer> Answers { get; set; } = [];

    public RegistrationStatus StatusAt(DateTime now) => RegistrationLinks.Status(SubmittedAt, OpenedAt, ExpiresAt, now);
}

public static class RegistrationLinks
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static RegistrationStatus Status(DateTime? submittedAt, DateTime? openedAt, DateTime expiresAt, DateTime now) =>
        submittedAt is not null ? RegistrationStatus.Submitted
        : expiresAt <= now ? RegistrationStatus.Expired
        : openedAt is not null ? RegistrationStatus.Opened
        : RegistrationStatus.Sent;

    /// <summary>Whether the program has a registration form to send.</summary>
    public static bool HasForm(TrainingProgram p) => p.RegistrationForm.Questions.Any(q => q.Type != QuestionType.Section);

    /// <summary>
    /// Creates the link of an enrollment, or renews it: new token and validity, the program's current form, open for
    /// answers again. Earlier answers are kept to fill the form.
    /// </summary>
    public static RegistrationRequest Issue(RegistrationRequest? existing, Guid enrollmentId, TrainingProgram program, DateTime now)
    {
        var r = existing ?? new RegistrationRequest { EnrollmentId = enrollmentId };
        r.Token = NewToken();
        r.Intro = program.RegistrationForm.Intro;
        r.Questions = program.RegistrationForm.Questions;
        r.IssuedAt = now;
        r.ExpiresAt = now + Lifetime;
        r.OpenedAt = null;
        r.SubmittedAt = null;
        return r;
    }

    /// <summary>192 random bits, base64url (32 characters).</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');

    public static bool LooksLikeToken(string token) =>
        token.Length == 32 && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
