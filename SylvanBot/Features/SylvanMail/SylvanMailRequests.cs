using System.Net;
using Discord.Net;

namespace SylvanBot.Features.SylvanMail;

internal static class SylvanMailRequests
{
    public static RequestOptions Options(CancellationToken ct) => new RequestOptions() { CancelToken = ct, RetryMode = RetryMode.AlwaysFail };

    public static async Task<T> AcceptedAsync<T>(Func<Task<T>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpException exception) when ((int)exception.HttpCode is >= 400 and < 500 && exception.HttpCode != HttpStatusCode.TooManyRequests)
        {
            throw new BotCommandException(
                $"Discord refused the operation (HTTP {(int)exception.HttpCode}); no automatic retry.",
                innerException: exception);
        }
    }

    public static async Task<IMessageChannel> MessageChannelAsync(DiscordSocketClient client, ulong channelId, CancellationToken ct)
    {
        var channel = await client.Rest.GetChannelAsync(id: channelId, Options(ct));

        return channel as IMessageChannel ?? throw new BotCommandException("The delivery channel is inaccessible.");
    }

    public static void LogFailure(ILogger logger, string stage, ulong sourceId, Exception exception) =>
        logger.Warning(
            "SylvanMail {Stage} failed for source {SourceId}: {ExceptionType}",
            propertyValue0: stage,
            propertyValue1: sourceId,
            propertyValue2: exception.GetType().Name);
}