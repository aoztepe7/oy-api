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
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();
    public DbSet<Theme> Themes => Set<Theme>();
    public DbSet<SpecialDay> SpecialDays => Set<SpecialDay>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<IntegrationConfig> Integrations => Set<IntegrationConfig>();

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
        b.Properties<decimal>().HavePrecision(14, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasOne(x => x.Role).WithMany(x => x.Users).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Role>().HasIndex(x => x.Name).IsUnique();

        b.Entity<TrainingProgram>(e =>
        {
            e.ToTable("Programs");
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => new { x.IsPublished, x.EndDate });
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
        b.Entity<Enrollment>().HasIndex(x => new { x.ProgramId, x.Status });
        b.Entity<Payment>().HasOne(x => x.Invoice).WithOne(x => x.Payment).HasForeignKey<Invoice>(x => x.PaymentId);
        b.Entity<Invoice>().HasIndex(x => x.Status);

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
