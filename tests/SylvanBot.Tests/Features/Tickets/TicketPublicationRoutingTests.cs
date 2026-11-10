using SylvanBot.Application.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketPublicationRoutingTests
{
    [Test]
    public void PublicationChannel_ExistingPost_PreservesItsChannel()
    {
        // Arrange
        var ticket = new Ticket();
        ticket.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 100, Removed = true });
        var configuration = new TicketConfiguration { DestinationChannelId = 20 };

        // Act
        var channel = configuration.PublicationChannel(ticket);

        // Assert
        channel.ShouldBe(10UL);
        configuration.PublicationChannel(new Ticket()).ShouldBe(20UL);
    }
}