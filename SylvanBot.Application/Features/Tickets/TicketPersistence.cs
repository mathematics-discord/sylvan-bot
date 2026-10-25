namespace SylvanBot.Application.Features.Tickets;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class TicketPersistence :
    IEntityTypeConfiguration<Ticket>,
    IEntityTypeConfiguration<TicketConfiguration>
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
            });

        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Action)
            .HasConversion<string>();

        builder.Property(ticket => ticket.Status)
            .HasConversion<string>();

        builder.Property(ticket => ticket.Comment)
            .HasColumnType("text");

        builder.Property(ticket => ticket.Version)
            .IsConcurrencyToken();

        builder.HasIndex(ticket => new { ticket.GuildId, ticket.Number })
            .IsUnique();
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.SubjectUserId, ticket.Hidden, ticket.Id });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.ChannelId, ticket.MessageId });
        builder.HasIndex(ticket => new { ticket.GuildId, ticket.PreviousChannelId, ticket.PreviousMessageId });
    }

    public void Configure(EntityTypeBuilder<TicketConfiguration> builder)
    {
        builder.ToTable("ticket_configurations");

        builder.HasKey(configuration => configuration.GuildId);

        builder.Property(configuration => configuration.GuildId)
            .ValueGeneratedNever();
    }
}