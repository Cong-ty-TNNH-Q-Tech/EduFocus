using EduFocus.Domain.Entities;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class CloudOutboxConfiguration : IEntityTypeConfiguration<CloudOutboxEvent>
{
    public void Configure(EntityTypeBuilder<CloudOutboxEvent> builder)
    {
        builder.ToTable("CloudOutbox");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ClientEventId)
            .HasMaxLength(CloudOutboxEvent.MaxClientEventIdLength)
            .HasColumnType("TEXT(64)")
            .IsRequired();
        builder.HasIndex(x => x.ClientEventId)
            .IsUnique()
            .HasDatabaseName("UX_CloudOutbox_ClientEventId");

        builder.Property(x => x.Type)
            .HasMaxLength(CloudOutboxEvent.MaxTypeLength)
            .HasColumnType("TEXT(32)")
            .IsRequired();

        builder.Property(x => x.PayloadJson).HasColumnType("TEXT").IsRequired();

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.CreatedAtUtc).HasDatabaseName("IX_CloudOutbox_CreatedAtUtc");

        builder.Property(x => x.NextAttemptAtUtc).IsRequired();
        builder.HasIndex(x => new { x.SentAtUtc, x.NextAttemptAtUtc })
            .HasDatabaseName("IX_CloudOutbox_Due");

        builder.Property(x => x.AttemptCount).IsRequired();

        builder.Property(x => x.SentAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.FailedAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.LastError)
            .HasMaxLength(CloudOutboxEvent.MaxErrorLength)
            .HasColumnType("TEXT(512)");
    }
}
