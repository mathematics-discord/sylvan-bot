namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;

public sealed class TicketPublicationRoutingTests
{
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
