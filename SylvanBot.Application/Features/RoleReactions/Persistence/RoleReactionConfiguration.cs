using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.RoleReactions.Persistence;

/// <summary>Configures persisted reaction role mappings.</summary>
public sealed class RoleReactionConfiguration : IEntityTypeConfiguration<RoleReaction>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<RoleReaction> builder)
    {
        builder.ToTable("RoleReactions");

        builder.HasKey(mapping => new
        {
            mapping.GuildId,
            mapping.MessageId,
            mapping.EmojiKey,
        });

        builder.Property(mapping => mapping.GuildId).ValueGeneratedNever();

        builder.Property(mapping => mapping.ChannelId).IsRequired();

        builder.Property(mapping => mapping.MessageId).ValueGeneratedNever();

        builder.Property(mapping => mapping.EmojiKey).HasMaxLength(256);

        builder.Property(mapping => mapping.EmojiText).IsRequired().HasMaxLength(256);

        builder.Property(mapping => mapping.RoleId).IsRequired();
    }
}