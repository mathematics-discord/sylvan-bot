namespace SylvanBot.Gateway;

public interface IDiscordGatewayHandler
{
    Task InitializeAsync(CancellationToken ct);
    void Unsubscribe();
}