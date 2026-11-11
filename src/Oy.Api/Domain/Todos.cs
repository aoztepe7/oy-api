namespace Oy.Api.Domain;

/// <summary>Who sees a to-do. The creator always does.</summary>
public enum TodoVisibility
{
    /// <summary>Only the creator.</summary>
    Private,
    /// <summary>Every panel user.</summary>
    Everyone,
    /// <summary>The creator and the assigned users.</summary>
    Assigned,
}

public enum TodoPriority { Normal, High }

/// <summary>How a to-do comes back once completed (needs a due date).</summary>
public enum TodoRepeat
{
    None,
    Daily,
    /// <summary>Monday to Friday.</summary>
    Weekdays,
    /// <summary>Same weekday as the due date.</summary>
    Weekly,
    /// <summary>Same day of the month as the first due date (the last day in shorter months).</summary>
    Monthly,
}

/// <summary>A to-do of panel (staff) users; guardians never see these.</summary>
public class TodoItem : Entity
{
    public required string Title { get; set; }
    public string Notes { get; set; } = "";
    public TodoVisibility Visibility { get; set; }
    public TodoPriority Priority { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>null once the creator's account is deleted (shared to-dos stay).</summary>
    public Guid? CreatedById { get; set; }
    public User? CreatedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedById { get; set; }
    public User? CompletedBy { get; set; }
    public List<TodoAssignee> Assignees { get; set; } = [];
    public List<TodoComment> Comments { get; set; } = [];

    public TodoRepeat Repeat { get; set; }
    /// <summary>Monthly repeats: the day of the month the series started on (so 31 → 28 → 31 does not drift).</summary>
    public int RepeatDay { get; set; }
    /// <summary>The next to-do created when this one was completed (removed again when it is reopened).</summary>
    public Guid? NextId { get; set; }

    /// <summary>
    /// Due date of the next to-do of a repeating series: one step after <paramref name="due"/>, moved on until it is not in the past.
    /// </summary>
    public static DateOnly NextDue(TodoRepeat repeat, DateOnly due, int repeatDay, DateOnly today)
    {
        DateOnly Step(DateOnly d) => repeat switch
        {
            TodoRepeat.Daily => d.AddDays(1),
            TodoRepeat.Weekdays => d.DayOfWeek switch
            {
                DayOfWeek.Friday => d.AddDays(3),
                DayOfWeek.Saturday => d.AddDays(2),
                _ => d.AddDays(1),
            },
            TodoRepeat.Weekly => d.AddDays(7),
            TodoRepeat.Monthly => Billing.DayOf(new DateOnly(d.Year, d.Month, 1).AddMonths(1), repeatDay > 0 ? repeatDay : d.Day),
            _ => d,
        };
        if (repeat == TodoRepeat.None) return due;
        var next = Step(due);
        for (var i = 0; next < today && i < 1000; i++) next = Step(next);
        return next;
    }
}

/// <summary>A comment on a to-do, visible to everyone who sees the to-do.</summary>
public class TodoComment : Entity
{
    public Guid TodoId { get; set; }
    public TodoItem Todo { get; set; } = null!;
    /// <summary>null once the author's account is deleted.</summary>
    public Guid? AuthorId { get; set; }
    public User? Author { get; set; }
    public required string Text { get; set; }
}

public class TodoAssignee
{
    public Guid TodoId { get; set; }
    public TodoItem Todo { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
