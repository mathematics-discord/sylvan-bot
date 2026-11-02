using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.MessageCache;

public sealed class CachedMessageConfiguration : IEntityTypeConfiguration<CachedMessage>
{
    public void Configure(EntityTypeBuilder<CachedMessage> builder)
    {
        builder.ToTable(
            "cached_messages",
            table => table.HasCheckConstraint("ck_cached_messages_expiry", "expires_at_utc = created_at_utc + interval '24 hours'"));

        builder.HasKey(message => new
        {
            message.BotId,
            message.GuildId,
            message.MessageId,
        });

        builder.Property(message => message.BotId).HasColumnName("bot_id");

        builder.Property(message => message.GuildId).HasColumnName("guild_id");

        builder.Property(message => message.MessageId).HasColumnName("message_id");

        builder.Property(message => message.CreatedAtUtc).HasColumnName("created_at_utc");

        builder.Property(message => message.ExpiresAtUtc).HasColumnName("expires_at_utc");

        builder.Property(message => message.SnapshotJson).HasColumnName("snapshot").HasColumnType("jsonb");

        builder.HasIndex(message => message.ExpiresAtUtc);

        builder.HasIndex(message => new
        {
            message.BotId,
            message.GuildId,
            message.ExpiresAtUtc,
        });
    }
}