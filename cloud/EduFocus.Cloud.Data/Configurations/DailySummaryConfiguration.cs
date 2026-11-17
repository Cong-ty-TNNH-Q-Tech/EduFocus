using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class DailySummaryConfiguration : IEntityTypeConfiguration<DailySummary>
{
    public void Configure(EntityTypeBuilder<DailySummary> builder)
    {
        builder.ToTable("DailySummaries", table =>
        {
            table.HasCheckConstraint(
                "CK_DailySummaries_NonNegative",
                "\"SessionsCount\" >= 0 AND \"ActiveSeconds\" >= 0 AND \"StudySeconds\" >= 0 " +
                "AND \"IdleSeconds\" >= 0 AND \"OnlineClassSeconds\" >= 0 AND \"MonitoringEligibleSeconds\" >= 0 " +
                "AND \"MonitoredSeconds\" >= 0 AND \"FocusedSeconds\" >= 0 AND \"DistractedSeconds\" >= 0 " +
                "AND \"AbsentSeconds\" >= 0 AND \"DrowsySeconds\" >= 0 AND \"BlockedAttempts\" >= 0");
            table.HasCheckConstraint(
                "CK_DailySummaries_AttentionPercent",
                "\"AttentionPercent\" IS NULL OR \"AttentionPercent\" BETWEEN 0 AND 100");
            table.HasCheckConstraint(
                "CK_DailySummaries_CoveragePercent",
                "\"CoveragePercent\" IS NULL OR \"CoveragePercent\" BETWEEN 0 AND 100");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DesktopDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.FamilyId, x.DesktopDeviceId, x.LocalDate })
            .IsUnique()
            .HasDatabaseName("UX_DailySummaries_Family_Device_Date");
        builder.HasIndex(x => new { x.FamilyId, x.LocalDate })
            .HasDatabaseName("IX_DailySummaries_FamilyId_LocalDate");
    }
}
