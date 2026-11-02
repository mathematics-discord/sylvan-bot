using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodConfigurationTests
{
    [Test]
    public void Construct_EmptyConfiguration_HasNoMatchOrExemptions()
    {
        // Act
        var configuration = new AutomodConfiguration([], []);

        // Assert
        configuration.Matcher.Match("hello").Rule.HasValue.ShouldBeFalse();
        configuration.ExemptRoleIds.ShouldBeEmpty();
    }

    [Test]
    public void Construct_ExemptionsOnly_OwnsRoleIds()
    {
        // Arrange
        List<ulong> roles = [1, 1, 2];
        var configuration = new AutomodConfiguration([], roles);

        // Act
        roles.Clear();

        // Assert
        configuration.ExemptRoleIds.SetEquals([1UL, 2UL]).ShouldBeTrue();
        configuration.Matcher.Match("hello").Rule.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Construct_Rules_OwnsIndependentRuleInstances()
    {
        // Arrange
        var rule = new AutomodRule { Id = 7, GuildId = 3, Pattern = "hello", Action = AutomodAction.Ban, DurationSeconds = 60 };
        List<AutomodRule> rules = [rule];
        var configuration = new AutomodConfiguration(rules, []);

        // Act
        rules.Clear();
        var match = configuration.Matcher.Match("hello").Rule.Value;

        // Assert
        ReferenceEquals(match, rule).ShouldBeFalse();
        match.Id.ShouldBe(7);
        match.GuildId.ShouldBe(3UL);
        match.Action.ShouldBe(AutomodAction.Ban);
        match.DurationSeconds.ShouldBe(60);
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("[")]
    [TestCase("a*")]
    public void Construct_InvalidStoredPattern_RejectsConfiguration(string pattern)
    {
        // Arrange
        var rule = new AutomodRule { Id = 7, GuildId = 3, Pattern = pattern, MatchType = AutomodMatchType.Regex };

        // Act
        var error = Should.Throw<FormatException>(() => new AutomodConfiguration([rule], []));

        // Assert
        error.Message.ShouldContain("rule 7");
        error.Message.ShouldContain("guild 3");
    }
    [TestCase(AutomodAction.Delete)]
    [TestCase(AutomodAction.Note)]
    [TestCase(AutomodAction.Mute)]
    [TestCase(AutomodAction.Ban)]
    public void Match_DisabledAction_DoesNotSelectItsRules(AutomodAction action)
    {
        // Arrange
        var rule = new AutomodRule { Id = 1, Pattern = "hello", Action = action };
        var configuration = new AutomodConfiguration([rule], [], [action]);

        // Act
        var result = configuration.Matcher.Match("hello");

        // Assert
        result.Rule.HasValue.ShouldBeFalse();
    }

    [Test]
    public void Match_DisabledEarlierRule_SelectsEnabledAction()
    {
        // Arrange
        var deletion = new AutomodRule { Id = 1, Pattern = "first", Action = AutomodAction.Delete };
        var mute = new AutomodRule { Id = 2, Pattern = "later", Action = AutomodAction.Mute, DurationSeconds = 60 };
        var configuration = new AutomodConfiguration([deletion, mute], [], [AutomodAction.Delete]);

        // Act
        var result = configuration.Matcher.Match("first then later");

        // Assert
        result.Rule.Value.Id.ShouldBe(2);
    }

    [Test]
    public void Match_ReenabledAction_UsesRetainedRulesAndExemptions()
    {
        // Arrange
        var rule = new AutomodRule { Id = 1, Pattern = "hello", Action = AutomodAction.Delete };
        List<AutomodRule> rules = [rule];
        var disabled = new AutomodConfiguration(rules, [42UL], [AutomodAction.Delete]);

        // Act
        var enabled = new AutomodConfiguration(rules, [42UL], []);

        // Assert
        disabled.Matcher.Match("hello").Rule.HasValue.ShouldBeFalse();
        enabled.Matcher.Match("hello").Rule.Value.Id.ShouldBe(1);
        enabled.ExemptRoleIds.ShouldContain(42UL);
        rules.Count.ShouldBe(1);
    }
}