namespace SylvanBot.Tests.Features.Tickets;

using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

public sealed class TicketEmbedFormatTests
{
    [TestCase(TicketAction.Ban, false, true)]
    [TestCase(TicketAction.Note, false, false)]
    [TestCase(TicketAction.Kick, false, false)]
    [TestCase(TicketAction.Timeout, false, false)]
    [TestCase(TicketAction.Role, false, false)]
    [TestCase(TicketAction.VoiceMute, false, false)]
    [TestCase(TicketAction.VoiceDeafen, false, false)]
    [TestCase(TicketAction.Ban, true, true)]
    [TestCase(TicketAction.Timeout, true, true)]
    [TestCase(TicketAction.Note, true, false)]
    public void IdFieldsAndThumbnailFollowTheTicketsSavedFormat(TicketAction action, bool original, bool thumbnail)
    {
        var ticket = new Ticket
        {
            Action = action, SubjectUserId = 123, ModeratorUserId = 456, UseOriginalEmbedFormat = original,
        };

        var embed = TicketPresentation.Describe(ticket);

        embed.Fields.Single(field => field.Name == "User ID").Value.ShouldBe(original ? "`123`" : "123");
        embed.Fields.Single(field => field.Name == "Moderator ID").Value.ShouldBe(original ? "`456`" : "456");
        (embed.Thumbnail is not null).ShouldBe(thumbnail);
    }
}
