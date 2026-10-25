// <copyright file="QuorumMember.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SylvanBot.Application.Features.Quorum;

/// <summary>
/// Represents a member of a quorum, capturing the user's unique identifier, whether the user is a bot, and the roles assigned to the user.
/// </summary>
/// <param name="UserId">The unique identifier of the user.</param>
/// <param name="IsBot">A value indicating whether the user is a bot.</param>
/// <param name="RoleIds">An array of unique identifiers representing the roles assigned to the user.</param>
public readonly record struct QuorumMember(ulong UserId, bool IsBot, ulong[] RoleIds);