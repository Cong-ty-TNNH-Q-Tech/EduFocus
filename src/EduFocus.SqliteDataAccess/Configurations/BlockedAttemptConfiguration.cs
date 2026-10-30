using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class BlockedAttemptConfiguration : IEntityTypeConfiguration<BlockedAttempt>
{
    public void Configure(EntityTypeBuilder<BlockedAttempt> builder)
    {
        builder.ToTable("BlockedAttempts", table =>
        {
            table.HasCheckConstraint(
                "CK_BlockedAttempts_TargetType",
                "\"TargetType\" IN ('Domain', 'App', 'Url')");
            table.HasCheckConstraint(
                "CK_BlockedAttempts_ObservedVia",
                "\"ObservedVia\" IN ('BlockPage', 'ProcessWatcher', 'Extension')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasOne<StudySession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.SessionId)
            .HasConversion<StudySessionIdConverter>()
            .HasColumnType("TEXT(36)");

        builder.Property(x => x.OccurredAtUtc).IsRequired();

        builder.Property(x => x.TargetType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.TargetValue).HasMaxLength(BlockedAttempt.MaxTargetValueLength).HasColumnType("TEXT(512)").IsRequired();
        builder.Property(x => x.Domain).HasMaxLength(256).HasColumnType("TEXT(256)");
        builder.Property(x => x.Url).HasMaxLength(2048).HasColumnType("TEXT(2048)");
        builder.Property(x => x.ProcessName).HasMaxLength(BlockedAttempt.MaxProcessNameLength).HasColumnType("TEXT(128)");
        builder.Property(x => x.WindowTitle).HasMaxLength(BlockedAttempt.MaxWindowTitleLength).HasColumnType("TEXT(512)");

        builder.Property(x => x.ObservedVia)
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasColumnType("TEXT(24)")
            .IsRequired();

        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_BlockedAttempts_OccurredAtUtc");

        builder.HasIndex(x => x.Domain)
            .HasDatabaseName("IX_BlockedAttempts_Domain");

        builder.HasIndex(x => x.SessionId)
            .HasDatabaseName("IX_BlockedAttempts_SessionId");
    }
}
