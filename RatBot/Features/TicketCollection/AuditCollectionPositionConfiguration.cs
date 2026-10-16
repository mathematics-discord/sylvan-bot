using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.TicketCollection;

public sealed class AuditCollectionPositionConfiguration : IEntityTypeConfiguration<AuditCollectionPosition>
{
    public void Configure(EntityTypeBuilder<AuditCollectionPosition> builder)
    {
        builder.ToTable("AuditCollectionPositions");
        builder.HasKey(position => position.GuildId);

        builder.Property(position => position.GuildId)
            .ValueGeneratedNever();

        builder.Property(position => position.CaptureStartedAtUtc)
            .HasColumnType("timestamp with time zone");
    }
}