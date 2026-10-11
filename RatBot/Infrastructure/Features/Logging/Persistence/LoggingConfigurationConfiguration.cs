using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RatBot.Domain.Features.Logging;

namespace RatBot.Infrastructure.Features.Logging.Persistence;

public sealed class LoggingConfigurationConfiguration : IEntityTypeConfiguration<LoggingConfiguration>
{
    public void Configure(EntityTypeBuilder<LoggingConfiguration> builder)
    {
        builder.ToTable("logging_configurations");
        builder.HasKey(configuration => configuration.GuildId);
        builder.Property(configuration => configuration.GuildId).HasColumnName("guild_id").ValueGeneratedNever();
        builder.Property(configuration => configuration.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(configuration => configuration.DeleteLogChannelId).HasColumnName("delete_log_channel_id");
        builder.Property(configuration => configuration.EditLogChannelId).HasColumnName("edit_log_channel_id");
        builder.Property(configuration => configuration.EvidenceRetentionPeriod)
            .HasColumnName("evidence_retention_period").IsRequired();
    }
}