using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SylvanBot.Application.Features.Announcements;

public sealed class AnnouncementSettingsConfiguration : IEntityTypeConfiguration<AnnouncementSettings>
{
    public void Configure(EntityTypeBuilder<AnnouncementSettings> builder)
    {
        builder.HasKey(settings => settings.GuildId);
        builder.Property(settings => settings.GuildId)
            .ValueGeneratedNever();
    }
}
