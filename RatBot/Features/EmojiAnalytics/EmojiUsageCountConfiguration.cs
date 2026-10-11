using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.EmojiAnalytics;

public sealed class EmojiUsageCountConfiguration : IEntityTypeConfiguration<EmojiUsageCount>
{
    public void Configure(EntityTypeBuilder<EmojiUsageCount> builder)
    {
        builder.ToTable("EmojiUsageCounts");
        builder.HasKey(x => new { x.GuildId, x.EmojiId });
        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint")
            .ValueGeneratedNever();
        builder.Property(x => x.EmojiId).ValueGeneratedNever();
        builder.Property(x => x.ReactionUsageCount).HasColumnType("int");
    }
}