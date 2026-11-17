using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class CloudLinkConfiguration : IEntityTypeConfiguration<CloudLink>
{
    public void Configure(EntityTypeBuilder<CloudLink> builder)
    {
        builder.ToTable("CloudLink", table =>
        {
            table.HasCheckConstraint(
                "CK_CloudLink_EnabledRequiresConsent",
                "\"IsEnabled\" = 0 OR \"ConsentAcceptedAtUtc\" IS NOT NULL");
            table.HasCheckConstraint(
                "CK_CloudLink_TokenClearedWhenDisabled",
                "\"DesktopToken\" IS NULL OR \"IsEnabled\" = 1");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.IsEnabled).HasDefaultValue(false).ValueGeneratedNever();

        builder.Property(x => x.ConsentAcceptedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.CloudBaseUrl)
            .HasMaxLength(CloudLink.MaxBaseUrlLength)
            .HasColumnType("TEXT(512)");

        builder.Property(x => x.PairingSessionId).HasColumnType("TEXT(36)");

        builder.Property(x => x.PairingCode)
            .HasMaxLength(CloudLink.MaxPairingCodeLength)
            .HasColumnType("TEXT(32)");

        builder.Property(x => x.PairingExpiresAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.FamilyId).HasColumnType("TEXT(36)");
        builder.Property(x => x.DesktopDeviceId).HasColumnType("TEXT(36)");

        builder.Property(x => x.DesktopToken)
            .HasMaxLength(CloudLink.MaxTokenLength)
            .HasColumnType("TEXT(256)");

        builder.Property(x => x.LinkedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.LastSyncAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.LastError)
            .HasMaxLength(CloudLink.MaxErrorLength)
            .HasColumnType("TEXT(512)");

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
