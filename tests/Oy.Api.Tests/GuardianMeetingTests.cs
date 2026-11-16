using System.Security.Claims;
using Oy.Api.Domain;
using Oy.Api.Features.Classroom;
using Oy.Api.Features.Meetings;
using Oy.Api.Infrastructure.Auth;

namespace Oy.Api.Tests;

public class GuardianMeetingTests
{
    private static GuardianMeeting Empty() => new() { Text = "" };
    private static MeetingInput Input(string? text, DateOnly? date, string? with = null) =>
        new(Guid.NewGuid(), date, MeetingChannel.Phone, with, text);

    [Fact]
    public void Text_and_date_are_required()
    {
        Assert.NotNull(MeetingEndpoints.Apply(Empty(), Input("  ", ClassroomEndpoints.Today())));
        Assert.NotNull(MeetingEndpoints.Apply(Empty(), Input("Konuştuk.", null)));
    }

    [Fact]
    public void Date_cannot_be_in_the_future()
    {
        Assert.NotNull(MeetingEndpoints.Apply(Empty(), Input("Konuştuk.", ClassroomEndpoints.Today().AddDays(1))));
    }

    [Fact]
    public void Valid_input_is_trimmed_and_copied()
    {
        var m = Empty();
        var day = ClassroomEndpoints.Today().AddDays(-3);
        Assert.Null(MeetingEndpoints.Apply(m, Input("  Uyku düzeni konuşuldu.  ", day, " Annesi ")));
        Assert.Equal("Uyku düzeni konuşuldu.", m.Text);
        Assert.Equal("Annesi", m.With);
        Assert.Equal(day, m.Date);
        Assert.Equal(MeetingChannel.Phone, m.Channel);
    }

    [Fact]
    public void Text_is_length_limited()
    {
        Assert.NotNull(MeetingEndpoints.Apply(Empty(), Input(new string('a', MeetingRules.TextMax + 1), ClassroomEndpoints.Today())));
    }

    [Fact]
    public void Only_the_author_or_an_enrollment_editor_may_edit()
    {
        var author = Guid.NewGuid();
        ClaimsPrincipal User(Guid id, params string[] perms) => new(new ClaimsIdentity(
            perms.Select(p => new Claim("perm", p)).Prepend(new Claim("sub", id.ToString()))));
        Assert.True(MeetingEndpoints.CanEdit(author, User(author)));
        Assert.False(MeetingEndpoints.CanEdit(author, User(Guid.NewGuid())));
        Assert.False(MeetingEndpoints.CanEdit(null, User(Guid.NewGuid())));
        Assert.True(MeetingEndpoints.CanEdit(author, User(Guid.NewGuid(), Permissions.EnrollmentsWrite)));
    }
}
