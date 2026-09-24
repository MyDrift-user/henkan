using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Henkan.App.Services;
using Henkan.Core.Conversion;
using Microsoft.UI.Dispatching;

namespace Henkan.App.ViewModels;

/// <summary>
/// The queue page: what has run, what is running, and what went wrong.
/// </summary>
/// <remarks>
/// Conversions are started from the Explorer menu, so this is a record rather
/// than a control panel. It keeps failures in view until they are cleared,
/// because a failure noticed a day later is still better than one never noticed.
/// </remarks>
public sealed partial class QueueViewModel : ObservableObject
{
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    public QueueViewModel()
    {
        AppServices.Queue.JobFinished += (_, _) => this.dispatcher.TryEnqueue(this.Refresh);
        this.Jobs.CollectionChanged += (_, _) => this.dispatcher.TryEnqueue(this.Refresh);
        this.Refresh();
    }

    public ObservableCollection<ConversionJob> Jobs => AppServices.Queue.Jobs;

    public bool IsEmpty => this.Jobs.Count == 0;

    [RelayCommand]
    private void CancelJob(ConversionJob job) => AppServices.Queue.Cancel(job);

    [RelayCommand]
    private void CancelAll() => AppServices.Queue.CancelAll();

    [RelayCommand]
    private void ClearFinished() => AppServices.Queue.ClearFinished();

    [RelayCommand]
    private void OpenOutput(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.OutputPath is null || !File.Exists(job.OutputPath))
        {
            this.StatusMessage = "There is no output file to open.";
            return;
        }

        this.Start(new ProcessStartInfo(job.OutputPath) { UseShellExecute = true }, "open the result");
    }

    [RelayCommand]
    private void OpenOutputFolder(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        string? result = job.OutputPath is not null && File.Exists(job.OutputPath) ? job.OutputPath : null;
        string arguments = result is null
            ? $"\"{Path.GetDirectoryName(job.InputPath)}\""
            : $"/select,\"{result}\"";

        this.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true }, "open Explorer");
    }

    private void Start(ProcessStartInfo startInfo, string what)
    {
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            this.StatusMessage = $"Could not {what}: {ex.Message}";
            ErrorReport.Log(ex, $"Could not {what}");
        }
    }

    private void Refresh()
    {
        this.OnPropertyChanged(nameof(this.IsEmpty));

        int failed = this.Jobs.Count(j => j.State == ConversionState.Failed);
        int skipped = this.Jobs.Count(j => j.State == ConversionState.Skipped);
        int done = this.Jobs.Count(j => j.State == ConversionState.Succeeded);
        int running = this.Jobs.Count(j => !j.IsFinished);

        var parts = new List<string>();

        if (running > 0)
        {
            parts.Add($"{running} in flight");
        }

        if (done > 0)
        {
            parts.Add($"{done} converted");
        }

        if (failed > 0)
        {
            parts.Add($"{failed} failed");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped");
        }

        this.StatusMessage = string.Join(", ", parts);
    }
}
