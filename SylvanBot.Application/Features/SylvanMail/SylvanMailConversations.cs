using Microsoft.EntityFrameworkCore;
using SylvanBot.Application.Common;
using SylvanBot.Infrastructure.Data;

namespace SylvanBot.Application.Features.SylvanMail;

public sealed class SylvanMailConversations(ISylvanMailForum forum)
{
    public async Task<SylvanMailConversation> ResolveAsync(
        BotDbContext db,
        SylvanMailSettings settings,
        SylvanMailMessage message,
        SylvanMailOperation operation,
        CancellationToken ct)
    {
        var conversation = await db.SylvanMailConversations
            .SingleOrDefaultAsync(
                item => item.GuildId == settings.GuildId && item.ForumId == settings.ForumId && item.UserId == message.AuthorId && !item.Retired,
                cancellationToken: ct);

        if (conversation?.ThreadId is { } existingThread)
        {
            var accessible = await forum.OpenPostAsync(conversation.GuildId, conversation.ForumId, existingThread, ct);

            if (!accessible)
            {
                conversation.Retired = true;
                await db.SaveChangesAsync(ct);
                conversation = null;
            }
        }

        if (conversation is null)
        {
            conversation = new SylvanMailConversation
            {
                GuildId = settings.GuildId,
                ForumId = settings.ForumId,
                UserId = message.AuthorId,
            };

            db.SylvanMailConversations.Add(conversation);
        }

        operation.ConversationId = conversation.Id;
        await db.SaveChangesAsync(ct);

        if (conversation.ThreadId is not null)
            return conversation;

        await CreateOrRecoverPostAsync(db, conversation, settings.RoleId, message.Username, ct);

        return conversation;
    }

    private async Task CreateOrRecoverPostAsync(
        BotDbContext db,
        SylvanMailConversation conversation,
        ulong roleId,
        string username,
        CancellationToken ct)
    {
        if (conversation.CreationStarted)
        {
            Option<ulong> found = await forum.FindPostAsync(
                guildId: conversation.GuildId,
                forumId: conversation.ForumId,
                number: conversation.Number,
                ct: ct);

            if (found.Value is not Some<ulong>(var recoveredThread))
            {
                throw new BotCommandException(
                    "An earlier post creation has an uncertain outcome. No replacement was created; staff must reconcile the saved attempt.");
            }

            conversation.ThreadId = recoveredThread;
        }
        else
        {
            conversation.CreationStarted = true;
            await db.SaveChangesAsync(ct);

            try
            {
                conversation.ThreadId = await forum.CreatePostAsync(
                    post: new SylvanMailPost
                    {
                        GuildId = conversation.GuildId,
                        ForumId = conversation.ForumId,
                        Number = conversation.Number,
                        UserId = conversation.UserId,
                    },
                    roleId: roleId,
                    username: username,
                    ct: ct);
            }
            catch (BotCommandException)
            {
                // A definite refusal made no post. Only ambiguous requests require marker reconciliation.
                conversation.CreationStarted = false;
                await db.SaveChangesAsync(CancellationToken.None);

                throw;
            }
        }

        await db.SaveChangesAsync(ct);

        var accessible = await forum.OpenPostAsync(conversation.GuildId, conversation.ForumId, conversation.ThreadId.GetValueOrDefault(), ct);

        if (!accessible)
            throw new BotCommandException("The post disappeared before relay. This message was not sent.");
    }
}