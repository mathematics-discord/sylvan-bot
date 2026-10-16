using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.TicketCollection;

public sealed class CapturedAuditEntryConfiguration : IEntityTypeConfiguration<CapturedAuditEntry>
{
    public void Configure(EntityTypeBuilder<CapturedAuditEntry> builder)
    {
        builder.ToTable("CapturedAuditEntries", table =>
        {
            table.HasCheckConstraint(
                "CK_CapturedAuditEntries_VoiceMutePair",
                "(\"VoiceMuteBefore\" IS NULL) = (\"VoiceMuteAfter\" IS NULL)");

            table.HasCheckConstraint(
                "CK_CapturedAuditEntries_VoiceDeafenPair",
                "(\"VoiceDeafenBefore\" IS NULL) = (\"VoiceDeafenAfter\" IS NULL)");

            table.HasCheckConstraint(
                "CK_CapturedAuditEntries_TimeoutPresence",
                "\"HasTimeoutChange\" OR (\"TimeoutBeforeUtc\" IS NULL AND \"TimeoutAfterUtc\" IS NULL)");
        });

        builder.HasKey(entry => new { entry.GuildId, entry.AuditId });

        builder.Property(entry => entry.GuildId)
            .ValueGeneratedNever();

        builder.Property(entry => entry.AuditId)
            .ValueGeneratedNever();

        builder.Property(entry => entry.Action)
            .HasConversion<int>();

        builder.Property(entry => entry.OccurredAtUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(entry => entry.CapturedAtUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(entry => entry.Reason)
            .HasColumnType("text");

        builder.PrimitiveCollection(entry => entry.AddedRoleIds)
            .HasColumnType("bigint[]")
            .ElementType()
            .HasConversion<long>();

        builder.PrimitiveCollection(entry => entry.RemovedRoleIds)
            .HasColumnType("bigint[]")
            .ElementType()
            .HasConversion<long>();

        builder.Property(entry => entry.TimeoutBeforeUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(entry => entry.TimeoutAfterUtc)
            .HasColumnType("timestamp with time zone");
    }
}