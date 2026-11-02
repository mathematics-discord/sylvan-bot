using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Automod.Persistence;

public sealed class AutomodSettingsConfiguration : IEntityTypeConfiguration<AutomodSettings>
{
    public void Configure(EntityTypeBuilder<AutomodSettings> builder)
    {
        builder.ToTable("automod_settings");
        builder.HasKey(settings => new { settings.GuildId, settings.Action });

        builder.Property(settings => settings.GuildId)
            .ValueGeneratedNever();
    }
}
