namespace RatBot.Features.Logging.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class LoggingPersistence :
    IEntityTypeConfiguration<LoggingConfiguration>,
    IEntityTypeConfiguration<LoggingExcludedChannel>
{
    public void Configure(EntityTypeBuilder<LoggingConfiguration> builder)
    {
        builder.ToTable("logging_configurations");
        builder.HasKey(configuration => configuration.GuildId);
        builder.Property(configuration => configuration.GuildId)
            .HasColumnName("guild_id")
            .ValueGeneratedNever();

        builder.Property(configuration => configuration.Enabled)
            .HasColumnName("enabled")
            .IsRequired();

        builder.Property(configuration => configuration.DeleteLogChannelId)
            .HasColumnName("delete_log_channel_id");

        builder.Property(configuration => configuration.EditLogChannelId)
            .HasColumnName("edit_log_channel_id");

        builder.Property(configuration => configuration.LegacyEvidenceRetentionPeriod)
            .HasColumnName("evidence_retention_period")
            .IsRequired();
    }

    public void Configure(EntityTypeBuilder<LoggingExcludedChannel> builder)
    {
        builder.ToTable("logging_excluded_channels");
        builder.HasKey(ch => new { ch.GuildId, ch.ChannelId });
        builder.Property(ch => ch.GuildId)
            .HasColumnName("guild_id");

        builder.Property(ch => ch.ChannelId)
            .HasColumnName("channel_id");

        builder.Property(ch => ch.ExcludedAtUtc)
            .HasColumnName("excluded_at_utc")
            .IsRequired();
    }
}