// <copyright file="QuorumVoterRole.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace SylvanBot.Application.Features.Quorum;

/// <summary>
///     Represents a voter role associated with a quorum configuration. Each voter role
///     corresponds to a specific role within the context of a guild's quorum-based operations.
/// </summary>
/// <remarks>
///     Instances of this class are linked to <c>QuorumConfiguration</c> entities and define
///     roles that participate in quorum decision-making logic. QuorumVoterRole entities
///     are typically persisted and managed through database operations.
/// </remarks>
public sealed class QuorumVoterRole
{
    /// <summary>
    ///     Gets or initializes the unique identifier of the quorum configuration associated with a specific voter role.
    ///     This property establishes the relationship between a voter role and its corresponding quorum configuration.
    /// </summary>
    public Guid QuorumConfigurationId { get; init; }

    /// <summary>
    ///     Gets or initializes the unique identifier of the voter role within the quorum configuration.
    ///     This property represents the role that is designated to participate in quorum-related decisions.
    /// </summary>
    public ulong RoleId { get; init; }
}