using EduFocus.Domain.Entities;
using EduFocus.Domain.ValueObjects;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;

namespace EduFocus.SqliteDataAccess;

/// <summary>
/// SQLite persistence model for EduFocus 2.0. Entity invariants live in the domain;
/// this context is the single source of truth for physical names, constraints,
/// indexes and UTC storage (database_schema §3–§12).
/// </summary>
public sealed class EduFocusDbContext : DbContext
{
    public EduFocusDbContext(DbContextOptions<EduFocusDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    public DbSet<ParentCredential> ParentCredentials => Set<ParentCredential>();

    public DbSet<BlockRule> BlockRules => Set<BlockRule>();

    public DbSet<ScheduleWindow> ScheduleWindows => Set<ScheduleWindow>();

    public DbSet<StudySession> StudySessions => Set<StudySession>();

    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();

    public DbSet<AttentionEvent> AttentionEvents => Set<AttentionEvent>();

    public DbSet<BlockedAttempt> BlockedAttempts => Set<BlockedAttempt>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EduFocusDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveConversion<UtcDateTimeConverter>()
            .HaveColumnType("TEXT(28)");

        configurationBuilder.Properties<DateOnly>()
            .HaveColumnType("TEXT(10)");

        configurationBuilder.Properties<DomainName>()
            .HaveConversion<DomainNameConverter>()
            .HaveMaxLength(256)
            .HaveColumnType("TEXT(256)");

        configurationBuilder.Properties<AppSettingsId>()
            .HaveConversion<AppSettingsIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<ParentCredentialId>()
            .HaveConversion<ParentCredentialIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<BlockRuleId>()
            .HaveConversion<BlockRuleIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<ScheduleWindowId>()
            .HaveConversion<ScheduleWindowIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<StudySessionId>()
            .HaveConversion<StudySessionIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<ActivitySampleId>()
            .HaveConversion<ActivitySampleIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<AttentionEventId>()
            .HaveConversion<AttentionEventIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<BlockedAttemptId>()
            .HaveConversion<BlockedAttemptIdConverter>()
            .HaveColumnType("TEXT(36)");

        configurationBuilder.Properties<AuditLogId>()
            .HaveConversion<AuditLogIdConverter>()
            .HaveColumnType("TEXT(36)");
    }
}
