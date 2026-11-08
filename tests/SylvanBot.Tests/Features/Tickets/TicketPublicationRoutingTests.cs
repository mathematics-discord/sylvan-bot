namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketPublicationRoutingTests
{
    [Test]
    public void ImportedTicketKeepsItsPinnedChannelAfterExpiryAndReconfiguration()
    {
        var ticket = new Ticket { ImportedFromLegacy = true, PinnedPublicationChannelId = 10, Action = TicketAction.Ban };
        ticket.End(TicketStatus.Expired);
        var config = new TicketConfiguration { DestinationChannelId = 30, LegacyChannelId = 20 };

        config.PublicationChannel(ticket).ShouldBe(10UL);
    }

    [Test]
    public void PauseHoldsAReclassifiedImportAndLetsNativeTicketsPublish()
    {
        var config = new TicketConfiguration
        {
            DestinationChannelId = 10,
            LegacyChannelId = 20,
            OrderedPublicationPending = true,
            OrderedPublicationPaused = true,
        };
        var moved = new Ticket { ImportedFromLegacy = true, PinnedPublicationChannelId = 20 };
        moved.Publications.Add(new TicketPublication { ChannelId = 10, MessageId = 42, Removed = true });

        config.HoldsImportedPublication(moved).ShouldBeTrue();
        config.HoldsImportedPublication(new Ticket()).ShouldBeFalse();
    }

    [TestCase(false, true, false, true, 10)]
    [TestCase(false, true, true, true, 10)]
    [TestCase(true, true, false, false, 10)]
    [TestCase(true, true, true, true, 20)]
    [TestCase(true, false, true, false, 20)]
    [TestCase(true, false, false, false, 10)]
    public void RouteDependsOnImportProvenanceAndBackfillState(
        bool split, bool pending, bool imported, bool shouldWait, int channel)
    {
        var configuration = new TicketConfiguration
        {
            DestinationChannelId = 10,
            LegacyChannelId = split ? 20UL : null,
            OrderedPublicationPending = pending,
        };
        var ticket = new Ticket { ImportedFromLegacy = imported };

        configuration.AwaitsOrderedPublication(ticket).ShouldBe(shouldWait);
        configuration.PublicationChannel(ticket).ShouldBe((ulong)channel);
    }
}
