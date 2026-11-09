using System.Globalization;
using System.Text.RegularExpressions;

namespace Oy.Api.Domain;

/// <summary>How a built-in pre-registration field is asked.</summary>
public enum FieldMode { Hidden, Optional, Required }

public enum QuestionType { Text, Textarea, Select, Radio, Checkbox, Date, Number, YesNo }

/// <summary>A custom question on a program's pre-registration form.</summary>
public class FormQuestion
{
    /// <summary>Stable key of the question (answers are matched by it).</summary>
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public QuestionType Type { get; set; }
    public bool Required { get; set; }
    /// <summary>Asked once per child (e.g. allergies, T-shirt size) instead of once per family.</summary>
    public bool PerChild { get; set; }
    /// <summary>Choices of Select / Radio questions.</summary>
    public List<string> Options { get; set; } = [];
    public string Hint { get; set; } = "";
}

/// <summary>
/// Pre-registration form of a program, set in the panel. Child and parent names are always asked
/// (an enrollment is identified by them); everything else is configurable.
/// </summary>
public class EnrollmentFormConfig
{
    public string Intro { get; set; } = "";
    public FieldMode ChildBirthDate { get; set; } = FieldMode.Required;
    public FieldMode ParentPhone { get; set; } = FieldMode.Required;
    public FieldMode ParentEmail { get; set; } = FieldMode.Required;
    public FieldMode Note { get; set; } = FieldMode.Optional;
    public bool AllowSiblings { get; set; } = true;
    public List<FormQuestion> Questions { get; set; } = [];
}

/// <summary>Answer to a custom question, stored with the question text as asked at the time.</summary>
public record EnrollmentAnswer(string Question, string Value);

/// <summary>One child of a submitted pre-registration with its per-child answers (question id → value).</summary>
public record SubmittedChild(string Name, DateOnly? BirthDate, IReadOnlyDictionary<string, string> Answers);

public static partial class EnrollmentForms
{
    public const int MaxQuestions = 30;
    public const int MaxOptions = 30;
    public const int MaxChildren = 6;

