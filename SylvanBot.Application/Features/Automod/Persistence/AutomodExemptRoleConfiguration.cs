using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Automod.Persistence;

public sealed class AutomodExemptRoleConfiguration : IEntityTypeConfiguration<AutomodExemptRole>
{
    public void Configure(EntityTypeBuilder<AutomodExemptRole> builder)
    {
        builder.ToTable("automod_exempt_roles");

        builder.HasKey(role => new { role.GuildId, role.RoleId });

        builder.Property(role => role.GuildId)
            .ValueGeneratedNever();

        builder.Property(role => role.RoleId)
            .ValueGeneratedNever();
    }
}