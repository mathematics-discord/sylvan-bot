namespace RatBot.Features.Logging.Gateway;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BirbResponseHandler(ILogger logger) : InteractionModuleBase<IInteractionContext>
{
    private readonly ILogger _logger = logger.ForContext<BirbResponseHandler>();

    public const string AcceptId = "accept-joy";
    public const string DeclineId = "decline-joy";

    [ComponentInteraction(AcceptId)]
    public async Task Accept()
    {
        await RespondAsync($"May joye befall thee, by this Robynes passyng, <@{Context.User.Id}> ☺️", ephemeral: true);
        _logger.Information("responded to {UserId}", Context.User.Id);
    }

    [ComponentInteraction(DeclineId)]
    public async Task Decline()
    {
        await RespondAsync($"The Robyne passeth onward, troubling thee no further, <@{Context.User.Id}> ☺️",
            ephemeral: true);
        _logger.Information("declined by {UserId}", Context.User.Id);
    }
}