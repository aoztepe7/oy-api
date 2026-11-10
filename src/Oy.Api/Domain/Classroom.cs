namespace Oy.Api.Domain;

/// <summary>
/// One child's attendance for one lesson day. No row = not marked yet.
/// <see cref="AttendanceStatus.Present"/> and <see cref="AttendanceStatus.Late"/> put the child in the session's live pool.
/// </summary>
public class Attendance : Entity
{
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public AttendanceStatus Status { get; set; }
    /// <summary>When the child was marked as arrived (Present / Late); null when absent.</summary>
    public DateTime? ArrivedAt { get; set; }
    public Guid? MarkedByUserId { get; set; }
    /// <summary>Name snapshot of who marked it, so it survives user deletion.</summary>
    public string MarkedByName { get; set; } = "";

    public bool InPool => Status is AttendanceStatus.Present or AttendanceStatus.Late;
}

/// <summary>
/// A quick observation a teacher writes about a child during a lesson ("gözlem notu"). Visible to every teacher
/// on duty in that program; the raw material for the parent reports of later phases.
/// </summary>
public class ObservationNote : Entity
{
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public Guid SessionId { get; set; }
    public Session Session { get; set; } = null!;
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public Guid? AuthorUserId { get; set; }
    /// <summary>Name snapshot of the author, so it survives user deletion.</summary>
    public string AuthorName { get; set; } = "";
    public required string Text { get; set; }
    /// <summary>Development areas, e.g. "Sosyal", "Motor" — used to group notes in reports.</summary>
    public List<string> Tags { get; set; } = [];
}
