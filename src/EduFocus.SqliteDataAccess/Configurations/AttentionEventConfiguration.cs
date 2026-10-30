using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class AttentionEventConfiguration : IEntityTypeConfiguration<AttentionEvent>
{
    public void Configure(EntityTypeBuilder<AttentionEvent> builder)
    {
        builder.ToTable("AttentionEvents", table =>
        {
            table.HasCheckConstraint(
                "CK_AttentionEvents_State",
                "\"State\" IN ('Focused', 'Distracted', 'Absent', 'Drowsy', 'CameraUnavailable')");
            table.HasCheckConstraint(
                "CK_AttentionEvents_Duration",
                "\"DurationSeconds\" >= 0");
            table.HasCheckConstraint(
                "CK_AttentionEvents_Confidence",
                "\"Confidence\" IS NULL OR (\"Confidence\" >= 0 AND \"Confidence\" <= 1)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasOne<StudySession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.State)
            .HasConversion<string>()
            .HasMaxLength(24)
            .HasColumnType("TEXT(24)")
            .IsRequired();

        builder.Property(x => x.StartedAtUtc).IsRequired();
        builder.Property(x => x.EndedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");
        builder.Property(x => x.DurationSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.Confidence);
        builder.Property(x => x.AppName).HasMaxLength(AttentionEvent.MaxAppNameLength).HasColumnType("TEXT(128)");
        builder.Property(x => x.Domain).HasMaxLength(256).HasColumnType("TEXT(256)");
        builder.Property(x => x.Note).HasMaxLength(AttentionEvent.MaxNoteLength).HasColumnType("TEXT(256)");
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.SessionId, x.StartedAtUtc })
            .HasDatabaseName("IX_AttentionEvents_SessionId_StartedAtUtc");

        builder.HasIndex(x => x.State)
            .HasDatabaseName("IX_AttentionEvents_State");
    }
}
