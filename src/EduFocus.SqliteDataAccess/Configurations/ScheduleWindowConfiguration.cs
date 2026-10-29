using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class ScheduleWindowConfiguration : IEntityTypeConfiguration<ScheduleWindow>
{
    public void Configure(EntityTypeBuilder<ScheduleWindow> builder)
    {
        builder.ToTable("ScheduleWindows", table =>
        {
            table.HasCheckConstraint(
                "CK_ScheduleWindows_DaysMask",
                $"\"DaysMask\" BETWEEN 1 AND {(int)ScheduleDays.All}");
            table.HasCheckConstraint(
                "CK_ScheduleWindows_StartMinutes",
                "\"StartMinutes\" BETWEEN 0 AND 1439");
            table.HasCheckConstraint(
                "CK_ScheduleWindows_EndMinutes",
                "\"EndMinutes\" BETWEEN 1 AND 1440");
            table.HasCheckConstraint(
                "CK_ScheduleWindows_Range",
                "\"EndMinutes\" > \"StartMinutes\"");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Days)
            .HasColumnName("DaysMask")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.StartMinutes).IsRequired();
        builder.Property(x => x.EndMinutes).IsRequired();
        builder.Property(x => x.Label).HasMaxLength(ScheduleWindow.MaxLabelLength).HasColumnType("TEXT(100)");
        builder.Property(x => x.IsEnabled).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.IsEnabled)
            .HasDatabaseName("IX_ScheduleWindows_IsEnabled");
    }
}
