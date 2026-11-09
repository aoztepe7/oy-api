using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class EnrollmentFormTests
{
    private static readonly Dictionary<string, string> None = [];

    private static SubmittedChild Child(string name = "Ada Yılmaz", DateOnly? birth = null, Dictionary<string, string>? answers = null) =>
        new(name, birth ?? new DateOnly(2019, 5, 1), answers ?? None);

    [Fact]
    public void Default_form_requires_birth_date_phone_and_email()
    {
        var (_, errors) = EnrollmentForms.Validate(new EnrollmentFormConfig(), [new SubmittedChild("Ada", null, None)], "", "", null, null);
        Assert.Contains("children[0].birthDate", errors.Keys);
        Assert.Contains("parentPhone", errors.Keys);
        Assert.Contains("parentEmail", errors.Keys);
    }

    [Fact]
    public void Hidden_and_optional_fields_are_not_required()
    {
        var form = new EnrollmentFormConfig { ChildBirthDate = FieldMode.Hidden, ParentEmail = FieldMode.Optional };
        var (_, errors) = EnrollmentForms.Validate(form, [new SubmittedChild("Ada", null, None)], "0542 543 19 93", "", null, null);
        Assert.Empty(errors);
    }

    [Fact]
    public void Family_answers_go_to_every_child_and_per_child_answers_to_their_child()
    {
        var form = new EnrollmentFormConfig
        {
            Questions =
            [
                new() { Id = "how", Label = "Bizi nereden duydunuz?", Type = QuestionType.Select, Options = ["Instagram", "Arkadaş"] },
                new() { Id = "size", Label = "Tişört bedeni", Type = QuestionType.Radio, Options = ["S", "M"], Required = true, PerChild = true },
            ],
        };
        var children = new[]
        {
            Child("Ada", answers: new() { ["size"] = "S" }),
            Child("Can", answers: new() { ["size"] = "M" }),
        };
        var (answers, errors) = EnrollmentForms.Validate(form, children, "0542 543 19 93", "a@b.co", null, new Dictionary<string, string> { ["how"] = "Instagram" });

        Assert.Empty(errors);
        Assert.Equal([new("Bizi nereden duydunuz?", "Instagram"), new("Tişört bedeni", "S")], answers[0]);
        Assert.Equal([new("Bizi nereden duydunuz?", "Instagram"), new("Tişört bedeni", "M")], answers[1]);
    }

    [Fact]
    public void Invalid_choices_and_missing_required_answers_are_reported()
    {
        var form = new EnrollmentFormConfig
        {
            Questions =
            [
                new() { Id = "size", Label = "Beden", Type = QuestionType.Select, Options = ["S", "M"], PerChild = true },
                new() { Id = "kvkk", Label = "KVKK metnini okudum", Type = QuestionType.Checkbox, Required = true },
            ],
        };
        var (_, errors) = EnrollmentForms.Validate(form, [Child(answers: new() { ["size"] = "XXL" })], "0542 543 19 93", "a@b.co", null, None);
        Assert.Contains("children[0].answers.size", errors.Keys);
        Assert.Contains("answers.kvkk", errors.Keys);
    }

    [Fact]
    public void Siblings_can_be_turned_off()
    {
        var form = new EnrollmentFormConfig { AllowSiblings = false };
        var (_, errors) = EnrollmentForms.Validate(form, [Child("Ada"), Child("Can")], "0542 543 19 93", "a@b.co", null, None);
        Assert.Contains("children", errors.Keys);
    }

    [Fact]
    public void Sanitize_drops_empty_rows_assigns_ids_and_checks_choices()
    {
        var (config, errors) = EnrollmentForms.Sanitize(new EnrollmentFormConfig
        {
            ParentPhone = FieldMode.Hidden,
            ParentEmail = FieldMode.Hidden,
            Questions =
            [
                new() { Label = "  " },
                new() { Label = "Beden", Type = QuestionType.Select, Options = ["S", " ", "S"] },
                new() { Label = "Alerji", Type = QuestionType.Textarea, Options = ["ignored"] },
            ],
        });
        Assert.Equal(2, config.Questions.Count);
        Assert.All(config.Questions, q => Assert.False(string.IsNullOrEmpty(q.Id)));
        Assert.Empty(config.Questions[1].Options);
        Assert.Equal(2, errors.Count); // select needs two options; phone or e-mail must stay
    }
}
