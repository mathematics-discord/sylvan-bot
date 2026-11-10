using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SylvanBot.Application.Features.Tickets;
using SylvanBot.Features.Tickets;

namespace SylvanBot.Tests.Features.Tickets;

public sealed class TicketAuditReaderTests
{
    [TestCase(999UL, 22, "Automatic action: message matches pattern 240", true)]
    [TestCase(999UL, 24, "Automatic action: message matches pattern 240", true)]
    [TestCase(999UL, 22, "Automod ban rule 240; automod operation 123", true)]
    [TestCase(999UL, 22, "Permanent, Automatic image-burst spam detection: 4 channels", false)]
    [TestCase(456UL, 22, "Automatic action: message matches pattern 240", false)]
    [TestCase(999UL, 24, "Sylvan ticket 42; operation 70560841-61c9-40d8-abbb-2cbd50bdc819", false)]
    public void Read_ApprovalRequirement_DistinguishesSylvanAutomodFromOtherActions(ulong moderator, int action, string reason, bool expected)
    {
        // Arrange
        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(
                new
                {
                    id = "1430000000000000000",
                    action_type = action,
                    target_id = "123",
                    user_id = moderator.ToString(CultureInfo.InvariantCulture),
                    reason,
                    changes = new[] { new { key = "communication_disabled_until", new_value = "2026-11-11T00:00:00Z" } },
                }));

        // Act
        var observation = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong> { 456 }, ownBotId: 999)
            .Single();

        // Assert
        observation.RequiresApproval.ShouldBe(expected);
    }

    [Test]
    public void Read_UncachedUsers_PreservesRawIdentities()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            """
            {"id":"1430000000000000000","action_type":22,"target_id":"123","user_id":"456"}
            """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result.Length.ShouldBe(1);
        result[0].SubjectUserId.ShouldBe(123UL);
        result[0].ModeratorUserId.ShouldBe(456UL);
        result[0].Reason.ShouldBeEmpty();
    }

    [TestCase("target_id")]
    [TestCase("user_id")]
    public void Read_MissingIdentity_ThrowsWithFieldName(string field)
    {
        // Arrange
        Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["id"] = "1430000000000000000", ["action_type"] = 22, ["target_id"] = "123", ["user_id"] = "456",
        };

        values.Remove(field);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));

        // Act
        void Act()
        {
            TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);
        }

        // Assert
        Should.Throw<InvalidDataException>(Act).Message.ShouldContain(field);
    }

    [Test]
    public void Read_RemovedTimeout_UsesExplicitNoTimeoutState()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            """
            {"id":"1430000000000000000","action_type":24,"target_id":"123","user_id":"456",
             "changes":[{"key":"communication_disabled_until","old_value":"2026-11-01T00:00:00Z","new_value":null}]}
            """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result[0].Removed.ShouldBeTrue();
        (result[0].Timeout is TicketTimeout.None).ShouldBeTrue();
        result[0].CreatesTicket.ShouldBeFalse();
    }

    [Test]
    public void Read_SimultaneousVoiceChanges_PreservesBothActions()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            """
            {"id":"1430000000000000000","action_type":24,"target_id":"123","user_id":"456",
             "changes":[{"key":"mute","old_value":false,"new_value":true},
                        {"key":"deaf","old_value":true,"new_value":false},
                        {"key":"nick","new_value":"different"}]}
            """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result.Length.ShouldBe(2);
        result[0].Action.ShouldBe(TicketAction.VoiceMute);
        result[0].CreatesTicket.ShouldBeTrue();
        result[1].Action.ShouldBe(TicketAction.VoiceDeafen);
        result[1].Removed.ShouldBeTrue();
    }

    [Test]
    public void Read_MultipleRoleChanges_PreservesEachRoleAndDirection()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            """
            {"id":"1430000000000000000","action_type":25,"target_id":"123","user_id":"456",
             "changes":[{"key":"$add","new_value":[{"id":"10"},{"id":"20"}]},
                        {"key":"$remove","new_value":[{"id":"30"}]}]}
            """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result.Length.ShouldBe(3);
        result[0].RoleId.ShouldBe(10UL);
        result[1].RoleId.ShouldBe(20UL);
        result[2].RoleId.ShouldBe(30UL);
        result[2].CreatesTicket.ShouldBeFalse();
    }

    [TestCase(arg1: 999UL, arg2: 42L)]
    [TestCase(arg1: 456UL, arg2: null)]
    public void Read_RestorationReason_TrustsOnlyOwnBot(ulong moderator, long? expected)
    {
        // Arrange
        using var document = JsonDocument.Parse(
            $$"""
              {"id":"1430000000000000000","action_type":25,"target_id":"123","user_id":"{{
                  moderator
              }}",
               "reason":"Role persistence; ticket 42","changes":[{"key":"$add","new_value":[{"id":"10"}]}]}
              """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result[0].RestoredTicketNumber.ShouldBe(expected);
    }

    [Test]
    public void Read_SelfVoiceAndUnrelatedChanges_CreatesNothing()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            """
            {"id":"1430000000000000000","action_type":24,"target_id":"123","user_id":"456",
             "changes":[{"key":"self_mute","new_value":true},{"key":"nick","new_value":"name"}]}
            """);

        // Act
        ImmutableArray<TicketObservation> result = TicketAuditReader.Read(guildId: 1, document.RootElement, new HashSet<ulong>(), ownBotId: 999);

        // Assert
        result.ShouldBeEmpty();
    }
}