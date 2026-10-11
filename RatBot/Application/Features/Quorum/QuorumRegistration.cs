using RatBot.Domain.Features.Quorum;

namespace RatBot.Application.Features.Quorum;

public readonly record struct QuorumRegistration(bool Created, QuorumConfiguration Configuration);