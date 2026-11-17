using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("DeviceTokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.TokenHash)
            .HasMaxLength(64)
            .HasColumnType("char(64)")
            .IsRequired();
        builder.Property(x => x.Scopes).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_DeviceTokens_TokenHash");
        builder.HasIndex(x => x.DeviceId).HasDatabaseName("IX_DeviceTokens_DeviceId");
    }
}
