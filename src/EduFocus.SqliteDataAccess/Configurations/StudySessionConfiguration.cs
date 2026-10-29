using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class StudySessionConfiguration : IEntityTypeConfiguration<StudySession>
{
    private const string MonitoredExpression =
        "(\"FocusedSeconds\" + \"DistractedSeconds\" + \"AbsentSeconds\" + \"DrowsySeconds\")";

    public void Configure(EntityTypeBuilder<StudySession> builder)
    {
        builder.ToTable("StudySessions", table =>
        {
            table.HasCheckConstraint(
                "CK_StudySessions_Status",
                "\"Status\" IN ('Active', 'Completed', 'Aborted', 'Interrupted')");
            table.HasCheckConstraint(
                "CK_StudySessions_Trigger",
                "\"Trigger\" IN ('Manual', 'Schedule', 'Recovery')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.StartedAtUtc).IsRequired();
        builder.Property(x => x.EndedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");
        builder.Property(x => x.LastHeartbeatAtUtc).IsRequired();
        builder.Property(x => x.StartLocalDate).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.Trigger)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.MonitoringEnabled).IsRequired();

        builder.Property(x => x.ActiveSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.FocusedSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.DistractedSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.AbsentSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.DrowsySeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.CameraUnavailableSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.IdleSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.StudySeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.DistractionSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.NeutralSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.OnlineClassSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.MonitoringEligibleSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.BlockedAttemptCount).HasDefaultValue(0).ValueGeneratedNever();

        builder.Ignore(x => x.MonitoredSeconds);
        builder.Ignore(x => x.AttentionPercent);
        builder.Ignore(x => x.CoveragePercent);
        builder.Ignore(x => x.IsActive);

        builder.Property<int>("StoredMonitoredSeconds")
            .HasColumnName("MonitoredSeconds")
            .HasColumnType("INTEGER")
            .IsRequired()
            .HasComputedColumnSql(MonitoredExpression, stored: true);

        builder.Property<double?>("StoredAttentionPercent")
            .HasColumnName("AttentionPercent")
            .HasColumnType("REAL")
            .HasComputedColumnSql(
                $"CASE WHEN {MonitoredExpression} > 0 THEN \"FocusedSeconds\" * 100.0 / {MonitoredExpression} ELSE NULL END",
                stored: true);

        builder.Property<double?>("StoredCoveragePercent")
            .HasColumnName("CoveragePercent")
            .HasColumnType("REAL")
            .HasComputedColumnSql(
                $"CASE WHEN \"MonitoringEligibleSeconds\" > 0 THEN {MonitoredExpression} * 100.0 / \"MonitoringEligibleSeconds\" ELSE NULL END",
                stored: true);

        builder.Property(x => x.Note).HasMaxLength(StudySession.MaxNoteLength).HasColumnType("TEXT(512)");
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.StartedAtUtc)
            .HasDatabaseName("IX_StudySessions_StartedAtUtc");

        builder.HasIndex(x => x.StartLocalDate)
            .HasDatabaseName("IX_StudySessions_StartLocalDate");

        builder.HasIndex(x => x.Status, "IX_StudySessions_Status");

        builder.HasIndex(x => x.LastHeartbeatAtUtc)
            .HasDatabaseName("IX_StudySessions_LastHeartbeatAtUtc");

        builder.HasIndex(x => x.Status, "UX_StudySessions_Active")
            .IsUnique()
            .HasFilter("\"Status\" = 'Active'");
    }
}
