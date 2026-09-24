using Henkan.Core.Conversion;
using Henkan.Core.Settings;

namespace Henkan.Core.Tests;

public sealed class OutputPathResolverTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "henkan-tests", Guid.NewGuid().ToString("n"));

    public OutputPathResolverTests()
    {
        Directory.CreateDirectory(this.root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void DefaultTemplateSitsBesideTheInput()
    {
        string input = this.Touch("clip.mov");
        string? output = OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Rename);
        Assert.Equal(Path.Combine(this.root, "clip.mp4"), output);
    }

    [Fact]
    public void RelativeTemplateIsAnchoredAtTheInputDirectory()
    {
        string input = this.Touch("clip.mov");
        string? output = OutputPathResolver.Resolve("converted\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Rename);
        Assert.Equal(Path.Combine(this.root, "converted", "clip.mp4"), output);
    }

    [Fact]
    public void RenamesOnConflict()
    {
        string input = this.Touch("clip.mov");
        this.Touch("clip.mp4");
        this.Touch("clip (2).mp4");

        string? output = OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Rename);
        Assert.Equal(Path.Combine(this.root, "clip (3).mp4"), output);
    }

    [Fact]
    public void OverwriteReturnsTheExistingPath()
    {
        string input = this.Touch("clip.mov");
        string existing = this.Touch("clip.mp4");

        Assert.Equal(existing, OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Overwrite));
    }

    [Fact]
    public void SkipReturnsNullAndFailThrows()
    {
        string input = this.Touch("clip.mov");
        this.Touch("clip.mp4");

        Assert.Null(OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Skip));
        Assert.Throws<ConversionSkippedException>(() =>
            OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "mp4", FileConflictPolicy.Fail));
    }

    [Fact]
    public void NeverOverwritesTheInputItself()
    {
        string input = this.Touch("photo.jpg");
        string? output = OutputPathResolver.Resolve("{inputDir}\\{inputName}.{outputExt}", input, "jpg", FileConflictPolicy.Overwrite);
        Assert.Equal(Path.Combine(this.root, "photo (2).jpg"), output);
    }

    private string Touch(string name)
    {
        string path = Path.Combine(this.root, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
