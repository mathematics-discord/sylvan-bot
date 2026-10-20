namespace RatBot.Application.Common;

public sealed class BotCommandException : Exception
{
    public BotCommandException(string message)
        : base(message)
    {
    }

    public BotCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}