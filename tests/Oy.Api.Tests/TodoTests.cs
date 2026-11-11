using Oy.Api.Domain;

namespace Oy.Api.Tests;

public class TodoTests
{
    private static readonly DateOnly Today = new(2026, 11, 11); // Wednesday

    [Theory]
    [InlineData(TodoRepeat.Daily, "2026-11-11", "2026-11-12")]
    [InlineData(TodoRepeat.Daily, "2026-11-05", "2026-11-11")]      // overdue: catches up to today
    [InlineData(TodoRepeat.Weekdays, "2026-11-13", "2026-11-16")]   // Friday → Monday
    [InlineData(TodoRepeat.Weekly, "2026-11-09", "2026-11-16")]
    [InlineData(TodoRepeat.Weekly, "2026-10-26", "2026-11-16")]     // two weeks late: next Monday
    [InlineData(TodoRepeat.Monthly, "2026-11-30", "2026-12-30")]
    public void Next_due_date(TodoRepeat repeat, string due, string expected) =>
        Assert.Equal(DateOnly.Parse(expected), TodoItem.NextDue(repeat, DateOnly.Parse(due), DateOnly.Parse(due).Day, Today));

    [Fact]
    public void Monthly_repeat_keeps_the_day_of_the_series()
    {
        // 31 January → 28 February → 31 March (not 28 March).
        var feb = TodoItem.NextDue(TodoRepeat.Monthly, new DateOnly(2027, 1, 31), 31, new DateOnly(2027, 1, 1));
        Assert.Equal(new DateOnly(2027, 2, 28), feb);
        Assert.Equal(new DateOnly(2027, 3, 31), TodoItem.NextDue(TodoRepeat.Monthly, feb, 31, new DateOnly(2027, 1, 1)));
    }
}
