namespace Oy.Api.Domain;

/// <summary>What a notification is about; the app shows its icon and the account settings can mute a kind.</summary>
public enum NotificationKind
{
    // Guardians
    FeedPost,
    Photos,
    PaymentDue,
    PaymentOverdue,
    InvoiceIssued,

    // Staff
    TodoAssigned,
    TodoComment,
    TodoDue,
    PreRegistration,
    RegistrationSubmitted,
    ContactMessage,
    PaymentDigest,
}

/// <summary>
/// One entry of a user's notification inbox (the bell in the panel / app). Each one is also sent as a push
/// notification to the user's signed-in app devices.
/// </summary>
public class Notification : Entity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public NotificationKind Kind { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>Panel path opened on tap, e.g. "/yapilacaklar?is=…".</summary>
    public string Url { get; set; } = "";
    /// <summary>
    /// Scheduled reminders: "due:{enrollment}:{period}:overdue" etc. A user gets each key once, so a reminder job that
    /// runs again (restart, second instance) sends nothing twice. Empty for event notifications.
    /// </summary>
    public string Key { get; set; } = "";
    public DateTime? ReadAt { get; set; }
}

/// <summary>Kinds a user turned off (no inbox entry, no push).</summary>
public class NotificationMute
{
    public Guid UserId { get; set; }
    public NotificationKind Kind { get; set; }
}

public static class NotificationKinds
{
    public static readonly IReadOnlyList<NotificationKind> Guardian =
        [NotificationKind.FeedPost, NotificationKind.Photos, NotificationKind.PaymentDue, NotificationKind.PaymentOverdue, NotificationKind.InvoiceIssued];

    public static readonly IReadOnlyList<NotificationKind> Staff =
    [
        NotificationKind.TodoAssigned, NotificationKind.TodoComment, NotificationKind.TodoDue, NotificationKind.PreRegistration,
        NotificationKind.RegistrationSubmitted, NotificationKind.ContactMessage, NotificationKind.PaymentDigest,
    ];

    public static string Label(NotificationKind kind) => kind switch
    {
        NotificationKind.FeedPost => "Yeni duyurular",
        NotificationKind.Photos => "Çocuğunuzun yeni fotoğrafları",
        NotificationKind.PaymentDue => "Yaklaşan ödeme hatırlatması",
        NotificationKind.PaymentOverdue => "Gecikmiş ödeme hatırlatması",
        NotificationKind.InvoiceIssued => "Kesilen faturalar",
        NotificationKind.TodoAssigned => "Bana atanan işler",
        NotificationKind.TodoComment => "İşlerime yazılan yorumlar",
        NotificationKind.TodoDue => "Bugün son günü olan işler",
        NotificationKind.PreRegistration => "Yeni ön kayıtlar",
        NotificationKind.RegistrationSubmitted => "Doldurulan esas kayıt formları",
        NotificationKind.ContactMessage => "İletişim formu mesajları",
        NotificationKind.PaymentDigest => "Sabah ödeme özeti",
        _ => kind.ToString(),
    };
}
