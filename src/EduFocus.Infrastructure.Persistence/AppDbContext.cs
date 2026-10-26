using EduFocus.Domain.Attention;
using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EduFocus.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<ParentCredentials> ParentCredentials => Set<ParentCredentials>();
    public DbSet<BlockRule> BlockRules => Set<BlockRule>();
    public DbSet<ScheduleWindow> ScheduleWindows => Set<ScheduleWindow>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<ActivitySample> ActivitySamples => Set<ActivitySample>();
    public DbSet<AttentionEvent> AttentionEvents => Set<AttentionEvent>();
    public DbSet<BlockedAttempt> BlockedAttempts => Set<BlockedAttempt>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var utc = new ValueConverter<DateTimeOffset,string>(v => v.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture), v => DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));
        var nullableUtc = new ValueConverter<DateTimeOffset?,string?>(v => v.HasValue ? v.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) : null, v => v == null ? null : DateTimeOffset.Parse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal));
        static void UtcProperty(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset> property, ValueConverter<DateTimeOffset,string> converter) => property.HasConversion(converter).HasColumnType("TEXT");
        static void NullableUtcProperty(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<DateTimeOffset?> property, ValueConverter<DateTimeOffset?,string?> converter) => property.HasConversion(converter).HasColumnType("TEXT");
        static void EnumText<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> p) where T:struct,Enum => p.HasConversion<string>().HasMaxLength(32);

        model.Entity<AppSettings>(b => {
            b.ToTable("AppSettings",t=>t.HasCheckConstraint("CK_AppSettings_SingletonId","Id = '00000000-0000-0000-0000-000000000001'")); b.HasKey(x=>x.Id); b.Property(x=>x.Id).ValueGeneratedNever();
            b.Property(x=>x.Protection).HasColumnName("ProtectionEnabled").HasConversion(v=>v==ProtectionState.On,v=>v?ProtectionState.On:ProtectionState.Off).HasDefaultValue(ProtectionState.Off);
            b.Property(x=>x.MonitoringEnabled).HasDefaultValue(false); b.Property(x=>x.ScheduleEnabled).HasDefaultValue(false); b.Property(x=>x.MonitorOnlyDuringClasses).HasDefaultValue(false);
            b.Property(x=>x.CameraDeviceId).HasMaxLength(256); b.Property(x=>x.DistractionThresholdSeconds).HasDefaultValue(20); b.Property(x=>x.AbsenceThresholdSeconds).HasDefaultValue(30); b.Property(x=>x.SampleIntervalSeconds).HasDefaultValue(15);
            b.Property(x=>x.ActivityRetentionDays).HasDefaultValue(30); b.Property(x=>x.EventRetentionDays).HasDefaultValue(90); b.Property(x=>x.SessionRetentionDays).HasDefaultValue(365); b.Property(x=>x.StartWithWindows).HasDefaultValue(false);
            b.Property(x=>x.Theme).HasConversion(v=>v.ToString().ToLowerInvariant(),v=>Enum.Parse<ThemePreference>(v,true)).HasMaxLength(16).HasDefaultValue(ThemePreference.Dark);
            b.Property(x=>x.Language).HasConversion(v=>v==LanguagePreference.Vietnamese?"vi":"en",v=>v=="vi"?LanguagePreference.Vietnamese:LanguagePreference.English).HasMaxLength(8).HasDefaultValue(LanguagePreference.Vietnamese);
            NullableUtcProperty(b.Property(x=>x.ScheduleOverrideUntilUtc),nullableUtc); NullableUtcProperty(b.Property(x=>x.LastCleanShutdownAtUtc),nullableUtc);
            UtcProperty(b.Property(x=>x.CreatedAtUtc),utc); UtcProperty(b.Property(x=>x.UpdatedAtUtc),utc);
        });
        model.Entity<ParentCredentials>(b=>{
            b.ToTable("ParentCredentials",t=>{t.HasCheckConstraint("CK_ParentCredentials_SingletonId","Id = '00000000-0000-0000-0000-000000000002'");t.HasCheckConstraint("CK_ParentCredentials_PinIterations","PinIterations >= 600000");t.HasCheckConstraint("CK_ParentCredentials_RecoveryIterations","RecoveryCodeIterations >= 600000");t.HasCheckConstraint("CK_ParentCredentials_FailedAttempts","FailedAttempts BETWEEN 0 AND 5");});b.HasKey(x=>x.Id);b.Property(x=>x.Id).ValueGeneratedNever();
            b.OwnsOne(x=>x.Pin,p=>{p.Property(x=>x.HashBase64).HasColumnName("PinHash").HasMaxLength(128).IsRequired();p.Property(x=>x.SaltBase64).HasColumnName("PinSalt").HasMaxLength(64).IsRequired();p.Property(x=>x.Iterations).HasColumnName("PinIterations").HasDefaultValue(600000);});
            b.OwnsOne(x=>x.RecoveryCode,p=>{p.Property(x=>x.HashBase64).HasColumnName("RecoveryCodeHash").HasMaxLength(128).IsRequired();p.Property(x=>x.SaltBase64).HasColumnName("RecoveryCodeSalt").HasMaxLength(64).IsRequired();p.Property(x=>x.Iterations).HasColumnName("RecoveryCodeIterations").HasDefaultValue(600000);});
            b.Property(x=>x.FailedAttempts).HasDefaultValue(0);NullableUtcProperty(b.Property(x=>x.LockoutUntilUtc),nullableUtc);UtcProperty(b.Property(x=>x.PinUpdatedAtUtc),utc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);
        });
        model.Entity<BlockRule>(b=>{
            b.ToTable("BlockRules",t=>{t.HasCheckConstraint("CK_BlockRules_Effect","Effect IN ('Block','Allow')");t.HasCheckConstraint("CK_BlockRules_RuleType","RuleType IN ('Domain','App','Url')");t.HasCheckConstraint("CK_BlockRules_MatchType","MatchType IN ('Exact','Suffix','Contains')");t.HasCheckConstraint("CK_BlockRules_EnforcementStatus","EnforcementStatus IN ('Enforced','NotEnforced')");});b.HasKey(x=>x.Id);
            EnumText(b.Property(x=>x.RuleType));EnumText(b.Property(x=>x.MatchType));EnumText(b.Property(x=>x.Category));EnumText(b.Property(x=>x.Effect));b.Property(x=>x.EnforcementStatus).HasConversion<string>().HasMaxLength(32).HasDefaultValue(RuleEnforcementStatus.Enforced);
            b.Property(x=>x.Value).HasMaxLength(512).IsRequired();b.Property(x=>x.NormalizedValue).HasMaxLength(512).IsRequired();b.Property(x=>x.Note).HasMaxLength(512);b.Property(x=>x.IsEnabled).HasDefaultValue(true);b.Property(x=>x.IsBuiltIn).HasDefaultValue(false);
            NullableUtcProperty(b.Property(x=>x.ExpiresAtUtc),nullableUtc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);UtcProperty(b.Property(x=>x.UpdatedAtUtc),utc);
            b.HasIndex(x=>new{x.RuleType,x.MatchType,x.NormalizedValue,x.Effect}).IsUnique();b.HasIndex(x=>new{x.Effect,x.IsEnabled});b.HasIndex(x=>x.Category);b.HasIndex(x=>x.ExpiresAtUtc);
        });
        model.Entity<ScheduleWindow>(b=>{b.ToTable("ScheduleWindows",t=>{t.HasCheckConstraint("CK_ScheduleWindows_DaysMask","DaysMask BETWEEN 1 AND 127");t.HasCheckConstraint("CK_ScheduleWindows_TimeRange","StartMinutes BETWEEN 0 AND 1439 AND EndMinutes BETWEEN 1 AND 1440 AND EndMinutes > StartMinutes");});b.HasKey(x=>x.Id);b.Property(x=>x.Label).HasMaxLength(100);b.Property(x=>x.IsEnabled).HasDefaultValue(true);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);UtcProperty(b.Property(x=>x.UpdatedAtUtc),utc);b.HasIndex(x=>x.IsEnabled);});
        model.Entity<StudySession>(b=>{
            b.ToTable("StudySessions",t=>{t.HasCheckConstraint("CK_StudySessions_NonNegativeCounters","ActiveSeconds >= 0 AND FocusedSeconds >= 0 AND DistractedSeconds >= 0 AND AbsentSeconds >= 0 AND DrowsySeconds >= 0 AND CameraUnavailableSeconds >= 0 AND IdleSeconds >= 0 AND StudySeconds >= 0 AND DistractionSeconds >= 0 AND NeutralSeconds >= 0 AND OnlineClassSeconds >= 0 AND MonitoredSeconds >= 0 AND MonitoringEligibleSeconds >= 0 AND BlockedAttemptCount >= 0");t.HasCheckConstraint("CK_StudySessions_MonitoredSeconds","MonitoredSeconds = FocusedSeconds + DistractedSeconds + AbsentSeconds + DrowsySeconds");t.HasCheckConstraint("CK_StudySessions_Percentages","(AttentionPercent IS NULL OR AttentionPercent BETWEEN 0 AND 100) AND (CoveragePercent IS NULL OR CoveragePercent >= 0) AND (ConfidenceAvg IS NULL OR ConfidenceAvg BETWEEN 0 AND 1)");t.HasCheckConstraint("CK_StudySessions_Lifecycle","(Status = 'Active' AND EndedAtUtc IS NULL) OR (Status <> 'Active' AND EndedAtUtc IS NOT NULL)");});b.HasKey(x=>x.Id);EnumText(b.Property(x=>x.Status));EnumText(b.Property(x=>x.Trigger));
            UtcProperty(b.Property(x=>x.StartedAtUtc),utc);NullableUtcProperty(b.Property(x=>x.EndedAtUtc),nullableUtc);UtcProperty(b.Property(x=>x.LastHeartbeatAtUtc),utc);
            b.Property(x=>x.MonitoredSeconds).HasDefaultValue(0);b.Property(x=>x.AttentionPercent);b.Property(x=>x.CoveragePercent);b.Property(x=>x.ConfidenceAvg);
            b.Property(x=>x.ActiveSeconds).HasDefaultValue(0);b.Property(x=>x.FocusedSeconds).HasDefaultValue(0);b.Property(x=>x.DistractedSeconds).HasDefaultValue(0);b.Property(x=>x.AbsentSeconds).HasDefaultValue(0);b.Property(x=>x.DrowsySeconds).HasDefaultValue(0);b.Property(x=>x.CameraUnavailableSeconds).HasDefaultValue(0);b.Property(x=>x.IdleSeconds).HasDefaultValue(0);b.Property(x=>x.StudySeconds).HasDefaultValue(0);b.Property(x=>x.DistractionSeconds).HasDefaultValue(0);b.Property(x=>x.NeutralSeconds).HasDefaultValue(0);b.Property(x=>x.OnlineClassSeconds).HasDefaultValue(0);b.Property(x=>x.MonitoringEligibleSeconds).HasDefaultValue(0);b.Property(x=>x.BlockedAttemptCount).HasDefaultValue(0);
            b.Property(x=>x.Note).HasMaxLength(512);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);UtcProperty(b.Property(x=>x.UpdatedAtUtc),utc);
            b.HasIndex(x=>x.StartedAtUtc);b.HasIndex(x=>x.StartLocalDate);b.HasIndex(x=>x.Status);b.HasIndex(x=>x.LastHeartbeatAtUtc);b.HasIndex(x=>x.Status).IsUnique().HasFilter("Status = 'Active'").HasDatabaseName("UX_StudySessions_Active");
        });
        model.Entity<ActivitySample>(b=>{b.ToTable("ActivitySamples",t=>t.HasCheckConstraint("CK_ActivitySamples_Durations","DurationSeconds >= 0 AND IdleSeconds >= 0"));b.HasKey(x=>x.Id);b.HasOne<StudySession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Cascade);UtcProperty(b.Property(x=>x.SampledAtUtc),utc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);b.Property(x=>x.UrlSource).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ActivityUrlSource.None);b.Property(x=>x.Category).HasConversion<string>().HasMaxLength(32).HasDefaultValue(ActivityCategory.Unknown);b.Property(x=>x.DurationSeconds).HasDefaultValue(0L);b.Property(x=>x.IdleSeconds).HasDefaultValue(0L);b.Property(x=>x.IsForeground).HasDefaultValue(true);b.Property(x=>x.IsOnlineClass).HasDefaultValue(false);b.Property(x=>x.AppName).HasMaxLength(128);b.Property(x=>x.ProcessName).HasMaxLength(128);b.Property(x=>x.WindowTitle).HasMaxLength(512);b.Property(x=>x.Url).HasMaxLength(2048);b.Property(x=>x.Domain).HasMaxLength(256);b.HasIndex(x=>new{x.SessionId,x.SampledAtUtc});b.HasIndex(x=>x.Domain);b.HasIndex(x=>x.Category);});
        model.Entity<AttentionEvent>(b=>{b.ToTable("AttentionEvents",t=>t.HasCheckConstraint("CK_AttentionEvents_DurationConfidence","DurationSeconds >= 0 AND (Confidence IS NULL OR Confidence BETWEEN 0 AND 1)"));b.HasKey(x=>x.Id);b.HasOne<StudySession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.Cascade);EnumText(b.Property(x=>x.State));UtcProperty(b.Property(x=>x.StartedAtUtc),utc);NullableUtcProperty(b.Property(x=>x.EndedAtUtc),nullableUtc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);b.Property(x=>x.DurationSeconds).HasDefaultValue(0L);b.Property(x=>x.AppName).HasMaxLength(128);b.Property(x=>x.Domain).HasMaxLength(256);b.Property(x=>x.Note).HasMaxLength(256);b.HasIndex(x=>new{x.SessionId,x.StartedAtUtc});b.HasIndex(x=>x.State);});
        model.Entity<BlockedAttempt>(b=>{b.ToTable("BlockedAttempts");b.HasKey(x=>x.Id);b.HasOne<StudySession>().WithMany().HasForeignKey(x=>x.SessionId).OnDelete(DeleteBehavior.SetNull);EnumText(b.Property(x=>x.TargetType));EnumText(b.Property(x=>x.ObservedVia));UtcProperty(b.Property(x=>x.OccurredAtUtc),utc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);b.Property(x=>x.TargetValue).HasMaxLength(512).IsRequired();b.Property(x=>x.Domain).HasMaxLength(256);b.Property(x=>x.Url).HasMaxLength(2048);b.Property(x=>x.ProcessName).HasMaxLength(128);b.Property(x=>x.WindowTitle).HasMaxLength(512);b.HasIndex(x=>x.OccurredAtUtc);b.HasIndex(x=>x.Domain);b.HasIndex(x=>x.SessionId);});
        model.Entity<AuditLog>(b=>{b.ToTable("AuditLogs",t=>t.HasCheckConstraint("CK_AuditLogs_Actor","Actor IN ('Parent','System','Student')"));b.HasKey(x=>x.Id);EnumText(b.Property(x=>x.Actor));UtcProperty(b.Property(x=>x.OccurredAtUtc),utc);UtcProperty(b.Property(x=>x.CreatedAtUtc),utc);b.Property(x=>x.EventType).HasMaxLength(64).IsRequired();b.Property(x=>x.Description).HasMaxLength(512).IsRequired();b.Property(x=>x.MetadataJson).HasMaxLength(2048);b.HasIndex(x=>x.OccurredAtUtc);b.HasIndex(x=>x.EventType);});
    }
}
