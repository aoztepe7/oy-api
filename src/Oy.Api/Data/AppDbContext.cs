using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Oy.Api.Domain;

namespace Oy.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<TrainingProgram> Programs => Set<TrainingProgram>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<StaffAssignment> Assignments => Set<StaffAssignment>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<RegistrationRequest> Registrations => Set<RegistrationRequest>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<Theme> Themes => Set<Theme>();
    public DbSet<SpecialDay> SpecialDays => Set<SpecialDay>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<IntegrationConfig> Integrations => Set<IntegrationConfig>();
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();
    public DbSet<Obligation> Obligations => Set<Obligation>();
    public DbSet<DueAdjustment> DueAdjustments => Set<DueAdjustment>();
    public DbSet<TodoItem> Todos => Set<TodoItem>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<TodoAssignee> TodoAssignees => Set<TodoAssignee>();
    public DbSet<TodoComment> TodoComments => Set<TodoComment>();
    public DbSet<ObligationPayment> ObligationPayments => Set<ObligationPayment>();
    public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<ObservationNote> ObservationNotes => Set<ObservationNote>();
    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();
    public DbSet<PrivateFile> PrivateFiles => Set<PrivateFile>();
    public DbSet<FeedPost> FeedPosts => Set<FeedPost>();
    public DbSet<ClassPhoto> ClassPhotos => Set<ClassPhoto>();
    public DbSet<ClassPhotoTag> ClassPhotoTags => Set<ClassPhotoTag>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Enums stored as readable strings.
        b.Properties<ProgramType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<PricePeriod>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<EmploymentType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<AssignmentRole>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<EnrollmentStatus>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<InvoiceStatus>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<ThemeKind>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<SpecialDayKind>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<IntegrationType>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<AttendanceStatus>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<UserKind>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<GuardianRelation>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<ObligationCategory>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<TodoVisibility>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<TodoPriority>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<TodoRepeat>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<DevicePlatform>().HaveConversion<string>().HaveMaxLength(32);
        b.Properties<decimal>().HavePrecision(14, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasOne(x => x.Role).WithMany(x => x.Users).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Staff).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.StaffId).IsUnique();
        });
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();

        b.Entity<TrainingProgram>(e =>
        {
            e.ToTable("Programs");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.IsPublished, x.EndDate });
            Json(e.Property(x => x.PriceOptions));
            Json(e.Property(x => x.EnrollmentForm));
            Json(e.Property(x => x.RegistrationForm));
        });
        b.Entity<Session>(e =>
        {
            e.HasIndex(x => new { x.ProgramId, x.Date }).IsUnique();
            e.HasIndex(x => x.Date);
            e.HasOne(x => x.Theme).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.MakeupFor).WithMany().HasForeignKey(x => x.MakeupForSessionId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Expense>().HasIndex(x => new { x.ProgramId, x.Date });
        b.Entity<StaffAssignment>().HasOne(x => x.Staff).WithMany(x => x.Assignments).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Enrollment>(e =>
        {
            e.HasIndex(x => new { x.ProgramId, x.Status });
            Json(e.Property(x => x.Answers));
            Json(e.Property(x => x.Billing));
        });
        b.Entity<RegistrationRequest>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(64);
            e.HasIndex(x => x.Token).IsUnique();
            e.HasIndex(x => x.EnrollmentId).IsUnique();
            e.HasOne(x => x.Enrollment).WithOne(x => x.Registration).HasForeignKey<RegistrationRequest>(x => x.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
            Json(e.Property(x => x.Questions));
            Json(e.Property(x => x.Values));
            Json(e.Property(x => x.Answers));
        });
        // Attendance and notes go with their session (and so with a deleted program). Deleting an enrollment
        // that has any is refused by the endpoints; NoAction (checked at statement end) lets a program cascade.
        b.Entity<Attendance>(e =>
        {
            e.HasIndex(x => new { x.SessionId, x.EnrollmentId }).IsUnique();
            e.HasOne(x => x.Session).WithMany(x => x.Attendance).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Enrollment).WithMany().OnDelete(DeleteBehavior.NoAction);
        });
        b.Entity<ObservationNote>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.ProgramId, x.EnrollmentId });
            e.HasIndex(x => x.SessionId);
            e.HasOne(x => x.Program).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Session).WithMany(x => x.Notes).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Enrollment).WithMany().OnDelete(DeleteBehavior.NoAction);
            Json(e.Property(x => x.Tags));
        });
        b.Entity<GuardianLink>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.EnrollmentId }).IsUnique();
            e.HasOne(x => x.User).WithMany(x => x.GuardianLinks).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Enrollment).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PrivateFile>(e =>
        {
            e.Property(x => x.StoredName).HasMaxLength(100);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.Property(x => x.Folder).HasMaxLength(20);
            e.Property(x => x.OriginalName).HasMaxLength(255);
            e.HasIndex(x => new { x.Folder, x.CreatedAt });
        });
        b.Entity<MediaAsset>(e =>
        {
            e.Property(x => x.Url).HasMaxLength(300);
            e.Property(x => x.Folder).HasMaxLength(100);
            e.Property(x => x.OriginalName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.HasIndex(x => x.Url).IsUnique();
            e.HasIndex(x => new { x.Folder, x.CreatedAt });
        });
        b.Entity<FeedPost>(e =>
        {
            e.HasIndex(x => x.PublishedAt);
            // A post for one program goes with it (it must never fall back to "everyone").
            e.HasOne(x => x.Program).WithMany().OnDelete(DeleteBehavior.Cascade);
            Json(e.Property(x => x.ImageIds));
        });
        b.Entity<ClassPhoto>(e =>
        {
            e.HasIndex(x => x.SessionId);
            e.HasIndex(x => x.PublishedAt);
            e.Property(x => x.Caption).HasMaxLength(300);
            e.HasOne(x => x.Session).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Program).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.File).WithMany().OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ClassPhotoTag>(e =>
        {
            e.HasKey(x => new { x.PhotoId, x.EnrollmentId });
            e.HasIndex(x => x.EnrollmentId);
            e.HasOne(x => x.Photo).WithMany(x => x.Tags).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Enrollment).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Payment>().HasOne(x => x.Invoice).WithOne(x => x.Payment).HasForeignKey<Invoice>(x => x.PaymentId);
        b.Entity<Invoice>().HasIndex(x => x.Status);
        b.Entity<Payment>().HasIndex(x => new { x.EnrollmentId, x.Period });
        b.Entity<UserSession>(e =>
        {
            e.ToTable("UserSessions");
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.Property(x => x.PreviousTokenHash).HasMaxLength(64);
            e.Property(x => x.DeviceName).HasMaxLength(100);
            e.Property(x => x.AppVersion).HasMaxLength(30);
            e.Property(x => x.PushToken).HasMaxLength(512);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.PreviousTokenHash);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasIndex(x => x.PushToken);
            e.HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TodoItem>(e =>
        {
            e.ToTable("Todos");
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(4000);
            e.HasIndex(x => new { x.CompletedAt, x.DueDate });
            e.HasIndex(x => x.CreatedById);
            // Shared to-dos outlive their creator's account.
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CompletedBy).WithMany().HasForeignKey(x => x.CompletedById).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TodoComment>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasIndex(x => new { x.TodoId, x.CreatedAt });
            e.HasOne(x => x.Todo).WithMany(x => x.Comments).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TodoAssignee>(e =>
        {
            e.HasKey(x => new { x.TodoId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Todo).WithMany(x => x.Assignees).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DueAdjustment>(e =>
        {
            e.HasIndex(x => new { x.EnrollmentId, x.Period }).IsUnique();
            e.HasOne(x => x.Enrollment).WithMany(x => x.DueAdjustments).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Note).HasMaxLength(500);
        });
        b.Entity<Obligation>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.Staff).WithMany().OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<ObligationPayment>(e =>
        {
            e.HasIndex(x => new { x.ObligationId, x.Month }).IsUnique();
            e.HasOne(x => x.Obligation).WithMany(x => x.Payments).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Note).HasMaxLength(500);
        });

        b.Entity<Page>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            Json(e.Property(x => x.Draft));
            Json(e.Property(x => x.Published));
        });
        b.Entity<SiteSettings>(e =>
        {
            Json(e.Property(x => x.HeaderNav));
            Json(e.Property(x => x.FooterNav));
            Json(e.Property(x => x.HeaderStyle));
            Json(e.Property(x => x.FooterStyle));
        });
        b.Entity<Theme>().HasIndex(x => x.EndDate);
        b.Entity<SpecialDay>().HasIndex(x => new { x.Date, x.Name }).IsUnique();
        b.Entity<IntegrationConfig>(e =>
        {
            e.HasIndex(x => x.Type).IsUnique();
            e.Property(x => x.Settings).HasColumnType("jsonb").HasConversion(DictionaryConverter, DictionaryComparer);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = entry.Entity.UpdatedAt = now;
            else if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(ct);
    }

    private static readonly Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Dictionary<string, string>, string>
        DictionaryConverter = new(
            v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
            v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new());


    private static readonly ValueComparer<Dictionary<string, string>> DictionaryComparer = new(
        (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
        v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
        v => new Dictionary<string, string>(v));

    /// <summary>Stores a property as jsonb; change tracking compares the serialized JSON.</summary>
    private static void Json<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property) =>
        property.HasColumnType("jsonb").HasConversion(
            new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<T, string>(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<T>(v, JsonSerializerOptions.Default)!),
            new ValueComparer<T>(
                (a, b) => JsonSerializer.Serialize(a, JsonSerializerOptions.Default) == JsonSerializer.Serialize(b, JsonSerializerOptions.Default),
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default).GetHashCode(),
                v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, JsonSerializerOptions.Default), JsonSerializerOptions.Default)!));
}
