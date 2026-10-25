namespace SylvanBot.Features.Moderation.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ImageSpamSettingsConfiguration : IEntityTypeConfiguration<ImageSpamSettings>
{
    public void Configure(EntityTypeBuilder<ImageSpamSettings> builder)
    {
        builder.ToTable(
            "ImageSpamSettings",
            table =>
            {
                table.HasCheckConstraint("CK_ImageSpamSettings_RequiredChannelCount_Positive",
                    "\"RequiredChannelCount\" > 0");

                table.HasCheckConstraint("CK_ImageSpamSettings_RequiredAttachmentCount_Positive",
                    "\"RequiredAttachmentCount\" > 0");

                table.HasCheckConstraint("CK_ImageSpamSettings_BurstDurationSeconds_Positive",
                    "\"BurstDurationSeconds\" > 0");
            }
        );

        builder.HasKey(x => x.GuildId);
        builder.Property(x => x.GuildId).IsRequired().HasConversion<long>().HasColumnType("bigint")
            .ValueGeneratedNever();
        builder.Property(x => x.RequiredChannelCount).IsRequired();
        builder.Property(x => x.RequiredAttachmentCount).IsRequired();
        builder.Property(x => x.BurstDurationSeconds).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired();
    }
}