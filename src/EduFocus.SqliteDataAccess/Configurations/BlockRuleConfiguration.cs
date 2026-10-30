using EduFocus.Domain.Entities;
using EduFocus.Domain.Enums;
using EduFocus.SqliteDataAccess.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduFocus.SqliteDataAccess.Configurations;

internal sealed class BlockRuleConfiguration : IEntityTypeConfiguration<BlockRule>
{
    public void Configure(EntityTypeBuilder<BlockRule> builder)
    {
        builder.ToTable("BlockRules", table =>
        {
            table.HasCheckConstraint("CK_BlockRules_RuleType", "\"RuleType\" IN ('Domain', 'App', 'Url')");
            table.HasCheckConstraint("CK_BlockRules_MatchType", "\"MatchType\" IN ('Exact', 'Suffix', 'Contains')");
            table.HasCheckConstraint(
                "CK_BlockRules_Category",
                "\"Category\" IN ('Chatbot', 'Social', 'Game', 'Study', 'System', 'Custom')");
            table.HasCheckConstraint("CK_BlockRules_Effect", "\"Effect\" IN ('Block', 'Allow')");
            table.HasCheckConstraint(
                "CK_BlockRules_EnforcementStatus",
                "\"EnforcementStatus\" IN ('Enforced', 'NotEnforced')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.RuleType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.MatchType)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .IsRequired();

        builder.Property(x => x.Value).HasMaxLength(BlockRule.MaxValueLength).HasColumnType("TEXT(512)").IsRequired();
        builder.Property(x => x.NormalizedValue).HasMaxLength(BlockRule.MaxValueLength).HasColumnType("TEXT(512)").IsRequired();

        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasColumnType("TEXT(32)")
            .IsRequired();

        builder.Property(x => x.Effect)
            .HasConversion<string>()
            .HasMaxLength(8)
            .HasColumnType("TEXT(8)")
            .IsRequired();

        builder.Property(x => x.IsEnabled).HasDefaultValue(true).ValueGeneratedNever();
        builder.Property(x => x.IsBuiltIn).HasDefaultValue(false).ValueGeneratedNever();

        builder.Property(x => x.EnforcementStatus)
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasColumnType("TEXT(16)")
            .HasDefaultValue(EnforcementStatus.Enforced)
            .ValueGeneratedNever();

        builder.Property(x => x.ExpiresAtUtc)
            .HasConversion<NullableUtcDateTimeConverter>()
            .HasColumnType("TEXT(28)");

        builder.Property(x => x.Note).HasMaxLength(BlockRule.MaxNoteLength).HasColumnType("TEXT(512)");
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => new { x.RuleType, x.MatchType, x.NormalizedValue, x.Effect })
            .IsUnique()
            .HasDatabaseName("UX_BlockRules_RuleType_MatchType_NormalizedValue_Effect");

        builder.HasIndex(x => new { x.Effect, x.IsEnabled })
            .HasDatabaseName("IX_BlockRules_Effect_IsEnabled");

        builder.HasIndex(x => x.Category)
            .HasDatabaseName("IX_BlockRules_Category");

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("IX_BlockRules_ExpiresAtUtc");
    }
}
