using System.Globalization;
using System.Reflection;
using Discord;
using Discord.Rest;
using ErrorOr;
using NSubstitute;
using RatBot.Domain.Common;
using RatBot.Features.TicketCollection;
using Shouldly;
using MemberInfo = Discord.Rest.MemberInfo;

namespace RatBot.Discord.Tests.Features.TicketCollection;

[TestFixture]
public sealed class DiscordTicketAuditMapperTests
{
    private static readonly DateTimeOffset OccurredAt = new DateTimeOffset(2026, 10, 15, 19, 20, 21, TimeSpan.Zero);
    private static readonly DateTimeOffset CapturedAt = OccurredAt.AddHours(1);
    private static readonly DateTimeOffset TimeoutUntil = OccurredAt.AddDays(1);

    [Test]
    public void Map_CombinedMemberChangesWithoutUsers_PreservesAllChangesAndEvidence()
    {
        // Arrange
        MemberInfo before = CreateMemberInfo(mute: false, deafen: true);
        MemberInfo after = CreateMemberInfo(mute: true, deafen: false, timeout: TimeoutUntil);
        MemberUpdateAuditLogData data = CreateDiscordData<MemberUpdateAuditLogData>(null, before, after);
        IAuditLogEntry entry = CreateEntry(ActionType.MemberUpdated, data);

        // Act
        ErrorOr<Option<TicketAuditObservation>> result = DiscordTicketAuditMapper.Map(
            guildId: 17,
            entry,
            capturedAtUtc: CapturedAt);

        // Assert
        result.IsError.ShouldBeFalse();
        Some<TicketAuditObservation> captured = result.Value.Value.ShouldBeOfType<Some<TicketAuditObservation>>();
        TicketAuditObservation observation = captured.Value;
        observation.GuildId.ShouldBe(17UL);
        observation.AuditId.ShouldBe(42UL);
        observation.OccurredAtUtc.ShouldBe(OccurredAt);
        observation.CapturedAtUtc.ShouldBe(CapturedAt);
        observation.ActorId.ShouldBeNull();
        observation.TargetId.ShouldBeNull();
        observation.Reason.ShouldBe("  #123: unchanged reason\r\nwith spacing  ");

        MemberUpdated changed = observation.Action.Value.ShouldBeOfType<MemberUpdated>();
        changed.VoiceMute.ShouldBe(new TicketBooleanChange(Before: false, After: true));
        changed.VoiceDeafen.ShouldBe(new TicketBooleanChange(Before: true, After: false));
        changed.Timeout.ShouldBe(new TicketTimeoutChange(BeforeUtc: null, AfterUtc: TimeoutUntil));
    }

    [Test]
    public void Map_TimeoutCleared_PreservesPresentChangeWithNullAfterAndAbsentVoiceChanges()
    {
        // Arrange
        MemberInfo before = CreateMemberInfo(timeout: TimeoutUntil);
        MemberInfo after = CreateMemberInfo();
        MemberUpdateAuditLogData data = CreateDiscordData<MemberUpdateAuditLogData>(null, before, after);
        IAuditLogEntry entry = CreateEntry(ActionType.MemberUpdated, data);

        // Act
        ErrorOr<Option<TicketAuditObservation>> result = DiscordTicketAuditMapper.Map(
            guildId: 17,
            entry,
            capturedAtUtc: CapturedAt);

        // Assert
        result.IsError.ShouldBeFalse();
        Some<TicketAuditObservation> captured = result.Value.Value.ShouldBeOfType<Some<TicketAuditObservation>>();
        MemberUpdated changed = captured.Value.Action.Value.ShouldBeOfType<MemberUpdated>();
        changed.VoiceMute.ShouldBeNull();
        changed.VoiceDeafen.ShouldBeNull();
        changed.Timeout.ShouldNotBeNull();
        changed.Timeout.BeforeUtc.ShouldBe(TimeoutUntil);
        changed.Timeout.AfterUtc.ShouldBeNull();
    }

