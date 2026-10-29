using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class AppSettingsConfiguration : IEntityTypeConfiguration<AppSettings>
{
    public void Configure(EntityTypeBuilder<AppSettings> builder)
    {
        builder.ToTable("AppSettings", table =>
        {
            table.HasCheckConstraint("CK_AppSettings_Theme", "\"Theme\" IN ('dark', 'light', 'system')");
            table.HasCheckConstraint("CK_AppSettings_Language", "\"Language\" IN ('vi', 'en')");
            table.HasCheckConstraint(
                "CK_AppSettings_Thresholds",
                "\"DistractionThresholdSeconds\" > 0 AND \"AbsenceThresholdSeconds\" > 0 AND \"SampleIntervalSeconds\" > 0");
            table.HasCheckConstraint(
                "CK_AppSettings_Retention",
                "\"ActivityRetentionDays\" > 0 AND \"EventRetentionDays\" > 0 AND \"SessionRetentionDays\" > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ProtectionEnabled).HasDefaultValue(false).ValueGeneratedNever();

        builder.Property(x => x.ProtectionSource)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .HasDefaultValue(ProtectionSource.None)
            .ValueGeneratedNever();

        builder.Property(x => x.MonitoringEnabled).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.ScheduleEnabled).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.MonitorOnlyDuringClasses).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.CameraDeviceId)
            .HasMaxLength(AppSettings.MaxCameraDeviceIdLength)
            .HasColumnType("TEXT(256)");

        builder.Property(x => x.DistractionThresholdSeconds).HasDefaultValue(20).ValueGeneratedNever();
        builder.Property(x => x.AbsenceThresholdSeconds).HasDefaultValue(30).ValueGeneratedNever();
        builder.Property(x => x.SampleIntervalSeconds).HasDefaultValue(15).ValueGeneratedNever();

        builder.Property(x => x.ActivityRetentionDays).HasDefaultValue(30).ValueGeneratedNever();
        builder.Property(x => x.EventRetentionDays).HasDefaultValue(90).ValueGeneratedNever();
        builder.Property(x => x.SessionRetentionDays).HasDefaultValue(365).ValueGeneratedNever();

        builder.Property(x => x.StartWithWindows).HasDefaultValue(true).ValueGeneratedNever();

        builder.Property(x => x.Theme)
            .HasConversion<AppThemeConverter>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .HasDefaultValue(AppTheme.Dark)
            .ValueGeneratedNever();

        builder.Property(x => x.Language)
            .HasConversion<AppLanguageConverter>()
            .HasMaxLength(8)
            .HasColumnType("TEXT(8)")
            .HasDefaultValue(AppLanguage.Vi)
            .ValueGeneratedNever();

        builder.Property(x => x.OnboardingCompleted).HasDefaultValue(false).ValueGeneratedNever();

        builder.Property(x => x.PrivacyAcceptedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.GuardianAcknowledgedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.ScheduleOverrideUntilUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.LastCleanShutdownAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
