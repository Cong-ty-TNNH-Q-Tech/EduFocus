using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class ActivitySampleConfiguration : IEntityTypeConfiguration<ActivitySample>
{
    public void Configure(EntityTypeBuilder<ActivitySample> builder)
    {
        builder.ToTable("ActivitySamples", table =>
        {
            table.HasCheckConstraint(
                "CK_ActivitySamples_UrlSource",
                "\"UrlSource\" IN ('UIA', 'Extension', 'None')");
            table.HasCheckConstraint(
                "CK_ActivitySamples_Category",
                "\"Category\" IN ('Study', 'Distraction', 'Neutral', 'Unknown')");
            table.HasCheckConstraint(
                "CK_ActivitySamples_Duration",
                "\"DurationSeconds\" >= 0 AND \"IdleSeconds\" >= 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.HasOne<StudySession>()
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.SampledAtUtc).IsRequired();
        builder.Property(x => x.DurationSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.AppName).HasMaxLength(ActivitySample.MaxAppNameLength).HasColumnType("TEXT(128)");
        builder.Property(x => x.ProcessName).HasMaxLength(ActivitySample.MaxProcessNameLength).HasColumnType("TEXT(128)");
        builder.Property(x => x.WindowTitle).HasMaxLength(ActivitySample.MaxWindowTitleLength).HasColumnType("TEXT(512)");
        builder.Property(x => x.Url).HasMaxLength(2048).HasColumnType("TEXT(2048)");
        builder.Property(x => x.Domain).HasMaxLength(256).HasColumnType("TEXT(256)");

        builder.Property(x => x.UrlSource)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .HasDefaultValue(UrlSource.None)
            .ValueGeneratedNever();

        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnType("TEXT(32)")
            .HasDefaultValue(ActivityCategory.Unknown)
            .ValueGeneratedNever();

        builder.Property(x => x.IsForeground).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.IsOnlineClass).HasDefaultValue(false).ValueGeneratedNever();
        builder.Property(x => x.IdleSeconds).HasDefaultValue(0).ValueGeneratedNever();
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.SessionId, x.SampledAtUtc })
            .HasDatabaseName("IX_ActivitySamples_SessionId_SampledAtUtc");

        builder.HasIndex(x => x.Domain)
            .HasDatabaseName("IX_ActivitySamples_Domain");

        builder.HasIndex(x => x.Category)
            .HasDatabaseName("IX_ActivitySamples_Category");
    }
}
