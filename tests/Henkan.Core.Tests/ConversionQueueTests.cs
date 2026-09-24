using Henkan.Core.Backends;
using Henkan.Core.Conversion;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Tools;

namespace Henkan.Core.Tests;

/// <summary>
/// The queue's refusals. ImageMagick runs in process and is always available, so
/// these exercise a real backend without needing a tool on the machine.
/// </summary>
public sealed class ConversionQueueTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "henkan-tests-" + Guid.NewGuid().ToString("n"));

    public ConversionQueueTests() => Directory.CreateDirectory(this.directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(this.directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task FileTheTargetCannotReadFailsWithAReasonRatherThanRunning()
    {
        using ConversionQueue queue = await CreateQueueAsync();
        string song = this.CreateFile("song.mp3");

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([song], Preset.Create("To PNG", "imagemagick/png"));

        ConversionJob job = Assert.Single(jobs);
        Assert.Equal(ConversionState.Failed, job.State);
        Assert.Contains(".mp3", job.Message);
        Assert.Null(job.OutputPath);
    }

    [Fact]
    public async Task MissingFileFailsWithoutTouchingTheBackend()
    {
        using ConversionQueue queue = await CreateQueueAsync();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(
            [Path.Combine(this.directory, "gone.png")],
            Preset.Create("To JPEG", "imagemagick/jpg"));

        ConversionJob job = Assert.Single(jobs);
        Assert.Equal(ConversionState.Failed, job.State);
        Assert.Equal("The file no longer exists.", job.Message);
    }

    [Fact]
    public async Task PresetPointingAtNothingIsReportedAtOnce()
    {
        using ConversionQueue queue = await CreateQueueAsync();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => queue.Enqueue([this.CreateFile("picture.png")], Preset.Create("Nonsense", "nowhere/nothing")));

        Assert.Contains("nowhere/nothing", error.Message);
    }

    [Fact]
    public async Task RejectedJobsStillCountAsFinished()
    {
        using ConversionQueue queue = await CreateQueueAsync();
        bool announced = false;
        queue.AllFinished += (_, _) => announced = true;

        queue.Enqueue([this.CreateFile("song.mp3")], Preset.Create("To PNG", "imagemagick/png"));

        Assert.True(announced);
        Assert.False(queue.HasActiveJobs);
    }

    [Fact]
    public async Task AListenerThatThrowsDoesNotDestroyTheResult()
    {
        using ConversionQueue queue = await CreateQueueAsync();
        string source = this.CreateImage("picture.png");
        string destination = Path.Combine(this.directory, "picture.jpg");

        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(
            [source],
            Preset.Create("To JPEG", "imagemagick/jpg") with
            {
                OutputPathTemplate = destination,
                ConflictPolicy = FileConflictPolicy.Overwrite,
            });

        // A XAML binding updated from a worker thread throws exactly like this.
        // It used to be caught as a failed conversion, and the finished file was
        // deleted on the way out.
        jobs[0].PropertyChanged += (_, _) => throw new InvalidOperationException("wrong thread");

        await finished.Task.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(ConversionState.Succeeded, jobs[0].State);
        Assert.True(File.Exists(destination), "The converted file was deleted because a listener threw.");
    }

    [Fact]
    public async Task AFolderIsRefusedByATargetThatOnlyReadsFiles()
    {
        using ConversionQueue queue = await CreateQueueAsync();
        string folder = Directory.CreateDirectory(Path.Combine(this.directory, "pictures")).FullName;

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([folder], Preset.Create("To JPEG", "imagemagick/jpg"));

        ConversionJob job = Assert.Single(jobs);
        Assert.Equal(ConversionState.Failed, job.State);
        Assert.Contains("folder", job.Message);
    }

    [Fact]
    public async Task AFolderIsAcceptedByATargetThatArchivesOne()
    {
        var registry = new BackendRegistry(new ToolLocator());
        await registry.ReloadAsync();

        // 7-Zip is fetched at build time, so a clone that has not run
        // fetch-deps has nothing to test here.
        if (registry.FindTarget("sevenzip/zip") is not { IsAvailable: true })
        {
            return;
        }

        using var queue = new ConversionQueue(registry, 1);
        string folder = Directory.CreateDirectory(Path.Combine(this.directory, "papers")).FullName;
        File.WriteAllText(Path.Combine(folder, "one.txt"), "hello");

        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([folder], Preset.Create("To ZIP", "sevenzip/zip"));
        await finished.Task.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.Equal(ConversionState.Succeeded, jobs[0].State);
        Assert.True(File.Exists(jobs[0].OutputPath), "The folder was not archived.");
    }

    [Fact]
    public async Task TwoFilesThatWantTheSameOutputNameBothGetOne()
    {
        var registry = new BackendRegistry(new ToolLocator());
        await registry.ReloadAsync();

        using var queue = new ConversionQueue(registry, 4);

        // Both of these render report.jpg. Resolved independently they see a free
        // name, take it, and one of them loses the file underneath it.
        string first = this.CreateImage("report.png");
        string second = this.CreateImage("report.bmp");

        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([first, second], Preset.Create("To JPEG", "imagemagick/jpg"));
        await finished.Task.WaitAsync(TimeSpan.FromMinutes(1));

        Assert.All(jobs, job => Assert.Equal(ConversionState.Succeeded, job.State));
        Assert.Equal(2, jobs.Select(j => j.OutputPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(jobs, job => Assert.True(File.Exists(job.OutputPath), $"{job.OutputPath} is missing."));
    }

    [Fact]
    public async Task ASelectionBecomesOneArchiveRatherThanOneEach()
    {
        var registry = new BackendRegistry(new ToolLocator());
        await registry.ReloadAsync();

        if (registry.FindTarget("sevenzip/zip") is not { IsAvailable: true })
        {
            return;
        }

        using var queue = new ConversionQueue(registry, 2);
        string[] inputs =
        [
            this.CreateFile("one.txt"),
            this.CreateFile("two.txt"),
            this.CreateFile("three.txt"),
        ];

        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(inputs, Preset.Create("To ZIP", "sevenzip/zip"));
        await finished.Task.WaitAsync(TimeSpan.FromMinutes(1));

        ConversionJob job = Assert.Single(jobs);
        Assert.Equal(ConversionState.Succeeded, job.State);
        Assert.Equal(3, job.Inputs.Count);
        Assert.True(File.Exists(job.OutputPath));

        // Named after the folder they came from, since no one file can name them.
        Assert.Equal(Path.GetFileName(this.directory) + ".zip", Path.GetFileName(job.OutputPath!));
    }

    [Fact]
    public async Task ASelectionStillConvertsOneFileAtATimeWhenTheTargetDoesNotCombine()
    {
        using ConversionQueue queue = await CreateQueueAsync();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(
            [this.CreateImage("a.png"), this.CreateImage("b.png")],
            Preset.Create("To JPEG", "imagemagick/jpg"));

        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public async Task DisposingWhileWorkIsInFlightDoesNotTakeTheProcessWithIt()
    {
        var registry = new BackendRegistry(new ToolLocator());
        await registry.ReloadAsync();

        // Enqueued and then pulled out from underneath, which is what happens
        // when the application exits mid-conversion. Releasing a semaphore that
        // has been disposed throws on a thread pool thread, and that ends the
        // process rather than reporting anything.
        using (var queue = new ConversionQueue(registry, 2))
        {
            queue.Enqueue([this.CreateImage("busy.png")], Preset.Create("To TIFF", "imagemagick/tiff"));
        }

        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    private string CreateImage(string name)
    {
        string path = Path.Combine(this.directory, name);

        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.CornflowerBlue, 64, 48);
        image.Write(path);

        return path;
    }

    private static async Task<ConversionQueue> CreateQueueAsync()
    {
        var registry = new BackendRegistry(new ToolLocator());
        await registry.ReloadAsync();
        return new ConversionQueue(registry, 2);
    }

    private string CreateFile(string name)
    {
        string path = Path.Combine(this.directory, name);
        File.WriteAllBytes(path, [0, 1, 2, 3]);
        return path;
    }
}
