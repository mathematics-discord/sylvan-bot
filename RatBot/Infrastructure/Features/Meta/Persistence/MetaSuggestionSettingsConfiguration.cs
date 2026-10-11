using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Infrastructure.Features.Meta.Persistence;

public sealed class MetaSuggestionSettingsConfiguration : IEntityTypeConfiguration<MetaSuggestionSettings>
{
    public void Configure(EntityTypeBuilder<MetaSuggestionSettings> builder)
    {
        builder.ToTable("MetaSuggestionSettings");
        builder.HasKey(x => x.GuildId);

        builder.Property(x => x.GuildId).HasColumnType("bigint").HasConversion<long>().ValueGeneratedNever();
        builder.Property(x => x.SuggestionsForumChannelId).HasColumnType("bigint").HasConversion<long>();
        builder.Property(x => x.ProposalsForumChannelId).HasColumnType("bigint").HasConversion<long>();
        builder.Property(x => x.CabinetRoleId).HasColumnType("bigint").HasConversion<long>();
        builder.Property(x => x.CabinetChairRoleId).HasColumnType("bigint").HasConversion<long>();
        builder.Property(x => x.CommitteeRoleId).HasColumnType("bigint").HasConversion<long>();
    }
}