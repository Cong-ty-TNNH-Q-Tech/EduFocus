using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class ParentCredentialConfiguration : IEntityTypeConfiguration<ParentCredential>
{
    public void Configure(EntityTypeBuilder<ParentCredential> builder)
    {
        builder.ToTable("ParentCredentials", table =>
        {
            table.HasCheckConstraint(
                "CK_ParentCredentials_PinIterations",
                $"\"PinIterations\" >= {ParentCredential.MinimumIterations}");
            table.HasCheckConstraint(
                "CK_ParentCredentials_RecoveryCodeIterations",
                $"\"RecoveryCodeIterations\" >= {ParentCredential.MinimumIterations}");
            table.HasCheckConstraint(
                "CK_ParentCredentials_FailedAttempts",
                "\"FailedAttempts\" >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.PinHash).HasMaxLength(128).HasColumnType("TEXT(128)").IsRequired();
        builder.Property(x => x.PinSalt).HasMaxLength(64).HasColumnType("TEXT(64)").IsRequired();
        builder.Property(x => x.PinIterations).IsRequired();
        builder.Property(x => x.RecoveryCodeHash).HasMaxLength(128).HasColumnType("TEXT(128)").IsRequired();
        builder.Property(x => x.RecoveryCodeSalt).HasMaxLength(64).HasColumnType("TEXT(64)").IsRequired();
        builder.Property(x => x.RecoveryCodeIterations).IsRequired();
        builder.Property(x => x.FailedAttempts).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.LockoutUntilUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");
        builder.Property(x => x.PinUpdatedAtUtc).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
    }
}
