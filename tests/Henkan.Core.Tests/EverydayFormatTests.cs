using Henkan.Core.Backends;
using Henkan.Core.Conversion;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Templating;

namespace Henkan.Core.Tests;

/// <summary>
/// The pieces added so the menu covers what people convert day to day: several
/// files into one, extensions with a dot in them, and new entries reaching an
/// existing installation exactly once.
/// </summary>
public class EverydayFormatTests
{
    [Fact]
    public void AllSelectedFilesBecomeOneArgumentEach()
    {
        VariableContext context = VariableContext.Create(@"C:\a\one.pdf", @"C:\a\one merged.pdf", @"C:\t")
            .WithInputs([@"C:\a\one.pdf", @"C:\a\two with spaces.pdf"]);

        IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(
            [new ArgumentFragment { Value = "-o {output}" }, new ArgumentFragment { Value = "{inputs}" }],
            context);

        Assert.Equal(["-o", @"C:\a\one merged.pdf", @"C:\a\one.pdf", @"C:\a\two with spaces.pdf"], arguments);
    }

    [Fact]
    public void WithoutASelectionInputsIsTheOneInput()
    {
        VariableContext context = VariableContext.Create(@"C:\a\one.pdf", @"C:\a\out.pdf", @"C:\t");

        IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments([new ArgumentFragment { Value = "{inputs}" }], context);

        Assert.Equal([@"C:\a\one.pdf"], arguments);
    }

    [Fact]
    public void ACompressedTarKeepsBothHalvesOfItsExtension()
    {
        string input = Path.Combine(Path.GetTempPath(), $"henkan-{Guid.NewGuid():n}", "project");

        string? output = OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "tar.gz", FileConflictPolicy.Rename);

        Assert.Equal(input + ".tar.gz", output);
    }

    [Fact]
    public void ATakenTarGetsItsNumberBeforeTheWholeExtension()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"henkan-{Guid.NewGuid():n}");

        string next = OutputPathResolver.NextFreeName(Path.Combine(folder, "project.tar.gz"), alreadyTaken: _ => false);

        Assert.Equal(Path.Combine(folder, "project (2).tar.gz"), next);
    }

    [Theory]
    [InlineData("tar.gz", "tar", "gzip")]
    [InlineData("tar.xz", "tar", "xz")]
    [InlineData("zip", "zip", null)]
    public void CompressedTarsAreBuiltInTwoSteps(string format, string inner, string? outer)
    {
        Assert.Equal((inner, outer), ArchiveBackend.SplitCompound(format));
    }

    [Fact]
    public void NewStartersReachAnOldInstallationOnce()
    {
        string path = Path.Combine(Path.GetTempPath(), $"henkan-presets-{Guid.NewGuid():n}.json");

        try
        {
            var store = new PresetStore(path);
            store.Save([Preset.Create("To MP3", "ffmpeg/mp3") with { SortOrder = 10 }]);
            store.Load();

            int first = store.AddNewStarters(1);
            int second = store.AddNewStarters(PresetStore.StarterVersion);

            Assert.True(first > 20, $"Only {first} entries were added.");
            Assert.Equal(0, second);
            Assert.Contains(store.Presets, p => p.TargetKey == "pipeline/text-dotx");

            // The first set is not brought back: the user had it and chose.
            Assert.DoesNotContain(store.Presets, p => p.TargetKey == "ffmpeg/flac");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AConversionTheUserAlreadyHasIsLeftAlone()
    {
        string path = Path.Combine(Path.GetTempPath(), $"henkan-presets-{Guid.NewGuid():n}.json");

        try
        {
            var store = new PresetStore(path);
            store.Save([Preset.Create("My DOTX", "pipeline/text-dotx") with { ShowInContextMenu = false }]);
            store.Load();

            store.AddNewStarters(1);

            Preset only = Assert.Single(store.Presets, p => p.TargetKey == "pipeline/text-dotx");
            Assert.Equal("My DOTX", only.Name);
            Assert.False(only.ShowInContextMenu);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFreshInstallationGetsEverySet()
    {
        Preset[] starters = [.. PresetStore.CreateStarterPresets()];

        Assert.Contains(starters, p => p.TargetKey == "ffmpeg/mp3");
        Assert.Contains(starters, p => p.TargetKey == "office/powerpoint-mp4");
        Assert.Equal(starters.Length, starters.Select(p => p.TargetKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
