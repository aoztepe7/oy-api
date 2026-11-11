using System.Globalization;
using System.Text.RegularExpressions;

namespace Oy.Api.Domain;

/// <summary>How a built-in pre-registration field is asked.</summary>
public enum FieldMode { Hidden, Optional, Required }

/// <summary>
/// Answer type of a form question. Stored as a number inside the jsonb form definitions:
/// new values go at the end only.
/// </summary>
public enum QuestionType
{
    Text, Textarea, Select, Radio, Checkbox, Date, Number, YesNo,
    /// <summary>Several choices from the option list.</summary>
    MultiSelect,
    /// <summary>Not a question: a heading (label) with an optional text (hint) that splits the form.</summary>
    Section,
}

/// <summary>What a short text answer must look like.</summary>
public enum TextFormat { Any, Digits, Phone, Email, NationalId }

/// <summary>Shows a question only when an earlier choice question was answered with one of <see cref="Values"/>.</summary>
public class QuestionCondition
{
    public string QuestionId { get; set; } = "";
    public List<string> Values { get; set; } = [];
}

/// <summary>A custom question on a program's pre-registration or registration form.</summary>
public class FormQuestion
{
    /// <summary>Stable key of the question (answers are matched by it).</summary>
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public QuestionType Type { get; set; }
    public bool Required { get; set; }
    /// <summary>Asked once per child (e.g. allergies, T-shirt size) instead of once per family.</summary>
    public bool PerChild { get; set; }
    /// <summary>Choices of Select / Radio / MultiSelect questions.</summary>
    public List<string> Options { get; set; } = [];
    public string Hint { get; set; } = "";

    /// <summary>Choice questions: adds "Diğer" with a free text ("Diğer: …").</summary>
    public bool AllowOther { get; set; }
    /// <summary>MultiSelect: how many options may be ticked.</summary>
    public int? MinChoices { get; set; }
    public int? MaxChoices { get; set; }
    /// <summary>Text only.</summary>
    public TextFormat Format { get; set; }
    /// <summary>Text / Textarea: at most this many characters (500 / 2000 when empty).</summary>
    public int? MaxLength { get; set; }
    /// <summary>Number: allowed range, whole numbers only, unit shown next to the field ("cm").</summary>
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public bool Integer { get; set; }
    public string Unit { get; set; } = "";
    /// <summary>Date: allowed range.</summary>
    public DateOnly? MinDate { get; set; }
    public DateOnly? MaxDate { get; set; }
    public QuestionCondition? ShowIf { get; set; }
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
    public const int MaxQuestions = 50;
    public const int MaxOptions = 30;
    public const int MaxChildren = 6;
    public const int TextLimit = 500;
    public const int TextareaLimit = 2000;
    /// <summary>The extra choice of questions with <see cref="FormQuestion.AllowOther"/>; answered as "Diğer: …".</summary>
    public const string Other = "Diğer";
    public const string OtherPrefix = Other + ": ";
    /// <summary>MultiSelect answers are submitted one option per line.</summary>
    public const char MultiSeparator = '\n';

    private static readonly CultureInfo Tr = new("tr-TR");

