using Serilog.Context;

namespace SylvanBot.Interactions;

public sealed class InteractionLogScope : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _properties;

    private InteractionLogScope(IReadOnlyList<IDisposable> properties)
    {
        _properties = properties;
    }

    public static InteractionLogScope Begin(InteractionLogScopeDetails details)
    {
        List<IDisposable> properties =
        [
            LogContext.PushProperty("LogArea", "interaction"),
            LogContext.PushProperty("ServiceInstanceId", value: details.ServiceInstanceId),
            LogContext.PushProperty("ProcessId", value: details.ProcessId),
            LogContext.PushProperty("InteractionId", value: details.InteractionId),
            LogContext.PushProperty("InteractionType", value: details.InteractionType),
            LogContext.PushProperty("InteractionName", value: details.InteractionName),
            LogContext.PushProperty("InteractionCreatedAtUtc", value: details.InteractionCreatedAtUtc),
            LogContext.PushProperty("UserId", value: details.UserId),
            LogContext.PushProperty("GuildId", value: details.GuildId),
            LogContext.PushProperty("ChannelId", value: details.ChannelId),
            LogContext.PushProperty("CommandName", value: details.CommandName),
        ];

        return new InteractionLogScope(properties);
    }

    public void Dispose()
    {
        for (var i = _properties.Count - 1; i >= 0; i--)
            _properties[i].Dispose();
    }
}
