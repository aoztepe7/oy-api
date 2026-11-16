namespace Oy.Api.Domain;

public enum MeetingChannel { FaceToFace, Phone, Online, Message }

/// <summary>
/// A note of a meeting with a child's parents ("veli görüşmesi"), outside lessons: what was talked about, what was
/// agreed. Internal to the staff: every panel user can read it, guardians never see it.
/// </summary>
public class GuardianMeeting : Entity
{
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    /// <summary>The day the meeting took place (may be earlier than the note).</summary>
    public DateOnly Date { get; set; }
    public MeetingChannel Channel { get; set; }
    /// <summary>Who the staff met, e.g. "Annesi Ayşe Hanım"; empty = the enrollment's parent.</summary>
    public string With { get; set; } = "";
    public required string Text { get; set; }
    public Guid? AuthorUserId { get; set; }
    /// <summary>Name snapshot of the author, so it survives user deletion.</summary>
    public string AuthorName { get; set; } = "";
}

public static class MeetingRules
{
    public const int TextMax = 4000;
    public const int WithMax = 200;
}