    [GeneratedRegex(@"^[+\d][\d\s()-]{8,}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    public static bool IsChoice(QuestionType t) => t is QuestionType.Select or QuestionType.Radio;

    /// <summary>Cleans a form definition from the panel; returns error messages keyed by field.</summary>
    public static (EnrollmentFormConfig Config, List<(string Field, string Message)> Errors) Sanitize(EnrollmentFormConfig? input)
    {
        var errors = new List<(string, string)>();
        var c = input ?? new EnrollmentFormConfig();
        var questions = new List<FormQuestion>();
        var ids = new HashSet<string>();
        foreach (var q in c.Questions ?? [])
        {
            var label = (q.Label ?? "").Trim();
            if (label.Length == 0) continue; // empty rows from the editor
            var options = IsChoice(q.Type)
                ? (q.Options ?? []).Select(o => (o ?? "").Trim()).Where(o => o.Length > 0).Distinct().Take(MaxOptions).Select(o => Cut(o, 100)).ToList()
                : [];
            if (IsChoice(q.Type) && options.Count < 2) errors.Add(("enrollmentForm", $"“{label}” sorusu için en az iki seçenek girin."));
            var id = string.IsNullOrWhiteSpace(q.Id) || !ids.Add(q.Id.Trim()) ? NewId(ids) : q.Id.Trim();
            questions.Add(new FormQuestion
            {
                Id = Cut(id, 40), Label = Cut(label, 200), Type = q.Type, Required = q.Required, PerChild = q.PerChild,
                Options = options, Hint = Cut((q.Hint ?? "").Trim(), 300),
            });
        }
        if (questions.Count > MaxQuestions) errors.Add(("enrollmentForm", $"En fazla {MaxQuestions} soru eklenebilir."));
        if (c.ParentPhone == FieldMode.Hidden && c.ParentEmail == FieldMode.Hidden)
            errors.Add(("enrollmentForm", "Size dönebilmemiz için telefon ya da e-postadan en az biri formda olmalı."));

        return (new EnrollmentFormConfig
        {
            Intro = Cut((c.Intro ?? "").Trim(), 1000),
            ChildBirthDate = c.ChildBirthDate, ParentPhone = c.ParentPhone, ParentEmail = c.ParentEmail, Note = c.Note,
            AllowSiblings = c.AllowSiblings, Questions = questions,
        }, errors);
    }

    /// <summary>
    /// Validates a submission against the form. Error keys: <c>parentPhone</c>, <c>children[0].birthDate</c>,
    /// <c>answers.{id}</c>, <c>children[1].answers.{id}</c>. Returns the stored answers per child (family answers included).
    /// </summary>
    public static (List<List<EnrollmentAnswer>> AnswersPerChild, Dictionary<string, string> Errors) Validate(
        EnrollmentFormConfig form, IReadOnlyList<SubmittedChild> children, string? phone, string? email, string? note,
        IReadOnlyDictionary<string, string>? familyAnswers)
    {
        var errors = new Dictionary<string, string>();
        void Fail(string key, string message) => errors.TryAdd(key, message);

        if (children.Count == 0) Fail("children", "En az bir çocuk ekleyin.");
        if (children.Count > (form.AllowSiblings ? MaxChildren : 1))
            Fail("children", form.AllowSiblings ? $"Bir ön kayıtta en fazla {MaxChildren} çocuk olabilir." : "Bu program için çocuk başına ayrı ön kayıt yapılır.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            if (child.Name.Trim().Length < 2) Fail($"children[{i}].name", "Çocuğun adını yazın.");
            if (form.ChildBirthDate == FieldMode.Required && child.BirthDate is null) Fail($"children[{i}].birthDate", "Doğum tarihini seçin.");
            if (child.BirthDate > today) Fail($"children[{i}].birthDate", "Geçerli bir doğum tarihi seçin.");
        }

        phone = phone?.Trim() ?? "";
        email = email?.Trim() ?? "";
        if (form.ParentPhone == FieldMode.Required && phone.Length == 0) Fail("parentPhone", "Telefon numaranızı yazın.");
        if (phone.Length > 0 && !PhoneRegex().IsMatch(phone)) Fail("parentPhone", "Geçerli bir telefon numarası yazın.");
        if (form.ParentEmail == FieldMode.Required && email.Length == 0) Fail("parentEmail", "E-posta adresinizi yazın.");
        if (email.Length > 0 && !EmailRegex().IsMatch(email)) Fail("parentEmail", "Geçerli bir e-posta adresi yazın.");
        if (form.Note == FieldMode.Required && string.IsNullOrWhiteSpace(note)) Fail("note", "Bu alanı doldurun.");

        var family = new List<EnrollmentAnswer>();
        foreach (var q in form.Questions.Where(q => !q.PerChild))
        {
            var value = Check(q, familyAnswers, $"answers.{q.Id}", Fail);
            if (value is not null) family.Add(new EnrollmentAnswer(q.Label, value));
        }

        var perChild = new List<List<EnrollmentAnswer>>();
        for (var i = 0; i < children.Count; i++)
        {
            var answers = new List<EnrollmentAnswer>(family);
            foreach (var q in form.Questions.Where(q => q.PerChild))
            {
                var value = Check(q, children[i].Answers, $"children[{i}].answers.{q.Id}", Fail);
                if (value is not null) answers.Add(new EnrollmentAnswer(q.Label, value));
            }
            perChild.Add(answers);
        }
        return (perChild, errors);
    }

    /// <summary>Normalised answer text, or null when unanswered (errors reported through <paramref name="fail"/>).</summary>
    private static string? Check(FormQuestion q, IReadOnlyDictionary<string, string>? answers, string key, Action<string, string> fail)
    {
        var raw = answers is not null && answers.TryGetValue(q.Id, out var v) ? (v ?? "").Trim() : "";
        if (q.Type == QuestionType.Checkbox)
        {
            var on = raw is "true" or "on" or "1" or "Evet";
            if (q.Required && !on) fail(key, "Devam etmek için onaylayın.");
            return on ? "Evet" : q.Required ? null : "Hayır";
        }
        if (raw.Length == 0)
        {
            if (q.Required) fail(key, "Bu soruyu yanıtlayın.");
            return null;
        }
        switch (q.Type)
        {
            case QuestionType.Select or QuestionType.Radio when !q.Options.Contains(raw):
            case QuestionType.YesNo when raw is not ("Evet" or "Hayır"):
                fail(key, "Listeden bir seçenek seçin.");
                return null;
            case QuestionType.Number when !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                fail(key, "Bir sayı yazın.");
                return null;
            case QuestionType.Date when !DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                fail(key, "Geçerli bir tarih seçin.");
                return null;
        }
        return Cut(raw, q.Type == QuestionType.Textarea ? 2000 : 500);
    }

    private static string NewId(HashSet<string> taken)
    {
        string id;
        do id = "q" + Guid.NewGuid().ToString("N")[..8]; while (!taken.Add(id));
        return id;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
