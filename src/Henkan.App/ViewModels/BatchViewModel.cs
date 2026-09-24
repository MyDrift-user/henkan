using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Henkan.App.Services;
using Henkan.Core.Conversion;
using Microsoft.UI.Dispatching;

namespace Henkan.App.ViewModels;

/// <summary>
/// One run started from the Explorer menu: the files it covers and how far along
/// they are. Backs the small progress window rather than the main one.
/// </summary>
/// <remarks>
/// This watches only its own jobs, not the whole queue, so a conversion started
/// while another is still running gets its own honest count instead of inheriting
/// someone else's total.
/// </remarks>
public sealed partial class BatchViewModel : ObservableObject
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private bool refreshQueued;
    private bool announced;

    [ObservableProperty]
    public partial string Title { get; set; } = "Converting";

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsRunning { get; set; } = true;

    [ObservableProperty]
    public partial bool HasFailures { get; set; }

    public BatchViewModel()
    {
        AppServices.Queue.JobFinished += this.OnJobFinished;
        this.Jobs.CollectionChanged += (_, _) => this.Refresh();
    }

    /// <summary>Raised once every job in this batch has finished and none failed.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised when the batch finishes with at least one failure.</summary>
    public event EventHandler? Failed;

    /// <summary>Raised on the calling thread as each file joins the batch.</summary>
    public event EventHandler<ConversionJob>? JobAdded;

    /// <summary>Raised once per file when it stops, whatever the outcome.</summary>
    public event EventHandler<ConversionJob>? JobEnded;

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    /// <summary>Adds the files of one context menu invocation to this batch.</summary>
    public void Add(IReadOnlyList<ConversionJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        // A second right click while this window is open joins the same batch,
        // which reopens it for an outcome it may already have announced.
        this.announced = false;

        foreach (ConversionJob job in jobs)
        {
            job.PropertyChanged += this.OnJobChanged;
            this.Jobs.Add(job);
            this.JobAdded?.Invoke(this, job);
        }

        this.Refresh();
    }

    /// <summary>True once every file in the batch has stopped.</summary>
    public bool IsFinished => this.Jobs.Count > 0 && this.Jobs.All(j => j.IsFinished);

    /// <summary>
    /// Records a failure that happened before any job could be created, so the
    /// presenter still has something to show instead of nothing at all.
    /// </summary>
    public void AddProblem(string message)
    {
        this.IsRunning = false;
        this.HasFailures = true;
        this.announced = true;
        this.Title = "Henkan could not start";
        this.Summary = message;
        this.Failed?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        AppServices.Queue.JobFinished -= this.OnJobFinished;

        foreach (ConversionJob job in this.Jobs)
        {
            job.PropertyChanged -= this.OnJobChanged;
        }
    }

    [RelayCommand]
    private void CancelAll()
    {
        foreach (ConversionJob job in this.Jobs.Where(j => !j.IsFinished))
        {
            AppServices.Queue.Cancel(job);
        }
    }

    [RelayCommand]
    private void ShowInFolder(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string? result = job.OutputPath is not null && File.Exists(job.OutputPath) ? job.OutputPath : null;
        string arguments = result is null
            ? $"\"{Path.GetDirectoryName(job.InputPath)}\""
            : $"/select,\"{result}\"";

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorReport.Log(ex, "Could not open Explorer");
        }
    }

    private void OnJobFinished(object? sender, ConversionJob job)
    {
        if (!this.Jobs.Contains(job))
        {
            return;
        }

        this.dispatcher.TryEnqueue(() => this.JobEnded?.Invoke(this, job));
        this.RequestRefresh();
    }

    private void OnJobChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConversionJob.Progress) or nameof(ConversionJob.State))
        {
            this.RequestRefresh();
        }
    }

    /// <summary>
    /// Coalesces the refresh. A running ffmpeg reports progress many times a
    /// second per file, and one recount of the batch per report is plenty.
    /// </summary>
    private void RequestRefresh()
    {
        if (this.refreshQueued)
        {
            return;
        }

        this.refreshQueued = true;
        this.dispatcher.TryEnqueue(() =>
        {
            this.refreshQueued = false;
            this.Refresh();
        });
    }

    private void Refresh()
    {
        int total = this.Jobs.Count;

        if (total == 0)
        {
            return;
        }

        int finished = this.Jobs.Count(j => j.IsFinished);
        int failed = this.Jobs.Count(j => j.State is ConversionState.Failed);
        int skipped = this.Jobs.Count(j => j.State is ConversionState.Skipped);
        int cancelled = this.Jobs.Count(j => j.State is ConversionState.Cancelled);

        // Partial progress of the running jobs counts, or a single long
        // conversion would sit at zero for its whole duration.
        this.Progress = this.Jobs.Sum(j => j.IsFinished ? 1d : j.Progress) / total;
        this.IsRunning = finished < total;
        this.HasFailures = failed > 0 || skipped > 0;

        if (this.IsRunning)
        {
            this.Title = total == 1 ? "Converting" : $"Converting {total} files";
            this.Summary = $"{finished} of {total} done";
            return;
        }

        var parts = new List<string>();

        int converted = finished - failed - skipped - cancelled;

        if (converted > 0)
        {
            parts.Add($"{converted} converted");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped");
        }

        if (cancelled > 0)
        {
            parts.Add($"{cancelled} cancelled");
        }

        this.Title = this.HasFailures ? "Henkan could not finish" : "Done";
        this.Summary = string.Join(", ", parts);

        // The outcome is announced once. Refresh runs again on every later
        // property change, and closing the window twice is not a nice thing.
        if (this.announced)
        {
            return;
        }

        this.announced = true;

        if (this.HasFailures)
        {
            this.Failed?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            this.Completed?.Invoke(this, EventArgs.Empty);
        }
    }
}
