using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Quorum;

public sealed class QuorumPersistence :
    IEntityTypeConfiguration<QuorumConfiguration>,
    IEntityTypeConfiguration<QuorumVoterRole>
{
    public void Configure(EntityTypeBuilder<QuorumConfiguration> builder)
    {
        builder.ToTable(
            "quorum_configurations",
            "public",
            table =>
            {
                table.HasCheckConstraint("ck_quorum_configurations_channel_kind", "channel_kind IN ('text', 'forum')");
                table.HasCheckConstraint("ck_quorum_configurations_proportion", "proportion > 0 AND proportion <= 1");
            });

        builder.HasKey(configuration => configuration.Id)
           .HasName("pk_quorum_configurations");

        builder.HasAlternateKey(configuration => new { configuration.GuildId, configuration.ChannelId })
           .HasName("ak_quorum_configurations_guild_id_channel_id");

        builder.Property(configuration => configuration.Id)
           .HasColumnName("id")
           .ValueGeneratedNever();

        builder.Property(configuration => configuration.GuildId)
           .HasColumnName("guild_id");

        builder.Property(configuration => configuration.ChannelId)
           .HasColumnName("channel_id");

        builder.Property(configuration => configuration.ChannelKind)
           .HasColumnName("channel_kind")
           .HasMaxLength(16)
           .IsRequired();

        builder.Property(configuration => configuration.Proportion)
           .HasColumnName("proportion")
           .HasPrecision(precision: 9, scale: 8);

        builder.HasMany(configuration => configuration.VoterRoles)
           .WithOne()
           .HasForeignKey(role => role.QuorumConfigurationId)
           .OnDelete(DeleteBehavior.Cascade)
           .HasConstraintName("fk_quorum_voter_roles_quorum_configurations");
    }

    public void Configure(EntityTypeBuilder<QuorumVoterRole> builder)
    {
        builder.ToTable("quorum_voter_roles", "public");

        builder.HasKey(role => new { role.QuorumConfigurationId, role.RoleId })
           .HasName("pk_quorum_voter_roles");

        builder.Property(role => role.QuorumConfigurationId)
           .HasColumnName("quorum_configuration_id");

        builder.Property(role => role.RoleId)
           .HasColumnName("role_id");
    }
}