using EduFocus.Cloud.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.Cloud.Data.Configurations;

internal sealed class PairingSessionConfiguration : IEntityTypeConfiguration<PairingSession>
{
    public void Configure(EntityTypeBuilder<PairingSession> builder)
    {
        builder.ToTable("PairingSessions", table => table.HasCheckConstraint(
            "CK_PairingSessions_AttemptCount",
            "\"AttemptCount\" >= 0"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CodeHash)
            .HasMaxLength(64)
            .HasColumnType("char(64)")
            .IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        builder.HasOne<Family>()
            .WithMany()
            .HasForeignKey(x => x.FamilyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.DesktopDeviceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Device>()
            .WithMany()
            .HasForeignKey(x => x.ClaimedByDeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CodeHash)
            .IsUnique()
            .HasFilter("\"ClaimedAtUtc\" IS NULL")
            .HasDatabaseName("UX_PairingSessions_CodeHash_Unclaimed");
        builder.HasIndex(x => x.FamilyId).HasDatabaseName("IX_PairingSessions_FamilyId");
        builder.HasIndex(x => x.ExpiresAtUtc).HasDatabaseName("IX_PairingSessions_ExpiresAtUtc");
    }
}
