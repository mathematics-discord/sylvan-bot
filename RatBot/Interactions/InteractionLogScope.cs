using Serilog.Context;

namespace RatBot.Interactions;

public sealed class InteractionLogScope : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _properties;

    private InteractionLogScope(IReadOnlyList<IDisposable> properties)
    {
        _properties = properties;
    }

    public static InteractionLogScope Begin(InteractionLogScopeDetails details)
    {
        List<IDisposable> properties = new List<IDisposable>
        {
            LogContext.PushProperty("log_area", "interaction"),
            LogContext.PushProperty("service_instance_id", details.ServiceInstanceId),
            LogContext.PushProperty("process_id", details.ProcessId),
            LogContext.PushProperty("interaction_id", details.InteractionId),
            LogContext.PushProperty("interaction_type", details.InteractionType),
            LogContext.PushProperty("interaction_name", details.InteractionName),
            LogContext.PushProperty("interaction_created_at_utc", details.InteractionCreatedAtUtc),
            LogContext.PushProperty("user_id", details.UserId),
            LogContext.PushProperty("guild_id", details.GuildId),
            LogContext.PushProperty("channel_id", details.ChannelId),
            LogContext.PushProperty("command_name", details.CommandName)
        };

        return new InteractionLogScope(properties);
    }

    public void Dispose()
    {
        for (int i = _properties.Count - 1; i >= 0; i--)
            _properties[i].Dispose();
    }
}