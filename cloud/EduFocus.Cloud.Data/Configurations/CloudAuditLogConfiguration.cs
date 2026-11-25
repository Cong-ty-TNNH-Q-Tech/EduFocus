using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class CloudAuditLogConfiguration : IEntityTypeConfiguration<CloudAuditLog>
{
    public void Configure(EntityTypeBuilder<CloudAuditLog> builder)
    {
        builder.ToTable("CloudAuditLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Action).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MetadataJson).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.ActorDeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("IX_CloudAuditLogs_CreatedAtUtc");
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_CloudAuditLogs_FamilyId");
    }
}
