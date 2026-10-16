namespace RatBot.Features.TicketCollection;

public sealed record MemberUpdated(
    TicketBooleanChange? VoiceMute = null,
    TicketBooleanChange? VoiceDeafen = null,
    TicketTimeoutChange? Timeout = null);