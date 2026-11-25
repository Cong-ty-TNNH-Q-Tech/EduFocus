using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class NotificationRecordConfiguration : IEntityTypeConfiguration<NotificationRecord>
{
    public void Configure(EntityTypeBuilder<NotificationRecord> builder)
    {
        builder.ToTable("Notifications", table =>
        {
            table.HasCheckConstraint(
                "CK_Notifications_Category",
                "\"Category\" IN ('DailySummary', 'AttentionAlert', 'ProtectionOff', 'SecurityAlert', 'BlockedAttempt')");
            table.HasCheckConstraint(
                "CK_Notifications_Status",
                "\"Status\" IN ('Pending', 'Sent', 'Suppressed', 'Failed')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(x => x.DedupeKey).HasMaxLength(160).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(64);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.AttemptCount).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.FamilyId, x.DedupeKey })
            .IsUnique()
            .HasDatabaseName("UX_Notifications_Family_DedupeKey");
        builder.HasIndex(x => new { x.FamilyId, x.CreatedAtUtc })
            .HasDatabaseName("IX_Notifications_FamilyId_CreatedAtUtc");
    }
}
