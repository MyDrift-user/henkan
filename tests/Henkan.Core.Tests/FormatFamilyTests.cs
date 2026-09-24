using Henkan.Core.Backends;
using Henkan.Core.Presets;

namespace Henkan.Core.Tests;

public class FormatFamilyTests
{
    [Fact]
    public void ExpandsAFamilyToItsMembers()
    {
        IReadOnlyList<string> audio = FormatFamilies.Expand(["$audio"]);

        Assert.Contains("mp3", audio);
        Assert.Contains("flac", audio);
        Assert.DoesNotContain("png", audio);
    }

    [Fact]
    public void ExpandsNestedFamilies()
    {
        IReadOnlyList<string> documents = FormatFamilies.Expand(["$document"]);

        // $document is made of $text, $spreadsheet and $presentation.
        Assert.Contains("docx", documents);
        Assert.Contains("xlsx", documents);
        Assert.Contains("pptx", documents);
        Assert.DoesNotContain("$text", documents);
    }

    [Fact]
    public void MixesFamiliesWithPlainExtensionsAndDropsDuplicates()
    {
        IReadOnlyList<string> tokens = FormatFamilies.Expand(["$audio", "MP3", ".mp3", "custom"]);

        Assert.Single(tokens, e => e == "mp3");
        Assert.Contains("custom", tokens);
    }

    [Fact]
    public void UnknownFamilyContributesNothingRatherThanThrowing()
    {
        Assert.Equal(["mp3"], FormatFamilies.Expand(["$nonsense", "mp3"]));
    }

    [Theory]
    [InlineData("mp3", true)]
    [InlineData(".MP3", true)]
    [InlineData("png", false)]
    public void MatchesAgainstAFamily(string extension, bool expected) =>
        Assert.Equal(expected, FormatFamilies.Matches(["$audio"], extension));

    [Fact]
    public void EmptyAndWildcardBothAcceptEverything()
    {
        Assert.True(FormatFamilies.Matches([], "anything"));
        Assert.True(FormatFamilies.Matches(["*"], "anything"));
    }

    [Fact]
    public void WildcardTargetExpandsToNoRestriction()
    {
        var target = new ConversionTarget { Id = "x", Label = "X", OutputExtension = "x", InputExtensions = ["*"] };

        Assert.Empty(target.ExpandInputExtensions());
        Assert.True(target.Accepts("whatever"));
    }

    [Fact]
    public void PresetWithoutExtensionsInheritsItsTarget()
    {
        var target = new ConversionTarget { Id = "mp3", Label = "MP3", OutputExtension = "mp3", InputExtensions = ["$audio"] };
        Preset preset = Preset.Create("To MP3", "ffmpeg/mp3");

        IReadOnlyList<string>? resolved = preset.ResolveInputExtensions(target);

        Assert.NotNull(resolved);
        Assert.Contains("wav", resolved);
        Assert.DoesNotContain("png", resolved);
    }

    [Fact]
    public void PresetNarrowsItsTargetButCannotWidenIt()
    {
        var target = new ConversionTarget { Id = "mp3", Label = "MP3", OutputExtension = "mp3", InputExtensions = ["$audio"] };
        Preset preset = Preset.Create("Only from WAV", "ffmpeg/mp3") with { InputExtensions = ["wav", "png"] };

        Assert.Equal(["wav"], preset.ResolveInputExtensions(target));
    }

    [Fact]
    public void PresetThatAgreesWithItsTargetOnNothingIsDropped()
    {
        var target = new ConversionTarget { Id = "mp3", Label = "MP3", OutputExtension = "mp3", InputExtensions = ["$audio"] };
        Preset preset = Preset.Create("Impossible", "ffmpeg/mp3") with { InputExtensions = ["png"] };

        Assert.Null(preset.ResolveInputExtensions(target));
    }
}
