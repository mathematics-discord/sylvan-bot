using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Activity;

public sealed class ActivityMapping : IEntityTypeConfiguration<ActivitySettings>, IEntityTypeConfiguration<ActivityWeight>,
    IEntityTypeConfiguration<ActivityMember>, IEntityTypeConfiguration<ActivityAward>
{
    public void Configure(EntityTypeBuilder<ActivitySettings> builder)
    {
        builder.ToTable("activity_settings", table =>
        {
            table.HasCheckConstraint("CK_activity_cap", "\"MaxDaily\" >= 0");
            table.HasCheckConstraint("CK_activity_scores", "(\"Role1Id\" = 0 AND \"Role2Id\" = 0 AND \"Role1Score\" = 0 AND \"Role2Score\" = 0) OR (\"Role1Id\" <> 0 AND \"Role2Id\" <> 0 AND \"Role1Id\" <> \"Role2Id\" AND \"Role1Score\" > \"Role2Score\" AND \"Role2Score\" > 1000)");
        });
        builder.HasKey(row => row.GuildId);
        builder.Property(row => row.GuildId).ValueGeneratedNever();
    }

    public void Configure(EntityTypeBuilder<ActivityWeight> builder)
    {
        builder.ToTable("activity_weights", table => table.HasCheckConstraint("CK_activity_weight", "\"Weight\" >= 0 AND \"Weight\" <= 100"));
        builder.HasKey(row => new { row.GuildId, row.ChannelId });
    }

    public void Configure(EntityTypeBuilder<ActivityMember> builder)
    {
        builder.ToTable("activity_members", table => table.HasCheckConstraint("CK_activity_xp", "\"Xp\" >= 0 AND (NOT \"OptedOut\" OR \"Xp\" = 0)"));
        builder.HasKey(row => new { row.GuildId, row.UserId });
    }

    public void Configure(EntityTypeBuilder<ActivityAward> builder)
    {
        builder.ToTable("activity_awards", table => table.HasCheckConstraint("CK_activity_award", "\"Xp\" > 0"));
        builder.HasKey(row => new { row.GuildId, row.MessageId });
        builder.HasIndex(row => new { row.GuildId, row.UserId, row.AwardedAt });
        builder.HasIndex(row => row.AwardedAt);
    }
}
