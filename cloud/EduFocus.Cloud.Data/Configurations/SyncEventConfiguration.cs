using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class SyncEventConfiguration : IEntityTypeConfiguration<SyncEvent>
{
    public void Configure(EntityTypeBuilder<SyncEvent> builder)
    {
        builder.ToTable("SyncEvents", table =>
        {
            table.HasCheckConstraint(
                "CK_SyncEvents_Type",
                "\"Type\" IN ('DailySummary', 'AttentionAlert', 'ProtectionOff', 'SecurityAlert', 'BlockedAttempt')");
            table.HasCheckConstraint(
                "CK_SyncEvents_Status",
                "\"Status\" IN ('Pending', 'Processed', 'Rejected')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ClientEventId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(x => x.ReceivedAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.FamilyId, x.ClientEventId })
            .IsUnique()
            .HasDatabaseName("UX_SyncEvents_Family_ClientEventId");
        builder.HasIndex(x => new { x.FamilyId, x.ReceivedAtUtc })
            .HasDatabaseName("IX_SyncEvents_FamilyId_ReceivedAtUtc");
    }
}
