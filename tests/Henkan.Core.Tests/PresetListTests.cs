using System.Text.Json;
using Henkan.Core.Presets;
using Henkan.Core.Settings;

namespace Henkan.Core.Tests;

public class PresetListTests
{
    private static Preset Make(string name, string key, int order, bool variant = false) =>
        Preset.Create(name, key) with { SortOrder = order, IsVariant = variant };

    [Fact]
    public void AFileFromBeforeVariantsKeepsTheFirstEntryPerConversion()
    {
        List<Preset> normalized = PresetList.Normalize(
        [
            Make("MP3 320k", "ffmpeg/mp3", 20),
            Make("To MP3", "ffmpeg/mp3", 10),
            Make("To FLAC", "ffmpeg/flac", 30),
        ]);

        Assert.Equal("To MP3", PresetList.EntryFor(normalized, "ffmpeg/mp3")!.Name);
        Assert.Equal(["MP3 320k"], PresetList.VariantsOf(normalized, "ffmpeg/mp3").Select(p => p.Name));
        Assert.False(PresetList.EntryFor(normalized, "ffmpeg/flac")!.IsVariant);
    }

    [Fact]
    public void AVariantWithoutAnEntryStaysAVariant()
    {
        List<Preset> normalized = PresetList.Normalize([Make("MP3 320k", "ffmpeg/mp3", 10, variant: true)]);

        Assert.Null(PresetList.EntryFor(normalized, "ffmpeg/mp3"));
        Assert.Single(PresetList.VariantsOf(normalized, "ffmpeg/mp3"));
    }

    [Fact]
    public void KeysAreComparedWithoutRegardToCase()
    {
        List<Preset> list = [Make("To MP3", "FFmpeg/MP3", 10)];

        Assert.NotNull(PresetList.EntryFor(list, "ffmpeg/mp3"));
    }

    [Fact]
    public void ACopiedBackendTakesItsEntriesAlong()
    {
        List<Preset> moved = PresetList.RetargetBackend(
            [Make("To MP3", "ffmpeg/mp3", 10), Make("To PNG", "imagemagick/png", 20)],
            "ffmpeg",
            "ffmpeg-mine");

        Assert.Equal(["ffmpeg-mine/mp3", "imagemagick/png"], moved.Select(p => p.TargetKey));
    }

    [Fact]
    public void ARenamedConversionTakesItsEntriesAlong()
    {
        List<Preset> moved = PresetList.Retarget(
            [Make("To MP3", "ffmpeg/mp3", 10), Make("MP3 320k", "ffmpeg/mp3", 20, variant: true), Make("To FLAC", "ffmpeg/flac", 30)],
            "ffmpeg/mp3",
            "ffmpeg/mpeg3");

        Assert.Equal(["ffmpeg/mpeg3", "ffmpeg/mpeg3", "ffmpeg/flac"], moved.Select(p => p.TargetKey));
    }

    [Fact]
    public void NewEntriesGoToTheEndOfTheMenu()
    {
        Assert.Equal(40, PresetList.NextSortOrder([Make("a", "x/a", 10), Make("b", "x/b", 30)]));
        Assert.Equal(10, PresetList.NextSortOrder([]));
    }

    [Fact]
    public void TheVariantFlagSurvivesTheFile()
    {
        Preset preset = Make("MP3 320k", "ffmpeg/mp3", 10, variant: true);
        string json = JsonSerializer.Serialize(preset, JsonDefaults.Options);

        Assert.True(JsonSerializer.Deserialize<Preset>(json, JsonDefaults.Options)!.IsVariant);
    }

    [Fact]
    public void AnOldFileWithoutTheFlagReadsAsEntries()
    {
        const string json = """{ "id": "a", "name": "To MP3", "targetKey": "ffmpeg/mp3" }""";

        Assert.False(JsonSerializer.Deserialize<Preset>(json, JsonDefaults.Options)!.IsVariant);
    }

    [Fact]
    public void TheStoreNeverSavesTwoEntriesForOneConversion()
    {
        string path = Path.Combine(Path.GetTempPath(), $"henkan-presets-{Guid.NewGuid():n}.json");

        try
        {
            var store = new PresetStore(path);
            store.Save([Make("To MP3", "ffmpeg/mp3", 10), Make("Also MP3", "ffmpeg/mp3", 20)]);

            IReadOnlyList<Preset> loaded = new PresetStore(path).Load();

            Assert.Single(loaded, p => !p.IsVariant);
            Assert.Single(loaded, p => p.IsVariant);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
