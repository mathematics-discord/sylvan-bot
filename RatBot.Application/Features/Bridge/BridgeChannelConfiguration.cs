namespace RatBot.Features.Bridge;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class BridgeChannelConfiguration : IEntityTypeConfiguration<BridgeChannel>
{
    public void Configure(EntityTypeBuilder<BridgeChannel> builder)
    {
        builder.ToTable("bridge_channels");
        builder.HasKey(channel => new { channel.GuildId, channel.ChannelId });
        builder.Property(channel => channel.GuildId)
            .HasColumnName("guild_id")
            .ValueGeneratedNever();

        builder.Property(channel => channel.ChannelId)
            .HasColumnName("channel_id")
            .ValueGeneratedNever();
    }
}