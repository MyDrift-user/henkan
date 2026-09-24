using Henkan.Core.Expressions;

namespace Henkan.Core.Tests;

public class ConditionExpressionTests
{
    private static Func<string, string?> Values(params (string Key, string Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        return name => map.TryGetValue(name, out string? value) ? value : null;
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("!true", false)]
    [InlineData("!!true", true)]
    [InlineData("1 == 1", true)]
    [InlineData("1 == 2", false)]
    [InlineData("1 != 2", true)]
    [InlineData("2 > 1", true)]
    [InlineData("2 >= 2", true)]
    [InlineData("1 < 2", true)]
    [InlineData("1 <= 0", false)]
    [InlineData("true && false", false)]
    [InlineData("true || false", true)]
    [InlineData("false || false && true", false)]
    [InlineData("(true || false) && true", true)]
    [InlineData("'abc' == 'ABC'", true)]
    [InlineData("'abc' != 'abd'", true)]
    [InlineData("'10' > '9'", true)]
    [InlineData("'b' > 'a'", true)]
    public void EvaluatesLiterals(string text, bool expected)
    {
        Assert.Equal(expected, ConditionExpression.Parse(text).Evaluate(_ => null));
    }

    [Fact]
    public void ReadsOptionValues()
    {
        var lookup = Values(("Mode", "Bitrate"), ("Bitrate", "192"), ("Enabled", "true"));

        Assert.True(ConditionExpression.Parse("Mode == 'Bitrate'").Evaluate(lookup));
        Assert.True(ConditionExpression.Parse("Bitrate >= 128").Evaluate(lookup));
        Assert.True(ConditionExpression.Parse("Enabled").Evaluate(lookup));
        Assert.False(ConditionExpression.Parse("!Enabled").Evaluate(lookup));
        Assert.True(ConditionExpression.Parse("Enabled && Mode != 'VBR'").Evaluate(lookup));
    }

    [Fact]
    public void OptionNamesAreCaseInsensitive()
    {
        var lookup = Values(("VideoCodec", "libx264"));
        Assert.True(ConditionExpression.Parse("videocodec == 'LIBX264'").Evaluate(lookup));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("off")]
    public void FalsyStrings(string value)
    {
        Assert.False(ConditionExpression.Parse("Flag").Evaluate(Values(("Flag", value))));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("anything")]
    public void TruthyStrings(string value)
    {
        Assert.True(ConditionExpression.Parse("Flag").Evaluate(Values(("Flag", value))));
    }

    [Fact]
    public void UnknownNameIsFalsyRatherThanAnError()
    {
        Assert.False(ConditionExpression.Parse("Missing").Evaluate(_ => null));
        Assert.True(ConditionExpression.Parse("!Missing").Evaluate(_ => null));
        Assert.True(ConditionExpression.Parse("Missing == ''").Evaluate(_ => null));
    }

    [Fact]
    public void ReportsReferencedNames()
    {
        var expression = ConditionExpression.Parse("A && (B == 'x' || !C) && true");
        Assert.Equal(["A", "B", "C"], expression.ReferencedNames.Order());
    }

    [Theory]
    [InlineData("A &&")]
    [InlineData("(A")]
    [InlineData("A & B")]
    [InlineData("A = B")]
    [InlineData("'unterminated")]
    [InlineData("A B")]
    [InlineData("")]
    public void RejectsMalformedInput(string text)
    {
        Assert.False(ConditionExpression.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void StringEscapes()
    {
        Assert.True(ConditionExpression.Parse(@"'it\'s' == ""it's""").Evaluate(_ => null));
    }
}
