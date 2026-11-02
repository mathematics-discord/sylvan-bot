using System.Collections.Immutable;
using System.Text.RegularExpressions;
using SylvanBot.Application.Features.Automod;

namespace SylvanBot.Tests.Features.Automod;

public sealed class AutomodInputParserTests
{
    [Test]
    public void Parse_MixedItems_PreservesOrderAndRegexCommas()
    {
        // Arrange
        const string input = " hello , `a{1,3}`, world, ```x,y```";

        // Act
        ImmutableArray<AutomodPattern> patterns = AutomodInputParser.Parse(input);

        // Assert
        patterns.ShouldBe(new[]
        {
            new AutomodPattern("hello", AutomodMatchType.Word),
            new AutomodPattern("a{1,3}", AutomodMatchType.Regex),
            new AutomodPattern("world", AutomodMatchType.Word),
            new AutomodPattern("x,y", AutomodMatchType.Regex),
        });
    }

    [TestCase("`\\d+\\s+word`", @"\d+\s+word")]
    [TestCase("```regex\n\\d+\n```", @"\d+")]
    [TestCase("```\r\n\\d+\r\n```", @"\d+")]
    [TestCase("```regex```", "regex")]
    [TestCase("```text\nfirst\nsecond\n```", "first\nsecond")]
    [TestCase("` hello `", " hello ")]
    public void Parse_Code_PreservesPatternContent(string input, string expected)
    {
        // Act
        ImmutableArray<AutomodPattern> patterns = AutomodInputParser.Parse(input);

        // Assert
        patterns.Length.ShouldBe(1);
        patterns[0].ShouldBe(new AutomodPattern(expected, AutomodMatchType.Regex));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase(",hello")]
    [TestCase("hello,")]
    [TestCase("hello, ,world")]
    [TestCase("hello, ``")]
    [TestCase("hello, `unterminated")]
    [TestCase("hello, ```unterminated")]
    [TestCase("hello, `a` trailing")]
    [TestCase("hello, a`b`")]
    [TestCase("hello, `a*`")]
    [TestCase("hello, `^$`")]
    public void Parse_InvalidItem_RejectsEntireBatch(string input)
    {
        // Act / Assert
        Should.Throw<FormatException>(() => AutomodInputParser.Parse(input));
    }

    [Test]
    public void Parse_InvalidRegex_RejectsEntireBatch()
    {
        // Act / Assert
        Should.Throw<RegexParseException>(() => AutomodInputParser.Parse("hello, `[`"));
    }
}