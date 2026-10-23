namespace RatBot.Features.Bridge;

public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    public string Address { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 8765;

    public string Credential { get; init; } = string.Empty;
}