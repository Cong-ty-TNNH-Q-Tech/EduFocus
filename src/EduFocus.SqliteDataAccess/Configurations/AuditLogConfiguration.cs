using EduFocus.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs", table =>
        {
            table.HasCheckConstraint(
                "CK_AuditLogs_Actor",
                "\"Actor\" IN ('Parent', 'System', 'Student')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAtUtc).IsRequired();

        builder.Property(x => x.Actor)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasColumnType("TEXT(64)")
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(AuditLog.MaxDescriptionLength)
            .HasColumnType("TEXT(512)")
            .IsRequired();

        builder.Property(x => x.MetadataJson)
            .HasMaxLength(AuditLog.MaxMetadataLength)
            .HasColumnType("TEXT(2048)");
        builder.Property(x => x.CorrelationId).HasColumnType("TEXT(36)");
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.OccurredAtUtc)
            .HasDatabaseName("IX_AuditLogs_OccurredAtUtc");

        builder.HasIndex(x => x.EventType)
            .HasDatabaseName("IX_AuditLogs_EventType");
    }
}
