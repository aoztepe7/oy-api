using Oy.Api.Features.Classroom;

namespace Oy.Api.Tests;

public class LessonSummaryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void Completing_requires_a_summary(string? summary)
    {
        var (_, errors) = ClassroomEndpoints.ValidateSummary(summary);
        Assert.True(errors.Any);
    }

    [Fact]
    public void Saving_a_draft_may_clear_the_summary()
    {
        var (text, errors) = ClassroomEndpoints.ValidateSummary("  ", required: false);
        Assert.False(errors.Any);
        Assert.Equal("", text);
    }

    [Fact]
    public void Summary_is_trimmed_and_length_limited()
    {
        var (text, errors) = ClassroomEndpoints.ValidateSummary("  Orman yürüyüşü yaptık.  ");
        Assert.False(errors.Any);
        Assert.Equal("Orman yürüyüşü yaptık.", text);

        var (_, tooLong) = ClassroomEndpoints.ValidateSummary(new string('a', ClassroomEndpoints.SummaryMaxLength + 1));
        Assert.True(tooLong.Any);
    }
}
