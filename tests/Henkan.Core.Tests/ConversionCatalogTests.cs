using Henkan.Core.Backends;
using Henkan.Core.Presets;

namespace Henkan.Core.Tests;

/// <summary>
/// Guards the shape of the shipped catalogue rather than any one conversion.
/// A target that quietly claims to accept everything is what puts "To MP3" on
/// the context menu of a photograph, and a starter preset pointing at a target
/// that no longer exists disappears from the menu without a word.
/// </summary>
public class ConversionCatalogTests
{
    private static readonly IReadOnlyList<BackendDefinition> Definitions =
        [.. BackendRegistry.LoadBuiltInDefinitions().Select(d => d.Definition)];

    public static IEnumerable<object[]> Targets() =>
        Definitions.SelectMany(d => d.Targets.Select(t => new object[] { $"{d.Id}/{t.Id}", t }));

    [Theory]
    [MemberData(nameof(Targets))]
    public void EveryTargetNamesTheInputsItAccepts(string key, ConversionTarget target)
    {
        if (target.Category == "Archive")
        {
            // Putting a file into an archive works on any file there is, which is
            // the one case where naming the inputs would be a lie by omission.
            return;
        }

        IReadOnlyList<string> accepted = target.ExpandInputExtensions();

        Assert.False(accepted.Count == 0, $"{key} accepts every extension, so it will be offered for files it cannot read.");
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public void NoTargetProducesWhatItCannotRead(string key, ConversionTarget target)
    {
        // Not a rule of nature, but every current target that reads its own output
        // format should keep doing so, and one that does not is worth noticing.
        // Joining several files into one is the exception: PDFs to a PDF is the
        // whole point of merging.
        Assert.False(
            !target.CombinesInputs
            && target.InputExtensions.Count == 1 && target.InputExtensions[0].Equals(target.OutputExtension, StringComparison.OrdinalIgnoreCase),
            $"{key} converts a format to itself and nothing else.");
    }

    [Theory]
    [InlineData("Audio", "png")]
    [InlineData("Audio", "docx")]
    [InlineData("Video", "mp3")]
    [InlineData("Video", "xlsx")]
    [InlineData("Image", "mp3")]
    [InlineData("Image", "wav")]
    public void NoTargetInACategoryAcceptsNonsense(string category, string extension)
    {
        foreach (BackendDefinition definition in Definitions)
        {
            foreach (ConversionTarget target in definition.Targets.Where(t => t.Category == category))
            {
                Assert.False(
                    target.Accepts(extension),
                    $"{definition.Id}/{target.Id} is {category} but accepts a .{extension} file.");
            }
        }
    }

    [Fact]
    public void EveryStarterPresetPointsAtATargetThatExists()
    {
        foreach (Preset preset in PresetStore.CreateStarterPresets())
        {
            string[] parts = preset.TargetKey.Split('/', 2);
            Assert.Equal(2, parts.Length);

            BackendDefinition? definition = Definitions.FirstOrDefault(d => d.Id == parts[0]);
            Assert.NotNull(definition);
            Assert.NotNull(definition.FindTarget(parts[1]));
        }
    }

    [Fact]
    public void StarterPresetsLeaveTheirExtensionsToTheirTargets()
    {
        Assert.All(PresetStore.CreateStarterPresets(), preset => Assert.Empty(preset.InputExtensions));
    }

    [Fact]
    public void TargetIdsAreUniqueWithinABackend()
    {
        foreach (BackendDefinition definition in Definitions)
        {
            string[] ids = [.. definition.Targets.Select(t => t.Id)];
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void AnAudioFileIsOfferedAudioConversionsAndNothingElse()
    {
        string[] categories = [.. Definitions
            .SelectMany(d => d.Targets)
            .Where(t => t.Accepts("mp3"))
            .Select(t => t.Category)
            .Distinct()
            .Order()];

        // Archive, because any file at all can be put into one.
        Assert.Equal(["Archive", "Audio"], categories);
    }

    [Fact]
    public void AVideoFileIsOfferedAudioVideoAndAStillFrame()
    {
        string[] categories = [.. Definitions
            .SelectMany(d => d.Targets)
            .Where(t => t.Accepts("mp4"))
            .Select(t => t.Category)
            .Distinct()
            .Order()];

        Assert.Equal(["Archive", "Audio", "Image", "Video"], categories);
    }
}
