using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Automod.Persistence;

public sealed class AutomodBanConfiguration : IEntityTypeConfiguration<AutomodBan>
{
    public void Configure(EntityTypeBuilder<AutomodBan> builder)
    {
        builder.ToTable("automod_bans");
        builder.HasKey(value => value.Id);
        builder.Ignore(value => value.Reason);
        builder.Ignore(value => value.ExpiresAt);

        builder.Property(value => value.Status).HasMaxLength(32);

        builder.HasIndex(value => new { value.GuildId, value.UserId })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Pending', 'Active', 'Removing')");

        builder.HasIndex(value => new { value.Status, value.NextCheckAt });
    }
}