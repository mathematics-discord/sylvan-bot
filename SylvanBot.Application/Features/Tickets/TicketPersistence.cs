namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class TicketPersistence :
    IEntityTypeConfiguration<Ticket>,
    IEntityTypeConfiguration<TicketConfiguration>,
    IEntityTypeConfiguration<TicketModerator>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable(
            "tickets",
            table =>
            {
                table.HasCheckConstraint("CK_tickets_number", "\"Number\" > 0");

                table.HasCheckConstraint(
                    "CK_tickets_note_duration",
                    "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");

                table.HasCheckConstraint(
                    "CK_tickets_observed_duration",
                    "\"SourceAuditId\" IS NULL OR ((\"DurationKnown\" OR \"DurationSeconds\" IS NULL) "
                    + "AND (\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0))");

                table.HasCheckConstraint(
                    "CK_tickets_pending_operation",
                    "(\"PendingOperationId\" IS NULL AND \"PendingRequestedAtUtc\" IS NULL) "
                    + "OR (\"SourceAuditId\" IS NOT NULL AND \"PendingOperationId\" IS NOT NULL AND \"PendingRequestedAtUtc\" IS NOT NULL)");
            });

        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Action)
            .HasConversion<string>();

        builder.Property(ticket => ticket.Status)
            .HasConversion<string>();

        builder.Property(ticket => ticket.Completion)
            .HasConversion<string>();

        builder.Property(ticket => ticket.AuditReason)
            .HasMaxLength(512);

        builder.Property(ticket => ticket.Comment)
            .HasMaxLength(Ticket.CommentLimit);

        builder.Property(ticket => ticket.Version)
            .IsConcurrencyToken();

        builder.HasIndex(ticket => new { ticket.GuildId, ticket.Number })
            .IsUnique();
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SubjectUserId, ticket.Hidden, ticket.Id });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.ChannelId, ticket.MessageId });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.PreviousChannelId, ticket.PreviousMessageId });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SourceAuditId })
            .IsUnique()
            .HasFilter("\"SourceAuditId\" IS NOT NULL");

        builder.HasIndex(ticket => new { ticket.ModeratorUserId, ticket.CreatedAtUtc, ticket.Id })
            .HasFilter("\"Completion\" = 'Pending'");

        builder.HasIndex(ticket => new { ticket.GuildId, ticket.Status, ticket.ExpiresAtUtc })
            .HasFilter("\"SourceAuditId\" IS NOT NULL");
    }

    public void Configure(EntityTypeBuilder<TicketConfiguration> builder)
    {
        builder.ToTable("ticket_configurations", table =>
            table.HasCheckConstraint("CK_ticket_configurations_reminder", "\"ReminderIntervalSeconds\" >= 60"));

        builder.HasKey(configuration => configuration.GuildId);

        builder.Property(configuration => configuration.GuildId)
            .ValueGeneratedNever();

        builder.Property(configuration => configuration.ReminderIntervalSeconds)
            .HasDefaultValue(86400);
    }

    public void Configure(EntityTypeBuilder<TicketModerator> builder)
    {
        builder.ToTable("ticket_moderators");
        builder.HasKey(moderator => moderator.UserId);

        builder.Property(moderator => moderator.UserId)
            .ValueGeneratedNever();

        builder.Property(moderator => moderator.Version)
            .IsConcurrencyToken();
    }
}
