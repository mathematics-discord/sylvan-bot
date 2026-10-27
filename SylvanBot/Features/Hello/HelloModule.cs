using SylvanBot.Interactions;

namespace SylvanBot.Features.Hello;

[DefaultMemberPermissions(GuildPermission.SendMessages)]
public sealed class HelloModule(ILogger logger) : SlashCommandBase
{
    private readonly ILogger _logger = logger.ForContext<HelloModule>();

    [SlashCommand("hello", "Says hello!")]
    public Task HelloAsync()
    {
        _logger.Information("Received hello command from {User}", propertyValue: Context.User.Username);

        return RespondAsync($"Hello, {Context.User.Mention}!");
    }
}