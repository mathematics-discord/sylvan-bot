using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RatBot.Domain.Moderation;

namespace RatBot.Infrastructure.Persistence.Configurations;

public class AutobannedUserConfiguration : IEntityTypeConfiguration<AutobannedUser>
{
    public void Configure(EntityTypeBuilder<AutobannedUser> builder)
    {
        builder.ToTable("AutobannedUsers");

        builder.HasKey(x => new { x.GuildId, x.BannedUser });

        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint");

        builder.Property(x => x.BannedUser).IsRequired().HasConversion<long>().HasColumnType("bigint");

        builder.Property(x => x.Moderator).IsRequired().HasConversion<long>().HasColumnType("bigint");

        builder.Property(x => x.RegisteredAtUtc).IsRequired().HasColumnType("timestamp with time zone");

        builder.HasIndex(x => x.BannedUser);
        builder.HasIndex(x => x.Moderator);
        builder.HasIndex(x => x.GuildId);
    }
}