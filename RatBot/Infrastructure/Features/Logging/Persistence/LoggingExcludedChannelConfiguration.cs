using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RatBot.Domain.Features.Logging;

namespace RatBot.Infrastructure.Features.Logging.Persistence;

public sealed class LoggingExcludedChannelConfiguration : IEntityTypeConfiguration<LoggingExcludedChannel>
{
    public void Configure(EntityTypeBuilder<LoggingExcludedChannel> builder)
    {
        builder.ToTable("logging_excluded_channels");
        builder.HasKey(channel => new { channel.GuildId, channel.ChannelId });
        builder.Property(channel => channel.GuildId).HasColumnName("guild_id");
        builder.Property(channel => channel.ChannelId).HasColumnName("channel_id");
        builder.Property(channel => channel.ExcludedAtUtc).HasColumnName("excluded_at_utc").IsRequired();
    }
}