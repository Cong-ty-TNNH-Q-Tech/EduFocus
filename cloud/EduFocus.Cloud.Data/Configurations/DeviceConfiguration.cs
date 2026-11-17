using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices", table => table.HasCheckConstraint(
            "CK_Devices_Kind",
            "\"Kind\" IN ('Desktop', 'Mobile')"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.AppVersion).HasMaxLength(32);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_Devices_FamilyId");
    }
}
