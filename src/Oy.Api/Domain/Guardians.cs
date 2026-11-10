namespace Oy.Api.Domain;

/// <summary>Staff logins use the panel; guardian (veli) logins see only their children's feed and photos.</summary>
public enum UserKind { Staff, Guardian }

public enum GuardianRelation { Mother, Father, Other }

/// <summary>
/// A guardian login linked to an enrollment (a child in a program). One guardian can follow several children
/// (siblings), one child can have several guardians (mother and father).
/// </summary>
public class GuardianLink : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public GuardianRelation Relation { get; set; }
}

/// <summary>
/// A file that must not be public (children's photos, feed images). Stored outside the public media folder and
/// served only through short-lived signed links handed out to users allowed to see it.
/// </summary>
public class PrivateFile : Entity
{
    /// <summary>"akis" — the feed's image folder; "ders" — lesson photos; "cocuk" — a child's photos outside lessons.</summary>
    public string Folder { get; set; } = PrivateFolders.Feed;
    /// <summary>The uploader's file name, for the folder view.</summary>
    public string OriginalName { get; set; } = "";
    public required string StoredName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Guid? UploadedByUserId { get; set; }
}

/// <summary>A post in the guardians' feed, written in the panel. Visible once published.</summary>
public class FeedPost : Entity
{
    public string Text { get; set; } = "";
    /// <summary>Images (PrivateFile ids), in display order.</summary>
    public List<Guid> ImageIds { get; set; } = [];
    /// <summary>null = every guardian; otherwise only guardians with a child in this program.</summary>
    public Guid? ProgramId { get; set; }
    public TrainingProgram? Program { get; set; }
    public Guid? AuthorUserId { get; set; }
    public string AuthorName { get; set; } = "";
    public DateTime? PublishedAt { get; set; }
}

public static class PrivateFolders
{
    public const string Feed = "akis";
    public const string Lesson = "ders";
    public const string Child = "cocuk";
}

/// <summary>
/// A child's photo. Usually taken in a lesson (teachers tag the children in it); it can also be added to one
/// child outside lessons (an older photo) — then it has no session, only the date it was taken. Once shared,
/// the tagged children's guardians see it in their feed and in the child's gallery.
/// </summary>
public class ClassPhoto : Entity
{
    /// <summary>null = added to a child outside lessons.</summary>
    public Guid? SessionId { get; set; }
    public Session? Session { get; set; }
    /// <summary>When the photo was taken, for photos outside lessons (lesson photos use the lesson date).</summary>
    public DateOnly? TakenOn { get; set; }
    public Guid ProgramId { get; set; }
    public TrainingProgram Program { get; set; } = null!;
    public Guid FileId { get; set; }
    public PrivateFile File { get; set; } = null!;
    public string Caption { get; set; } = "";
    public Guid? UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = "";
    /// <summary>Set when shared; photos shared together carry the same timestamp (one feed item per child).</summary>
    public DateTime? PublishedAt { get; set; }
    public List<ClassPhotoTag> Tags { get; set; } = [];
}

public class ClassPhotoTag
{
    public Guid PhotoId { get; set; }
    public ClassPhoto Photo { get; set; } = null!;
    public Guid EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
}
