namespace SylvanBot.Interactions;

using Serilog.Context;

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
            LogContext.PushProperty("ServiceInstanceId", details.ServiceInstanceId),
            LogContext.PushProperty("ProcessId", details.ProcessId),
            LogContext.PushProperty("InteractionId", details.InteractionId),
            LogContext.PushProperty("InteractionType", details.InteractionType),
            LogContext.PushProperty("InteractionName", details.InteractionName),
            LogContext.PushProperty("InteractionCreatedAtUtc", details.InteractionCreatedAtUtc),
            LogContext.PushProperty("UserId", details.UserId),
            LogContext.PushProperty("GuildId", details.GuildId),
            LogContext.PushProperty("ChannelId", details.ChannelId),
            LogContext.PushProperty("CommandName", details.CommandName),
        ];

        return new InteractionLogScope(properties);
    }

    public void Dispose()
    {
        for (var i = _properties.Count - 1; i >= 0; i--)
            _properties[i].Dispose();
    }
}