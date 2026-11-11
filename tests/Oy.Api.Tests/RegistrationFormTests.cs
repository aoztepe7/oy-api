using Oy.Api.Domain;

namespace Oy.Api.Tests;

/// <summary>Question rules of the form engine (pre-registration and registration forms) and registration links.</summary>
public class RegistrationFormTests
{
    private static (List<EnrollmentAnswer> Answers, Dictionary<string, string> Errors, Dictionary<string, string> Values) Run(
        IEnumerable<FormQuestion> questions, Dictionary<string, string> answers)
    {
        var errors = new Dictionary<string, string>();
        var values = new Dictionary<string, string>();
        var result = EnrollmentForms.CheckAnswers(questions, answers, "answers.", values, (k, m) => errors.TryAdd(k, m));
        return (result, errors, values);
    }

    [Fact]
    public void Multi_select_keeps_option_order_and_checks_counts()
    {
        var q = new FormQuestion
        {
            Id = "art", Label = "İlgi alanları", Type = QuestionType.MultiSelect, Options = ["Resim", "Müzik", "Dans"],
            MinChoices = 2, MaxChoices = 2, AllowOther = true,
        };
        var ok = Run([q], new() { ["art"] = "Dans\nResim" });
        Assert.Empty(ok.Errors);
        Assert.Equal("Resim\nDans", ok.Values["art"]);
        Assert.Equal(new EnrollmentAnswer("İlgi alanları", "Resim, Dans"), ok.Answers.Single());

        Assert.Contains("answers.art", Run([q], new() { ["art"] = "Resim" }).Errors.Keys);
        Assert.Contains("answers.art", Run([q], new() { ["art"] = "Resim\nMüzik\nDans" }).Errors.Keys);
        Assert.Contains("answers.art", Run([q], new() { ["art"] = "Resim\nBale" }).Errors.Keys);

        var other = Run([q], new() { ["art"] = "Müzik\nDiğer: Satranç" });
        Assert.Empty(other.Errors);
        Assert.Equal("Müzik, Diğer: Satranç", other.Answers.Single().Value);
        Assert.Equal("“Diğer” için açıklama yazın.", Run([q], new() { ["art"] = "Müzik\nDiğer:" }).Errors["answers.art"]);
    }

    [Fact]
    public void Select_accepts_other_only_when_allowed()
    {
        var q = new FormQuestion { Id = "s", Label = "Okul", Type = QuestionType.Select, Options = ["A", "B"] };
        Assert.Contains("answers.s", Run([q], new() { ["s"] = "Diğer: C" }).Errors.Keys);
        q.AllowOther = true;
        Assert.Equal("Diğer: C", Run([q], new() { ["s"] = "Diğer: C" }).Values["s"]);
    }

    [Theory]
    [InlineData("12,5", null, "12,5 cm")]
    [InlineData("12.5", null, "12,5 cm")]
    [InlineData("120 cm", null, "120 cm")]
    [InlineData("abc", "Bir sayı yazın.", null)]
    [InlineData("4", "En az 5 olmalı.", null)]
    [InlineData("250,5", "En fazla 220 olmalı.", null)]
    public void Numbers_accept_comma_or_point_and_check_the_range(string input, string? error, string? display)
    {
        var q = new FormQuestion { Id = "h", Label = "Boy", Type = QuestionType.Number, Min = 5, Max = 220, Unit = "cm" };
        var r = Run([q], new() { ["h"] = input });
        if (error is null) Assert.Equal(display, r.Answers.Single().Value);
        else Assert.Equal(error, r.Errors["answers.h"]);
    }

    [Fact]
    public void Whole_numbers_only_when_asked()
    {
        var q = new FormQuestion { Id = "n", Label = "Kardeş sayısı", Type = QuestionType.Number, Integer = true };
        Assert.Equal("Küsuratsız bir sayı yazın.", Run([q], new() { ["n"] = "1,5" }).Errors["answers.n"]);
        Assert.Empty(Run([q], new() { ["n"] = "2" }).Errors);
    }

    [Theory]
    [InlineData(TextFormat.Digits, "12345", true)]
    [InlineData(TextFormat.Digits, "12a45", false)]
    [InlineData(TextFormat.NationalId, "10000000146", true)]
    [InlineData(TextFormat.NationalId, "10000000147", false)]
    [InlineData(TextFormat.NationalId, "01234567890", false)]
    [InlineData(TextFormat.Email, "veli@ornek.com", true)]
    [InlineData(TextFormat.Phone, "0542 543 19 93", true)]
    [InlineData(TextFormat.Phone, "12", false)]
    public void Text_formats(TextFormat format, string value, bool valid) =>
        Assert.Equal(valid, EnrollmentForms.FormatError(format, value) is null);

    [Fact]
    public void Text_length_and_date_range()
    {
        var text = new FormQuestion { Id = "t", Label = "Kısa", Type = QuestionType.Text, MaxLength = 3 };
        var date = new FormQuestion { Id = "d", Label = "Aşı", Type = QuestionType.Date, MinDate = new(2020, 1, 1), MaxDate = new(2026, 12, 31) };
        var r = Run([text, date], new() { ["t"] = "abcd", ["d"] = "2019-05-01" });
        Assert.Equal("En fazla 3 karakter yazabilirsiniz.", r.Errors["answers.t"]);
        Assert.Equal("Tarih 01.01.2020 ya da sonrası olmalı.", r.Errors["answers.d"]);
        Assert.Equal("05.03.2024", Run([date], new() { ["d"] = "2024-03-05" }).Answers.Single().Value);
    }

