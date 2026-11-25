using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class PushRegistrationConfiguration : IEntityTypeConfiguration<PushRegistration>
{
    public void Configure(EntityTypeBuilder<PushRegistration> builder)
    {
        builder.ToTable("PushRegistrations");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.P256dh).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Auth).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(32).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.DeviceId).HasDatabaseName("IX_PushRegistrations_DeviceId");
        builder.HasIndex(x => x.Endpoint).HasDatabaseName("IX_PushRegistrations_Endpoint");
    }
}
