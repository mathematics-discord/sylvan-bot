using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.RoleColours;

public sealed class RoleColourOptionConfiguration : IEntityTypeConfiguration<RoleColourOption>
{
    public void Configure(EntityTypeBuilder<RoleColourOption> builder)
    {
        builder.ToTable(
            "RoleColourOptions",
            table =>
            {
                table.HasCheckConstraint("CK_RoleColourOptions_DifferentRoles",
                    "\"SourceRoleId\" <> \"DisplayRoleId\"");
            }
        );

        builder.HasKey(x => x.OptionId);

        builder.Property(x => x.OptionId).ValueGeneratedOnAdd();

        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.SourceRoleId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.DisplayRoleId).IsRequired().HasConversion<long>().HasColumnType("bigint");
        builder.Property(x => x.CreatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(x => x.UpdatedAtUtc).IsRequired().HasColumnType("timestamp with time zone");

        builder
            .HasIndex(x => new
            {
                x.GuildId,
                x.SourceRoleId,
                x.DisplayRoleId
            })
            .IsUnique();
    }
}