    [Test]
    public void Map_AddedAndRemovedRoles_PreservesEveryRoleBeforeFiltering()
    {
        // Arrange
        MemberRoleEditInfo[] roles =
        [
            CreateRoleChange(roleId: 101, added: true),
            CreateRoleChange(roleId: 202, added: true),
            CreateRoleChange(roleId: 303, added: false),
            CreateRoleChange(roleId: 101, added: false),
        ];

        IUser target = Substitute.For<IUser>();
        target.Id.Returns(123UL);
        MemberRoleAuditLogData data = CreateDiscordData<MemberRoleAuditLogData>(roles, target, null);
        IAuditLogEntry entry = CreateEntry(ActionType.MemberRoleUpdated, data);

        // Act
        ErrorOr<Option<TicketAuditObservation>> result = DiscordTicketAuditMapper.Map(
            guildId: 17,
            entry,
            capturedAtUtc: CapturedAt);

        // Assert
        result.IsError.ShouldBeFalse();
        Some<TicketAuditObservation> captured = result.Value.Value.ShouldBeOfType<Some<TicketAuditObservation>>();
        captured.Value.TargetId.ShouldBe(123UL);
        MemberRolesChanged changed = captured.Value.Action.Value.ShouldBeOfType<MemberRolesChanged>();
        changed.AddedRoleIds.ShouldBe(new ulong[] { 101, 202 });
        changed.RemovedRoleIds.ShouldBe(new ulong[] { 303, 101 });
    }

    [Test]
    public void Map_IncompleteDeafenAlongsideValidMuteAndTimeout_ReportsFailureWithoutPartialObservation()
    {
        // Arrange
        MemberInfo before = CreateMemberInfo(mute: false, deafen: false, timeout: TimeoutUntil);
        MemberInfo after = CreateMemberInfo(mute: true);
        MemberUpdateAuditLogData data = CreateDiscordData<MemberUpdateAuditLogData>(null, before, after);
        IAuditLogEntry entry = CreateEntry(ActionType.MemberUpdated, data);

        // Act
        ErrorOr<Option<TicketAuditObservation>> result = DiscordTicketAuditMapper.Map(
            guildId: 17,
            entry,
            capturedAtUtc: CapturedAt);

        // Assert
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("TicketAudit.IncompleteVoiceChange");
    }

    private static IAuditLogEntry CreateEntry(ActionType action, IAuditLogData data)
    {
        IAuditLogEntry entry = Substitute.For<IAuditLogEntry>();
        entry.Id.Returns(42UL);
        entry.CreatedAt.Returns(OccurredAt);
        entry.Action.Returns(action);
        entry.Data.Returns(data);
        entry.User.Returns((IUser)null!);
        entry.Reason.Returns("  #123: unchanged reason\r\nwith spacing  ");
        return entry;
    }

    private static MemberInfo CreateMemberInfo(bool? mute = null, bool? deafen = null, DateTimeOffset? timeout = null)
    {
        // Discord.NET's audit data constructors and member-info input model are internal.
        // Construct real library data for the mapper without a Discord connection.
        Type modelType = typeof(MemberInfo).Assembly.GetType(
            "Discord.API.AuditLogs.MemberInfoAuditLogModel",
            throwOnError: true)!;

        object model = Activator.CreateInstance(modelType)!;

        modelType.GetProperty("IsMuted")!
            .SetValue(model, mute);

        modelType.GetProperty("IsDeafened")!
            .SetValue(model, deafen);

        modelType.GetProperty("TimeOutUntil")!
            .SetValue(model, timeout);

        return CreateDiscordData<MemberInfo>(model);
    }

    private static T CreateDiscordData<T>(params object?[] arguments)
    {
        return (T)Activator.CreateInstance(
            typeof(T),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: arguments,
            culture: CultureInfo.InvariantCulture)!;
    }

    private static MemberRoleEditInfo CreateRoleChange(ulong roleId, bool added)
    {
        return CreateDiscordData<MemberRoleEditInfo>(string.Empty, roleId, added, !added);
    }
}