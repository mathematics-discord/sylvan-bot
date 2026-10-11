using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Infrastructure.Features.Meta.Persistence;

public sealed class MetaProposalStateConfiguration : IEntityTypeConfiguration<MetaProposalState>
{
    public void Configure(EntityTypeBuilder<MetaProposalState> builder)
    {
        builder.ToTable("MetaProposalStates");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProposalTitle).HasMaxLength(MetaProposalState.MaxTitleLength);
        builder.Property(x => x.Summary).HasMaxLength(1500);
        builder.Property(x => x.Motivation).HasMaxLength(1950);
        builder.Property(x => x.Specification).HasMaxLength(1950);
        builder.Property(x => x.VetoReason).HasMaxLength(1950);

        builder.HasIndex(x => x.GuildId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.GuildId, x.SuggestionThreadChannelId }).IsUnique();
        builder.HasIndex(x => new { x.GuildId, x.PollMessageId });
        builder.HasIndex(x => new { x.GuildId, x.ProposalThreadChannelId });
        builder.HasIndex(x => new { x.Status, x.PollExpiresAtUtc });
    }
}