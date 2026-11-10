namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class TicketPersistence : IEntityTypeConfiguration<Ticket>,
    IEntityTypeConfiguration<TicketConfiguration>, IEntityTypeConfiguration<TicketModerator>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets", table =>
        {
            table.HasCheckConstraint("CK_tickets_number", "\"Number\" > 0");
            table.HasCheckConstraint("CK_tickets_identity", "\"GuildId\" > 0 AND \"SubjectUserId\" > 0 AND \"ModeratorUserId\" > 0");
            table.HasCheckConstraint("CK_tickets_duration", "(\"DurationSeconds\" IS NULL OR \"DurationSeconds\" > 0 OR \"Status\" <> 'Active') AND (\"Action\" <> 'Timeout' OR (\"DurationSeconds\" IS NOT NULL AND \"ExpiresAtUtc\" IS NOT NULL))");
            table.HasCheckConstraint("CK_tickets_note_duration", "\"Action\" <> 'Note' OR \"DurationSeconds\" IS NULL");
            table.HasCheckConstraint("CK_tickets_approval", "\"ApprovedByUserId\" IS NULL OR \"ApprovedAtUtc\" IS NOT NULL");
            table.HasCheckConstraint("CK_tickets_audit", "\"SourceAuditId\" IS NULL OR (\"SourceAuditId\" > 0 AND \"LastAuditId\" IS NOT NULL AND \"LastAuditId\" >= \"SourceAuditId\")");
            table.HasCheckConstraint("CK_tickets_role", "\"Action\" <> 'Role' OR (\"RoleId\" IS NOT NULL AND \"RoleId\" > 0)");
            table.HasCheckConstraint("CK_tickets_supersession", "\"Status\" <> 'Superseded' OR (\"SupersededByNumber\" IS NOT NULL AND \"SupersededByNumber\" > 0 AND \"SupersededByNumber\" <> \"Number\")");
        });
        builder.HasKey(ticket => ticket.Id);
        builder.Property(ticket => ticket.Action)
            .HasConversion<string>();
        builder.Property(ticket => ticket.Status)
            .HasConversion<string>();
        builder.Property(ticket => ticket.Version)
            .IsConcurrencyToken();
        builder.Property(ticket => ticket.Comment)
            .HasMaxLength(Ticket.CommentLimit);
        builder.Property(ticket => ticket.AuditReason)
            .HasMaxLength(512);
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.Number })
            .IsUnique();
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SourceAuditId, ticket.Action })
            .IsUnique()
            .HasFilter("\"SourceAuditId\" IS NOT NULL AND \"RoleId\" IS NULL");
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SourceAuditId, ticket.Action, ticket.RoleId })
            .IsUnique()
            .HasFilter("\"SourceAuditId\" IS NOT NULL AND \"RoleId\" IS NOT NULL");
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SubjectUserId, ticket.Hidden, ticket.Id });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.Status, ticket.ExpiresAtUtc });
        builder.HasIndex(ticket => new { ticket.ModeratorUserId, ticket.CreatedAtUtc, ticket.Id });
        builder.OwnsOne(ticket => ticket.Pending, pending =>
        {
            pending.ToTable("ticket_operations", table => table.HasCheckConstraint("CK_ticket_operations_command",
                "(\"Kind\" IN ('RemoveBan', 'RemoveRole') OR (\"Kind\" = 'SetTimeoutExpiry' AND \"ExpiryUtc\" IS NOT NULL)) AND \"Id\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
            pending.Property(operation => operation.Kind)
                .HasConversion<string>();
            pending.WithOwner()
                .HasForeignKey("TicketId");
            pending.HasKey("TicketId");
        });
        builder.OwnsMany(ticket => ticket.Publications, publication =>
        {
            publication.ToTable("ticket_publications");
            publication.WithOwner()
                .HasForeignKey("TicketId");
            publication.HasKey("TicketId", nameof(TicketPublication.ChannelId), nameof(TicketPublication.MessageId));
            publication.HasIndex(value => new { value.ChannelId, value.MessageId });
        });
        builder.OwnsMany(ticket => ticket.ModificationHistory, history =>
        {
            history.ToTable("ticket_history");
            history.WithOwner()
                .HasForeignKey("TicketId");
            history.HasKey("TicketId", nameof(TicketHistoryEntry.Sequence));
            history.Property(entry => entry.Sequence)
                .ValueGeneratedNever();
            history.Property(entry => entry.Action)
                .HasConversion<string>();
            history.Property<string>("HistoricalText");
            history.Property<string>("Description");
            history.Property<DateTimeOffset?>("_timestamp")
                .HasColumnName("Timestamp");
            history.Property<ulong?>("_moderatorId")
                .HasColumnName("ModeratorId");
            history.Property<string>("_initiatorKind")
                .HasColumnName("InitiatorKind");
            history.Property<long?>("_durationSeconds")
                .HasColumnName("DurationSeconds");
        });
    }

    public void Configure(EntityTypeBuilder<TicketConfiguration> builder)
    {
        builder.ToTable("ticket_configurations", table =>
        {
            table.HasCheckConstraint("CK_ticket_configurations_reminder", "\"ReminderIntervalSeconds\" >= 60");
            table.HasCheckConstraint("CK_ticket_configurations_enabled_channel", "NOT \"Enabled\" OR \"DestinationChannelId\" IS NOT NULL");
        });
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
