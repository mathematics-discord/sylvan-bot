namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

public sealed class TicketNotePresentationTests
{
    [Test]
    public void NoteUsesTheTicketEmbedAndPreservesItsLegacyDetails()
    {
        var ticket = new Ticket
        {
            Action = TicketAction.Note, Number = 1415, SubjectUserId = 123, ModeratorUserId = 456,
            CreatedAtUtc = DateTimeOffset.FromUnixTimeSeconds(1500000000),
        };

        var embed = TicketPresentation.Describe(ticket);

        embed.Author!.Value.Name.ShouldBe("Ticket #1415");
        embed.Title.ShouldBe("Note");
        embed.Description.ShouldBe("**Note** for <@123> by <@456>");
        embed.Timestamp.ShouldBe(ticket.CreatedAtUtc);
        embed.Color!.Value.RawValue.ShouldBe(0xFFFFFFu);
        embed.Fields.Select(field => field.Name).ShouldBe(new[] { "User ID", "Moderator ID", "Duration" });
        embed.Fields[^1].Value.ShouldBe("**Permanent**");
    }
}
