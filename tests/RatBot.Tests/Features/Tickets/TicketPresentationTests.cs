using Discord;
using RatBot.Features.Tickets;

namespace RatBot.Tests.Features.Tickets;

public sealed class TicketPresentationTests
{
    [Test]
    public void ChannelMessage_Note_PutsContentAndButtonsInsideOneContainer()
    {
        // Arrange
        var ticket = new Ticket { Number = 42, Action = TicketAction.Note, Comment = "A note", SubjectUserId = 123 };

        // Act
        var components = TicketPresentation.ChannelMessage(ticket);

        // Assert
        var container = components.Components.ShouldHaveSingleItem().ShouldBeOfType<ContainerComponent>();
        var text = container.Components
            .OfType<TextDisplayComponent>()
            .Select(display => display.Content);

        string.Join('\n', text).ShouldContain("A note");
        var row = container.Components
            .OfType<ActionRowComponent>()
            .ShouldHaveSingleItem();

        row.Components
            .OfType<ButtonComponent>()
            .Select(button => button.CustomId)
            .ShouldBe(["ticket:edit:42", "ticket:hide:42"]);
    }

    [Test]
    public void ChannelMessage_OversizedComment_PutsAttachmentInsideContainer()
    {
        // Arrange
        var ticket = new Ticket { Number = 42, Action = TicketAction.Note, Comment = new string('x', 3501) };

        // Act
        var components = TicketPresentation.ChannelMessage(ticket);

        // Assert
        var container = components.Components.ShouldHaveSingleItem().ShouldBeOfType<ContainerComponent>();
        container.Components
            .OfType<FileComponent>()
            .ShouldHaveSingleItem().File.Url.ShouldBe("attachment://42-comment.txt");

        container.Components
            .OfType<TextDisplayComponent>()
            .ShouldAllBe(display => !display.Content.Contains(ticket.Comment));
    }

    [Test]
    public void ChannelMessage_MaximumInlineCommentAndMetadata_FitsTextLimit()
    {
        // Arrange
        var ticket = new Ticket
        {
            Number = long.MaxValue, Action = TicketAction.Role, Hidden = true,
            Comment = new string('x', TicketPresentation.InlineCommentLimit),
            SubjectUserId = ulong.MaxValue, ModeratorUserId = ulong.MaxValue, RoleId = ulong.MaxValue,
            CreatedAtUtc = DateTimeOffset.MaxValue, LastModifiedAtUtc = DateTimeOffset.MaxValue,
            LastModifiedByUserId = ulong.MaxValue, DurationSeconds = long.MaxValue,
            Status = TicketStatus.ExpirationFailed,
        };

        // Act
        var components = TicketPresentation.ChannelMessage(ticket);

        // Assert
        var container = components.Components.ShouldHaveSingleItem().ShouldBeOfType<ContainerComponent>();
        container.Components
            .OfType<TextDisplayComponent>()
            .Sum(display => display.Content.Length)
            .ShouldBeLessThanOrEqualTo(4000);

        container.Components
            .OfType<ActionRowComponent>()
            .ShouldBeEmpty();
    }
}