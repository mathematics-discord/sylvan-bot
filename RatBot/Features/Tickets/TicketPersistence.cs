using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace RatBot.Features.Tickets;

public sealed class TicketPersistence :
    IEntityTypeConfiguration<TicketNote>,
    IEntityTypeConfiguration<TicketConfiguration>
{
    public void Configure(EntityTypeBuilder<TicketNote> builder)
    {
        builder.ToTable("ticket_notes", table =>
            table.HasCheckConstraint("CK_ticket_notes_positive_number", "\"TicketNumber\" > 0"));

        builder.HasKey(note => note.TicketNumber);
        builder.Property(note => note.TicketNumber)
            .UseIdentityAlwaysColumn()
            .HasIdentityOptions(startValue: 1, minValue: 1);

        builder.Property(note => note.Text)
            .HasMaxLength(2000)
            .IsRequired();

        builder.HasIndex(note => new { note.GuildId, note.LegacyTicketNumber })
            .IsUnique();
    }

    public void Configure(EntityTypeBuilder<TicketConfiguration> builder)
    {
        builder.ToTable("ticket_configurations");
        builder.HasKey(configuration => configuration.GuildId);
        builder.Property(configuration => configuration.GuildId)
            .ValueGeneratedNever();
    }
}
