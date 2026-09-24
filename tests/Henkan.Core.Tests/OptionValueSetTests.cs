using Henkan.Core.Options;

namespace Henkan.Core.Tests;

public class OptionValueSetTests
{
    private static readonly OptionDescriptor[] Schema =
    [
        new() { Id = "Mode", Label = "Mode", Kind = OptionKind.Choice, Default = "CRF", Choices = [new() { Value = "CRF" }, new() { Value = "Bitrate" }] },
        new() { Id = "Crf", Label = "CRF", Kind = OptionKind.Range, Default = "23", Minimum = 0, Maximum = 51, VisibleWhen = "Mode == 'CRF'" },
        new() { Id = "Bitrate", Label = "Bitrate", Kind = OptionKind.Integer, Default = "4000", VisibleWhen = "Mode == 'Bitrate'" },
        new() { Id = "Fast", Label = "Fast", Kind = OptionKind.Boolean, Default = "false" },
        new() { Id = "Broken", Label = "Broken", Kind = OptionKind.Text, VisibleWhen = "Mode ==" },
    ];

    [Fact]
    public void FillsDefaultsAndAppliesOverrides()
    {
        var set = OptionValueSet.FromSchema(Schema, new Dictionary<string, string> { ["Crf"] = "18", ["Unknown"] = "x" });

        Assert.Equal("CRF", set.GetString("Mode"));
        Assert.Equal(18, set.GetInt32("Crf"));
        Assert.Equal(4000, set.GetInt32("Bitrate"));
        Assert.False(set.GetBoolean("Fast"));
        Assert.False(set.ContainsKey("Unknown"));
    }

    [Fact]
    public void VisibilityFollowsOtherValues()
    {
        var set = OptionValueSet.FromSchema(Schema);
        Assert.True(set.IsVisible(Schema[1]));
        Assert.False(set.IsVisible(Schema[2]));

        set.Set("Mode", "Bitrate");
        Assert.False(set.IsVisible(Schema[1]));
        Assert.True(set.IsVisible(Schema[2]));
    }

    [Fact]
    public void MalformedConditionShowsTheOption()
    {
        var set = OptionValueSet.FromSchema(Schema);
        Assert.True(set.IsVisible(Schema[4]));
    }

    [Fact]
    public void OverridesOnlyStoreDifferencesFromDefaults()
    {
        var set = OptionValueSet.FromSchema(Schema);
        set.Set("Crf", "18");
        set.Set("Fast", true);
        set.Set("Mode", "CRF");

        var overrides = set.ToOverrides(Schema);
        Assert.Equal(2, overrides.Count);
        Assert.Equal("18", overrides["Crf"]);
        Assert.Equal("true", overrides["Fast"]);
    }

    [Fact]
    public void CoercesLooseBooleanSpellings()
    {
        var set = new OptionValueSet();
        set.Set("A", "Yes");
        set.Set("B", "OFF");
        set.Set("C", "1");
        Assert.True(set.GetBoolean("A"));
        Assert.False(set.GetBoolean("B"));
        Assert.True(set.GetBoolean("C"));
        Assert.True(set.GetBoolean("Missing", fallback: true));
    }
}
