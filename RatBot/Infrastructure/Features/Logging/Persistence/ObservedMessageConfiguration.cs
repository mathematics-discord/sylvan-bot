using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RatBot.Domain.Features.Logging;

namespace RatBot.Infrastructure.Features.Logging.Persistence;

public sealed class ObservedMessageConfiguration : IEntityTypeConfiguration<ObservedMessage>
{
    public void Configure(EntityTypeBuilder<ObservedMessage> builder)
    {
        builder.ToTable("observed_messages");
        builder.HasKey(message => new { message.GuildId, message.OriginalMessageId });
        builder.Property(message => message.OriginalMessageId).HasColumnName("original_message_id")
            .ValueGeneratedNever();
        builder.Property(message => message.GuildId).HasColumnName("guild_id").IsRequired().ValueGeneratedNever();
        builder.Property(message => message.ChannelId).HasColumnName("channel_id").IsRequired();
        builder.Property(message => message.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(message => message.ObservedAtUtc).HasColumnName("observed_at_utc").IsRequired();
        builder.HasIndex(message => new { message.GuildId, message.ChannelId });
        builder.HasIndex(message => message.AuthorId);
    }
}