    [Fact]
    public void Conditional_questions_are_only_asked_when_the_condition_holds()
    {
        FormQuestion[] questions =
        [
            new() { Id = "sec", Label = "Sağlık", Type = QuestionType.Section },
            new() { Id = "allergy", Label = "Alerjisi var mı?", Type = QuestionType.YesNo, Required = true },
            new() { Id = "which", Label = "Hangi alerji?", Type = QuestionType.Text, Required = true, ShowIf = new() { QuestionId = "allergy", Values = ["Evet"] } },
        ];
        var no = Run(questions, new() { ["allergy"] = "Hayır", ["which"] = "ignored" });
        Assert.Empty(no.Errors);
        Assert.Equal([new("Alerjisi var mı?", "Hayır")], no.Answers);

        Assert.Contains("answers.which", Run(questions, new() { ["allergy"] = "Evet" }).Errors.Keys);
        Assert.Equal(2, Run(questions, new() { ["allergy"] = "Evet", ["which"] = "Fıstık" }).Answers.Count);
    }

    [Fact]
    public void Per_child_question_can_depend_on_a_family_answer()
    {
        var form = new EnrollmentFormConfig
        {
            ChildBirthDate = FieldMode.Hidden, ParentEmail = FieldMode.Hidden,
            Questions =
            [
                new() { Id = "bus", Label = "Servis kullanacak mı?", Type = QuestionType.YesNo },
                new() { Id = "stop", Label = "Durak", Type = QuestionType.Text, Required = true, PerChild = true, ShowIf = new() { QuestionId = "bus", Values = ["Evet"] } },
            ],
        };
        var (_, errors) = EnrollmentForms.Validate(form, [new SubmittedChild("Ada", null, new Dictionary<string, string>())],
            "0542 543 19 93", null, null, new Dictionary<string, string> { ["bus"] = "Evet" });
        Assert.Contains("children[0].answers.stop", errors.Keys);
    }

    [Fact]
    public void Sanitize_checks_conditions_and_rules()
    {
        var errors = new List<(string, string)>();
        var questions = EnrollmentForms.SanitizeQuestions(
        [
            new() { Id = "size", Label = "Beden", Type = QuestionType.Select, Options = ["S", "M"], PerChild = true },
            new() { Id = "fam", Label = "Aile sorusu", ShowIf = new() { QuestionId = "size", Values = ["S"] } },
            new() { Id = "later", Label = "Sonrakine bağlı", ShowIf = new() { QuestionId = "x", Values = ["S"] } },
            new() { Id = "bad id!", Label = "Sayı", Type = QuestionType.Number, Min = 10, Max = 5 },
            new() { Id = "m", Label = "Çoklu", Type = QuestionType.MultiSelect, Options = ["A"], AllowOther = true, MinChoices = 5, MaxChoices = 9 },
            new() { Label = "Bölüm", Type = QuestionType.Section, Required = true },
        ], "registrationForm", perChild: true, errors);

        Assert.Equal(6, questions.Count);
        Assert.Null(questions[1].ShowIf); // family question cannot depend on a child's answer
        Assert.Null(questions[2].ShowIf); // unknown / later question: dropped silently
        Assert.Matches("^q[0-9a-f]{8}$", questions[3].Id);
        Assert.Equal(2, questions[4].MinChoices); // clamped to the number of choices ("A" + "Diğer")
        Assert.Null(questions[4].MaxChoices);
        Assert.False(questions[5].Required);
        Assert.Equal(2, errors.Count); // family→child condition, min > max
    }

    [Fact]
    public void Registration_link_status_and_token()
    {
        var now = new DateTime(2026, 11, 11, 10, 0, 0, DateTimeKind.Utc);
        var program = new TrainingProgram
        {
            Title = "Orman Okulu", Slug = "orman",
            RegistrationForm = new() { Intro = "Merhaba", Questions = [new() { Id = "a", Label = "Alerji", Type = QuestionType.Text }] },
        };
        Assert.True(RegistrationLinks.HasForm(program));

        var r = RegistrationLinks.Issue(null, Guid.NewGuid(), program, now);
        Assert.True(RegistrationLinks.LooksLikeToken(r.Token));
        Assert.Equal(RegistrationStatus.Sent, r.StatusAt(now));
        r.OpenedAt = now;
        Assert.Equal(RegistrationStatus.Opened, r.StatusAt(now));
        Assert.Equal(RegistrationStatus.Expired, r.StatusAt(now.AddDays(31)));
        r.SubmittedAt = now;
        Assert.Equal(RegistrationStatus.Submitted, r.StatusAt(now.AddDays(31)));

        var token = r.Token;
        RegistrationLinks.Issue(r, r.EnrollmentId, program, now.AddDays(40));
        Assert.NotEqual(token, r.Token);
        Assert.Equal(RegistrationStatus.Sent, r.StatusAt(now.AddDays(40)));

        program.RegistrationForm.Questions = [new() { Id = "s", Label = "Bölüm", Type = QuestionType.Section }];
        Assert.False(RegistrationLinks.HasForm(program));
    }
}