    [GeneratedRegex(@"^[+\d][\d\s()-]{8,}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex IdRegex();

    public static bool IsChoice(QuestionType t) => t is QuestionType.Select or QuestionType.Radio or QuestionType.MultiSelect;

    /// <summary>Questions another question can be shown or hidden by.</summary>
    public static bool CanBeCondition(QuestionType t) => IsChoice(t) || t is QuestionType.YesNo or QuestionType.Checkbox;

    /// <summary>Cleans a form definition from the panel; returns error messages keyed by field.</summary>
    public static (EnrollmentFormConfig Config, List<(string Field, string Message)> Errors) Sanitize(EnrollmentFormConfig? input)
    {
        var errors = new List<(string, string)>();
        var c = input ?? new EnrollmentFormConfig();
        var questions = SanitizeQuestions(c.Questions, "enrollmentForm", perChild: true, errors);
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
    /// Cleans the questions of a form: drops empty rows, assigns missing ids, checks rules and conditions.
    /// <paramref name="perChild"/> false: the form is about one child, the per-child flag is cleared.
    /// </summary>
    public static List<FormQuestion> SanitizeQuestions(IEnumerable<FormQuestion>? input, string field, bool perChild, List<(string, string)> errors)
    {
        void Fail(string message) => errors.Add((field, message));
        var questions = new List<FormQuestion>();
        var ids = new HashSet<string>();
        foreach (var q in input ?? [])
        {
            if (q is null) continue;
            var label = (q.Label ?? "").Trim();
            if (label.Length == 0) continue; // empty rows from the editor
            var type = Enum.IsDefined(q.Type) ? q.Type : QuestionType.Text;
            var choice = IsChoice(type);
            var section = type == QuestionType.Section;
            var allowOther = choice && q.AllowOther;
            var options = choice
                ? (q.Options ?? []).Select(o => Cut((o ?? "").Trim(), 100)).Where(o => o.Length > 0 && !(allowOther && o == Other))
                    .Distinct().Take(MaxOptions).ToList()
                : [];
            if (choice && options.Count + (allowOther ? 1 : 0) < 2) Fail($"“{label}” sorusu için en az iki seçenek girin.");
            var id = (q.Id ?? "").Trim();
            if (!IdRegex().IsMatch(id) || !ids.Add(id)) id = NewId(ids);

            var clean = new FormQuestion
            {
                Id = id, Label = Cut(label, 200), Type = type, Required = !section && q.Required, PerChild = perChild && q.PerChild,
                Options = options, Hint = Cut((q.Hint ?? "").Trim(), section ? 1000 : 300), AllowOther = allowOther,
            };

            switch (type)
            {
                case QuestionType.MultiSelect:
                    var total = options.Count + (allowOther ? 1 : 0);
                    clean.MinChoices = q.MinChoices is > 0 ? Math.Min(q.MinChoices.Value, total) : null;
                    clean.MaxChoices = q.MaxChoices is > 0 && q.MaxChoices < total ? q.MaxChoices : null;
                    if (clean.MinChoices > clean.MaxChoices) Fail($"“{label}”: en az seçim sayısı en fazladan büyük olamaz.");
                    break;
                case QuestionType.Text:
                    clean.Format = Enum.IsDefined(q.Format) ? q.Format : TextFormat.Any;
                    clean.MaxLength = q.MaxLength is > 0 and < TextLimit ? q.MaxLength : null;
                    break;
                case QuestionType.Textarea:
                    clean.MaxLength = q.MaxLength is > 0 and < TextareaLimit ? q.MaxLength : null;
                    break;
                case QuestionType.Number:
                    clean.Min = q.Min;
                    clean.Max = q.Max;
                    clean.Integer = q.Integer;
                    clean.Unit = Cut((q.Unit ?? "").Trim(), 20);
                    if (q.Min > q.Max) Fail($"“{label}”: en küçük değer en büyükten büyük olamaz.");
                    break;
                case QuestionType.Date:
                    clean.MinDate = q.MinDate;
                    clean.MaxDate = q.MaxDate;
                    if (q.MinDate > q.MaxDate) Fail($"“{label}”: en erken tarih en geç tarihten sonra olamaz.");
                    break;
            }

            // A condition points at an earlier choice question; a family question cannot depend on a child's answer.
            // A condition on a deleted (or later) question is dropped.
            if (q.ShowIf is { } cond && questions.FirstOrDefault(x => x.Id == cond.QuestionId) is { } target)
            {
                var allowed = ConditionValues(target);
                var values = (cond.Values ?? []).Select(v => (v ?? "").Trim()).Where(allowed.Contains).Distinct().ToList();
                if (!CanBeCondition(target.Type)) Fail($"“{label}”: koşul yalnızca seçmeli bir soruya bağlanabilir.");
                else if (values.Count == 0) Fail($"“{label}”: koşul için en az bir cevap seçin.");
                else if (target.PerChild && !clean.PerChild) Fail($"“{label}”: aile sorusu, çocuk başına sorulan bir soruya bağlanamaz.");
                else clean.ShowIf = new QuestionCondition { QuestionId = target.Id, Values = values };
            }

            questions.Add(clean);
        }
        if (questions.Count > MaxQuestions) Fail($"En fazla {MaxQuestions} soru eklenebilir.");
        return questions;
    }

    /// <summary>Answers of <paramref name="q"/> a condition can match.</summary>
    public static List<string> ConditionValues(FormQuestion q) => q.Type switch
    {
        QuestionType.YesNo or QuestionType.Checkbox => ["Evet", "Hayır"],
        _ => q.AllowOther ? [.. q.Options, Other] : q.Options,
    };

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

        var familyValues = new Dictionary<string, string>();
        var family = CheckAnswers(form.Questions.Where(q => !q.PerChild), familyAnswers, "answers.", familyValues, Fail);

        var perChild = new List<List<EnrollmentAnswer>>();
        for (var i = 0; i < children.Count; i++)
        {
            var values = new Dictionary<string, string>(familyValues);
            var own = CheckAnswers(form.Questions.Where(q => q.PerChild), children[i].Answers, $"children[{i}].answers.", values, Fail);
            perChild.Add([.. family, .. own]);
        }
        return (perChild, errors);
    }

    /// <summary>
    /// Validates the answers to a list of questions (question id → submitted value). Questions whose condition is not met are
    /// skipped and their answers dropped. <paramref name="values"/> collects the normalised value of every answered question;
    /// it may be pre-filled with answers conditions refer to (family answers for a child). Error keys: <paramref name="keyPrefix"/> + id.
    /// </summary>
    public static List<EnrollmentAnswer> CheckAnswers(
        IEnumerable<FormQuestion> questions, IReadOnlyDictionary<string, string>? answers, string keyPrefix,
        Dictionary<string, string> values, Action<string, string> fail)
    {
        var result = new List<EnrollmentAnswer>();
        foreach (var q in questions)
        {
            if (q.Type == QuestionType.Section) continue;
            if (q.ShowIf is { } cond && !Matches(cond, values)) continue;
            var raw = answers is not null && answers.TryGetValue(q.Id, out var v) ? v ?? "" : "";
            var value = Check(q, raw, keyPrefix + q.Id, fail);
            if (value is null) continue;
            values[q.Id] = value;
            result.Add(new EnrollmentAnswer(q.Label, Display(q, value)));
        }
        return result;
    }

    /// <summary>Whether the question the condition points at was answered with one of its values.</summary>
    public static bool Matches(QuestionCondition cond, IReadOnlyDictionary<string, string> values) =>
        values.TryGetValue(cond.QuestionId, out var value)
        && value.Split(MultiSeparator).Any(part => cond.Values.Contains(part.StartsWith(OtherPrefix, StringComparison.Ordinal) ? Other : part));

    /// <summary>Normalised answer, or null when unanswered (errors reported through <paramref name="fail"/>).</summary>
    private static string? Check(FormQuestion q, string input, string key, Action<string, string> fail)
    {
        var raw = input.Trim();
        if (q.Type == QuestionType.Checkbox)
        {
            var on = raw is "true" or "on" or "1" or "Evet";
            if (q.Required && !on) fail(key, "Devam etmek için onaylayın.");
            return on ? "Evet" : q.Required ? null : "Hayır";
        }
        if (q.Type == QuestionType.MultiSelect) return CheckMulti(q, raw, key, fail);
        if (raw.Length == 0)
        {
            if (q.Required) fail(key, "Bu soruyu yanıtlayın.");
            return null;
        }
        switch (q.Type)
        {
            case QuestionType.Select or QuestionType.Radio:
                var choice = Choice(q, raw);
                if (choice is null) fail(key, IsOtherWithoutText(q, raw) ? "“Diğer” için açıklama yazın." : "Listeden bir seçenek seçin.");
                return choice;
            case QuestionType.YesNo:
                if (raw is "Evet" or "Hayır") return raw;
                fail(key, "Listeden bir seçenek seçin.");
                return null;
            case QuestionType.Number:
                return CheckNumber(q, raw, key, fail);
            case QuestionType.Date:
                if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    fail(key, "Geçerli bir tarih seçin.");
                    return null;
                }
                if (date < q.MinDate) { fail(key, $"Tarih {q.MinDate:dd.MM.yyyy} ya da sonrası olmalı."); return null; }
                if (date > q.MaxDate) { fail(key, $"Tarih {q.MaxDate:dd.MM.yyyy} ya da öncesi olmalı."); return null; }
                return raw;
            default:
                var limit = q.MaxLength ?? (q.Type == QuestionType.Textarea ? TextareaLimit : TextLimit);
                if (raw.Length > limit) { fail(key, $"En fazla {limit} karakter yazabilirsiniz."); return null; }
                var formatError = q.Type == QuestionType.Text ? FormatError(q.Format, raw) : null;
                if (formatError is not null) { fail(key, formatError); return null; }
                return raw;
        }
    }

    /// <summary>An option of the list, or "Diğer: …" when allowed; null when neither.</summary>
    private static string? Choice(FormQuestion q, string raw)
    {
        if (q.Options.Contains(raw)) return raw;
        if (!q.AllowOther || !raw.StartsWith(Other, StringComparison.Ordinal)) return null;
        var text = raw[Other.Length..].TrimStart().TrimStart(':').Trim();
        return text.Length == 0 ? null : OtherPrefix + Cut(text, 200);
    }

    private static bool IsOtherWithoutText(FormQuestion q, string raw) => q.AllowOther && raw.TrimEnd(':', ' ') == Other;

    private static string? CheckMulti(FormQuestion q, string raw, string key, Action<string, string> fail)
    {
        var picked = new List<string>();
        foreach (var part in raw.Split(MultiSeparator).Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            var choice = Choice(q, part);
            if (choice is null)
            {
                fail(key, IsOtherWithoutText(q, part) ? "“Diğer” için açıklama yazın." : "Listeden seçenek seçin.");
                return null;
            }
            if (!picked.Contains(choice)) picked.Add(choice);
        }
        if (picked.Count == 0)
        {
            if (q.Required || q.MinChoices > 0) fail(key, q.MinChoices > 1 ? $"En az {q.MinChoices} seçenek seçin." : "Bu soruyu yanıtlayın.");
            return null;
        }
        if (picked.Count < q.MinChoices) { fail(key, $"En az {q.MinChoices} seçenek seçin."); return null; }
        if (picked.Count > q.MaxChoices) { fail(key, $"En fazla {q.MaxChoices} seçenek seçebilirsiniz."); return null; }
        // Keep the order of the option list ("Diğer" last).
        return string.Join(MultiSeparator, picked.OrderBy(p => q.Options.IndexOf(p) is var i and >= 0 ? i : int.MaxValue));
    }

    private static string? CheckNumber(FormQuestion q, string raw, string key, Action<string, string> fail)
    {
        // "12,5" and "12.5" both mean twelve and a half; spaces and a typed unit are ignored.
        var text = raw.Replace(" ", "").Replace(',', '.');
        var unit = q.Unit.Replace(" ", "");
        if (unit.Length > 0 && text.EndsWith(unit, StringComparison.OrdinalIgnoreCase)) text = text[..^unit.Length];
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n))
        {
            fail(key, "Bir sayı yazın.");
            return null;
        }
        if (q.Integer && n != decimal.Truncate(n)) { fail(key, "Küsuratsız bir sayı yazın."); return null; }
        if (n < q.Min) { fail(key, $"En az {Number(q.Min.Value)} olmalı."); return null; }
        if (n > q.Max) { fail(key, $"En fazla {Number(q.Max.Value)} olmalı."); return null; }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Error message when a short text does not have the required format.</summary>
    public static string? FormatError(TextFormat format, string value) => format switch
    {
        TextFormat.Digits when !value.All(char.IsAsciiDigit) => "Yalnızca rakam yazın.",
        TextFormat.Phone when !PhoneRegex().IsMatch(value) => "Geçerli bir telefon numarası yazın.",
        TextFormat.Email when !EmailRegex().IsMatch(value) => "Geçerli bir e-posta adresi yazın.",
        TextFormat.NationalId when !IsNationalId(value) => "Geçerli bir T.C. kimlik numarası yazın.",
        _ => null,
    };

    /// <summary>T.C. kimlik no: 11 digits, not starting with 0, with the two check digits.</summary>
    public static bool IsNationalId(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit) || value[0] == '0') return false;
        var d = value.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7 - even) % 10 + 10) % 10;
        return d[9] == tenth && d[10] == d.Take(10).Sum() % 10;
    }

    /// <summary>How a normalised answer is shown in the panel ("12,5 cm", "Resim, Müzik", "05.03.2024").</summary>
    public static string Display(FormQuestion q, string value) => q.Type switch
    {
        QuestionType.Number when decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) =>
            q.Unit.Length > 0 ? $"{Number(n)} {q.Unit}" : Number(n),
        QuestionType.MultiSelect => value.Replace(MultiSeparator.ToString(), ", "),
        QuestionType.Date when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) =>
            d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
        _ => value,
    };

    private static string Number(decimal n) => n.ToString("0.##", Tr);

    private static string NewId(HashSet<string> taken)
    {
        string id;
        do id = "q" + Guid.NewGuid().ToString("N")[..8]; while (!taken.Add(id));
        return id;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max];
}
