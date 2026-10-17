using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RatBot.Infrastructure.Data;

namespace RatBot.Features.Tickets;

[Group("ticket", "Moderator ticket notes.")]
[DefaultMemberPermissions(GuildPermission.UseApplicationCommands)]
[RequireContext(ContextType.Guild)]
public sealed class TicketModule(IDbContextFactory<BotDbContext> contextFactory, ILogger logger)
    : InteractionModuleBase<IInteractionContext>
{
    [SlashCommand("config", "View or update ticket configuration.")]
    [RequireUserPermission(GuildPermission.Administrator)]
    public async Task ConfigAsync(
        [Summary("enabled", "Whether ticket notes are enabled.")]
        bool? enabled = null,
        [Summary("channel", "Text channel for publishing ticket notes.")] [ChannelTypes(ChannelType.Text)]
        ITextChannel? channel = null)
    {
        await DeferAsync(ephemeral: true);
        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var configuration = await dbContext.TicketConfigurations
            .SingleOrDefaultAsync(configuration => configuration.GuildId == Context.Guild.Id);

        var isNew = configuration is null;
        configuration ??= new TicketConfiguration { GuildId = Context.Guild.Id };
        var hasUpdates = enabled is not null || channel is not null;

        if (hasUpdates)
        {
            var updatedEnabled = enabled ?? configuration.Enabled;
            var destinationChannelId = channel?.Id ?? configuration.DestinationChannelId;
            var destination = channel;

            if (updatedEnabled && destinationChannelId is not null && destination is null)
                destination = await Context.Guild.GetTextChannelAsync(destinationChannelId.Value);

            var needsDestinationValidation = channel is not null || updatedEnabled;
            var validDestination = destination is not null
                                   && destination.GuildId == Context.Guild.Id
                                   && destination.ChannelType == ChannelType.Text;

            if (needsDestinationValidation && !validDestination)
            {
                await ModifyOriginalResponseAsync(response => response.Content =
                    "Choose an available text channel in this server before enabling ticket notes.");

                return;
            }

            configuration.Enabled = updatedEnabled;
            configuration.DestinationChannelId = destinationChannelId;

            if (isNew)
                dbContext.TicketConfigurations.Add(configuration);

            await dbContext.SaveChangesAsync();
        }

        var destinationDescription = configuration.DestinationChannelId is null
            ? "unset"
            : $"<#{configuration.DestinationChannelId}>";

        await ModifyOriginalResponseAsync(response =>
        {
            response.Content = $"Ticket configuration:\n- Enabled: {configuration.Enabled}\n"
                               + $"- Destination: {destinationDescription}";

            response.AllowedMentions = AllowedMentions.None;
        });
    }

    [SlashCommand("note", "Save a moderator note and publish it to the tickets channel.")]
    public async Task NoteAsync(
        [Summary("user", "Subject's user mention or Discord user ID; departed users are supported.")]
        string user,
        [Summary("text", "Moderator note, up to 2,000 characters.")]
        string text)
    {
        await DeferAsync(ephemeral: true);

        var subjectUserId = await ValidateNoteInputAsync(user, text);

        if (subjectUserId is null)
            return;

        var noteLogger = logger
            .ForContext<TicketModule>()
            .ForContext("guild_id", Context.Guild.Id)
            .ForContext("subject_user_id", subjectUserId)
            .ForContext("author_user_id", Context.User.Id);

        await using var dbContext = await contextFactory.CreateDbContextAsync();
        var configuration = await dbContext.TicketConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.GuildId == Context.Guild.Id);

        if (configuration is not { Enabled: true, DestinationChannelId: not null })
        {
            await ModifyOriginalResponseAsync(response =>
                response.Content = "Ticket notes are disabled or have no destination configured.");
            return;
        }

        var destination = await GetDestinationAsync(
            configuration.DestinationChannelId.Value,
            (IGuildUser)Context.User,
            noteLogger);

        if (destination is null)
            return;

        var note = new TicketNote
        {
            GuildId = Context.Guild.Id,
            SubjectUserId = subjectUserId.Value,
            AuthorUserId = Context.User.Id,
            Text = text,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        dbContext.TicketNotes.Add(note);

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            // Exception messages/details can contain database row values, including the note.
            noteLogger.Error("Saving ticket note failed: {FailureType}", exception.GetType().Name);
            await ModifyOriginalResponseAsync(response =>
                response.Content = "Saving the note failed. No channel post was attempted.");
            return;
        }

        await PublishNoteAsync(dbContext, note, destination, noteLogger);
    }

    private async Task<ulong?> ValidateNoteInputAsync(string user, string text)
    {
        if (Context.User is not IGuildUser author)
        {
            await ModifyOriginalResponseAsync(response => response.Content = "Use this command in a server.");
            return null;
        }

        var permissions = author.GuildPermissions;
        var canCreateNote = permissions.Administrator
                            || permissions.ModerateMembers
                            || permissions.MuteMembers;

        if (!canCreateNote)
        {
            await ModifyOriginalResponseAsync(response => response.Content =
                "You need Administrator, Moderate Members or Mute Members permission to create a ticket note.");

            return null;
        }

        var validSubject = MentionUtils.TryParseUser(user, out var subjectUserId)
                           || ulong.TryParse(user, NumberStyles.None, CultureInfo.InvariantCulture, out subjectUserId);

        if (!validSubject || subjectUserId == 0)
        {
            await ModifyOriginalResponseAsync(response =>
                response.Content = "Provide a user mention or a Discord user ID.");
            return null;
        }

        var validText = !string.IsNullOrWhiteSpace(text) && text.Length <= 2000;

        if (!validText)
        {
            await ModifyOriginalResponseAsync(response => response.Content =
                "The note must contain non-whitespace text and be at most 2,000 characters.");

            return null;
        }

        return subjectUserId;
    }

    private async Task<ITextChannel?> GetDestinationAsync(
        ulong destinationChannelId,
        IGuildUser author,
        ILogger noteLogger)
    {
        try
        {
            var destination = await Context.Guild.GetTextChannelAsync(destinationChannelId);
            var validDestination = destination is not null
                                   && destination.GuildId == Context.Guild.Id
                                   && destination.ChannelType == ChannelType.Text;

            if (destination is null || !validDestination)
            {
                await ModifyOriginalResponseAsync(response =>
                    response.Content = "The tickets channel is unavailable. No note was created.");
                return null;
            }

            if (!author.GetPermissions(destination).ViewChannel)
            {
                await ModifyOriginalResponseAsync(response =>
                    response.Content = "You must be able to view the tickets channel to create a note.");
                return null;
            }

            var botUser = await Context.Guild.GetCurrentUserAsync();
            var botPermissions = botUser.GetPermissions(destination);
            var canPublish = botPermissions is { ViewChannel: true, SendMessages: true, EmbedLinks: true };

            if (canPublish)
                return destination;

            await ModifyOriginalResponseAsync(response =>
                response.Content =
                    "Sylvan needs View Channel, Send Messages and Embed Links in the tickets channel. No note was created.");

            return null;
        }
        catch (Exception exception)
        {
            noteLogger.Error(
                "Ticket destination check failed for channel {ChannelId}: {FailureType}",
                destinationChannelId,
                exception.GetType().Name);

            await ModifyOriginalResponseAsync(response =>
                response.Content = "The tickets channel could not be checked. No note was created.");
            return null;
        }
    }

    private async Task PublishNoteAsync(
        BotDbContext dbContext,
        TicketNote note,
        ITextChannel destination,
        ILogger noteLogger)
    {
        var ticketId = $"T-{note.TicketNumber.ToString("D4", CultureInfo.InvariantCulture)}";
        noteLogger = noteLogger.ForContext("ticket_number", note.TicketNumber);
        var embed = new EmbedBuilder()
            .WithTitle($"{ticketId} · Moderator note")
            .WithDescription(note.Text)
            .AddField("Subject", $"<@{note.SubjectUserId}> (`{note.SubjectUserId}`)", inline: true)
            .AddField("Author", $"<@{note.AuthorUserId}> (`{note.AuthorUserId}`)", inline: true)
            .WithTimestamp(note.CreatedAtUtc)
            .Build();

        IUserMessage publishedMsg;

        try
        {
            publishedMsg = await destination.SendMessageAsync(
                embed: embed,
                allowedMentions: AllowedMentions.None,
                options: new RequestOptions { RetryMode = RetryMode.AlwaysFail });
        }
        catch (Exception exception)
        {
            noteLogger.Error(
                "Publishing ticket note to channel {ChannelId} failed: {FailureType}",
                destination.Id,
                exception.GetType().Name);

            await ModifyOriginalResponseAsync(response => response.Content =
                $"{ticketId} was saved, but publication failed or could not be confirmed. "
                + "Check the tickets channel before creating another note.");

            return;
        }

        var jumpLink = $"https://discord.com/channels/{note.GuildId}/{destination.Id}/{publishedMsg.Id}";
        note.PublishedChannelId = destination.Id;
        note.PublishedMessageId = publishedMsg.Id;

        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (Exception exception)
        {
            noteLogger.Error(
                "Saving ticket publication reference for channel {ChannelId}, message {MessageId} failed: {FailureType}",
                destination.Id,
                publishedMsg.Id,
                exception.GetType().Name);

            await ModifyOriginalResponseAsync(response => response.Content =
                $"{ticketId} was saved and published: {jumpLink}\nSaving its channel/message reference failed. Do not create it again.");

            return;
        }

        await ModifyOriginalResponseAsync(response => response.Content = $"Saved and published {ticketId}: {jumpLink}");
    }
}