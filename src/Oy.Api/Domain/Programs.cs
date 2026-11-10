namespace Oy.Api.Domain;

/// <summary>An education group, camp, event or workshop ("eğitim").</summary>
public class TrainingProgram : Entity
{
    public required string Title { get; set; }
    public required string Slug { get; set; }
    public ProgramType Type { get; set; }
    public int AgeMin { get; set; }
    public int AgeMax { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>ISO weekdays, 1 = Monday … 7 = Sunday.</summary>
    public int[] DaysOfWeek { get; set; } = [];
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int Capacity { get; set; }
    /// <summary>Base price; with <see cref="PriceOptions"/> it is the lowest package price ("… den başlayan").</summary>
    public decimal Price { get; set; }
    public PricePeriod PricePeriod { get; set; }
    /// <summary>Optional price table, e.g. "1 Yetişkin + 1 Çocuk: 20.000", add-ons "İlave çocuk: +3.000".</summary>
    public List<PriceOption> PriceOptions { get; set; } = [];
    /// <summary>Whether the site shows the pre-registration form (otherwise a contact box).</summary>
    public bool EnrollmentEnabled { get; set; } = true;
    public EnrollmentFormConfig EnrollmentForm { get; set; } = new();
    public string Summary { get; set; } = "";
    public string Description { get; set; } = "";
    public string CoverImageUrl { get; set; } = "";
    public string Location { get; set; } = "";
    public bool IsPublished { get; set; }
    /// <summary>Position in the home page slider (1 = first); null = not featured.</summary>
    public int? FeaturedOrder { get; set; }

    public List<Session> Sessions { get; set; } = [];
    public List<StaffAssignment> Assignments { get; set; } = [];
    public List<Enrollment> Enrollments { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];

    /// <summary>All lesson dates produced by the schedule.</summary>
    public IEnumerable<DateOnly> ScheduledDates()
    {
        var days = DaysOfWeek.ToHashSet();
        for (var d = StartDate; d <= EndDate; d = d.AddDays(1))
        {
            if (days.Contains(d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek))
                yield return d;
        }
    }
}

/// <summary>One row of a program's price table. Add-ons are extras on top of a package ("+3.000").</summary>
public record PriceOption(string Label, decimal Price, string Note = "", bool IsAddon = false);

/// <summary>A single lesson day of a program.</summary>
public class Session : Entity
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsCancelled { get; set; }
    public string Note { get; set; } = "";
    /// <summary>Optional special theme for this lesson (e.g. a holiday theme).</summary>
    public Guid? ThemeId { get; set; }
    public Theme? Theme { get; set; }
    /// <summary>Extra lesson outside the weekly schedule; survives schedule re-sync.</summary>
    public bool IsMakeup { get; set; }
    /// <summary>The cancelled session this make-up lesson replaces.</summary>
    public Guid? MakeupForSessionId { get; set; }
    public Session? MakeupFor { get; set; }

    /// <summary>When a teacher started the lesson (attendance opens); null = not started.</summary>
    public DateTime? StartedAt { get; set; }
    public string StartedByName { get; set; } = "";
    /// <summary>When the lesson day was closed; attendance and notes are read-only until it is reopened.</summary>
    public DateTime? EndedAt { get; set; }

    public List<Attendance> Attendance { get; set; } = [];
    public List<ObservationNote> Notes { get; set; } = [];
}

public class Expense : Entity
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public DateOnly Date { get; set; }
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
}
