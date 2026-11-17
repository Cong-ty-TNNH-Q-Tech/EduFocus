using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences", table => table.HasCheckConstraint(
            "CK_NotificationPreferences_Minutes",
            "\"QuietStartMinutes\" BETWEEN 0 AND 1439 AND \"QuietEndMinutes\" BETWEEN 0 AND 1439 " +
            "AND \"DailySummaryMinutes\" BETWEEN 0 AND 1439"));

        builder.HasKey(x => x.FamilyId);
        builder.Property(x => x.FamilyId).ValueGeneratedNever();

        builder.Property(x => x.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
