using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Automod.Persistence;

public sealed class AutomodRuleConfiguration : IEntityTypeConfiguration<AutomodRule>
{
    public void Configure(EntityTypeBuilder<AutomodRule> builder)
    {
        builder.ToTable(
            "automod_rules",
            table =>
            {
                table.HasCheckConstraint("CK_automod_rules_match_type", "\"MatchType\" IN (0, 1)");

#pragma warning disable SA1118
                table.HasCheckConstraint(
                    "CK_automod_rules_action_duration",
                    "(\"Action\" IN (0, 1) AND \"DurationSeconds\" IS NULL) OR "
                    + "(\"Action\" = 2 AND \"DurationSeconds\" IS NOT NULL AND \"DurationSeconds\" BETWEEN 1 AND 2419200) OR "
                    + "(\"Action\" = 3 AND (\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0))");
#pragma warning restore SA1118
            });

        builder.HasKey(rule => rule.Id);

        builder.Property(rule => rule.Id).UseIdentityByDefaultColumn();
        builder.Property(rule => rule.Pattern).IsRequired();

        builder.HasIndex(rule => rule.GuildId);
    }
}