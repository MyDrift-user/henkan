using System.Collections.ObjectModel;
using Henkan.Core.Backends;
using Henkan.Core.Options;
using Henkan.Core.Presets;

namespace Henkan.Core.Conversion;

/// <summary>
/// Runs jobs with bounded parallelism and exposes them as an observable list.
/// </summary>
/// <remarks>
/// The queue never removes a job on its own; finished rows stay visible with
/// their outcome until the user clears them, which is what makes a failure
/// noticeable in a batch of fifty.
/// </remarks>
public sealed class ConversionQueue : IDisposable, IOutputReservation
{
    private readonly BackendRegistry registry;
    private readonly SynchronizationContext? notifications;
    private readonly SemaphoreSlim gate;
    private readonly CancellationTokenSource shutdown = new();
    private readonly Dictionary<ConversionJob, CancellationTokenSource> running = [];
    private readonly Lock runningGate = new();
    private readonly HashSet<string> claimed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock claimGate = new();
    private int parallelism;

    /// <param name="notifications">
    /// The context a job's property changes are raised on. Defaults to whichever
    /// one the queue was built on, which for the application is the thread its
    /// bindings live on and for a console is none at all.
    /// </param>
    public ConversionQueue(BackendRegistry registry, int parallelism, SynchronizationContext? notifications = null)
    {
        ArgumentNullException.ThrowIfNull(registry);

        this.registry = registry;
        this.notifications = notifications ?? SynchronizationContext.Current;
        this.parallelism = Math.Max(1, parallelism);
        this.gate = new SemaphoreSlim(this.parallelism, int.MaxValue);
    }

    public event EventHandler<ConversionJob>? JobFinished;

    public event EventHandler? AllFinished;

    public ObservableCollection<ConversionJob> Jobs { get; } = [];

    public int Parallelism => this.parallelism;

    /// <summary>Adjusts how many jobs may run at once without restarting anything in flight.</summary>
    public void SetParallelism(int value)
    {
        value = Math.Max(1, value);
        int delta = value - this.parallelism;
        this.parallelism = value;

        if (delta > 0)
        {
            this.gate.Release(delta);
        }
        else
        {
            // Absorb permits as they come back rather than blocking here.
            for (int i = 0; i < -delta; i++)
            {
                _ = this.gate.WaitAsync(this.shutdown.Token);
            }
        }
    }

    /// <summary>
    /// Creates a job for every file and starts it. Files the preset's target does
    /// not accept are added as failed jobs so the user sees why nothing happened.
    /// </summary>
    public IReadOnlyList<ConversionJob> Enqueue(IEnumerable<string> inputPaths, Preset preset)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentNullException.ThrowIfNull(preset);

        TargetDescriptor? target = this.registry.FindTarget(preset.TargetKey);
        if (target is null)
        {
            throw new InvalidOperationException(
                $"Preset \"{preset.Name}\" refers to \"{preset.TargetKey}\", which no backend provides.");
        }

        IReadOnlyList<OptionDescriptor> schema = target.Value.Backend.Definition.GetOptionSchema(target.Value.Target);
        var jobs = new List<ConversionJob>();

        // Archiving a selection is one conversion, not one per file. Everything
        // else is per file, so the list is split accordingly before any job is
        // made.
        IEnumerable<IReadOnlyList<string>> groups = target.Value.Target.CombinesInputs
            ? [[.. inputPaths]]
            : inputPaths.Select(IReadOnlyList<string> (p) => [p]);

        foreach (IReadOnlyList<string> group in groups)
        {
            string inputPath = group[0];
            OptionValueSet options = OptionValueSet.FromSchema(schema, preset.Options);
            var job = new ConversionJob(group, target.Value, preset, options, this.notifications, this);
            this.Jobs.Add(job);
            jobs.Add(job);

            if (group.Select(p => RejectionReason(p, target.Value)).FirstOrDefault(r => r is not null) is { } reason)
            {
                job.Reject(reason);
                this.JobFinished?.Invoke(this, job);
                continue;
            }

            _ = this.RunAsync(job);
        }

        if (jobs.All(j => j.IsFinished))
        {
            this.AllFinished?.Invoke(this, EventArgs.Empty);
        }

        return jobs;
    }

    /// <summary>
    /// Why a file cannot go through this target, or null when it can.
    /// </summary>
    /// <remarks>
    /// Checked before the job runs rather than left to the tool, because "ffmpeg
    /// exited with code 1" is a far worse answer than naming the mismatch.
    /// </remarks>
    private static string? RejectionReason(string inputPath, TargetDescriptor target)
    {
        if (Directory.Exists(inputPath))
        {
            if (!target.Target.AcceptsFolders)
            {
                return $"{target.Target.Label} cannot be made from a folder.";
            }
        }
        else if (!File.Exists(inputPath))
        {
            return "The file no longer exists.";
        }
        else
        {
            string extension = Path.GetExtension(inputPath).TrimStart('.');

            if (!target.Target.Accepts(extension))
            {
                return extension.Length == 0
                    ? $"{target.Target.Label} cannot be made from a file with no extension."
                    : $"{target.Target.Label} cannot be made from a .{extension.ToLowerInvariant()} file.";
            }
        }

        if (!target.IsAvailable)
        {
            return target.Backend.Availability.Reason ?? $"{target.Backend.Definition.Name} is not available.";
        }

        return null;
    }

    /// <inheritdoc />
    public bool IsReserved(string path)
    {
        lock (this.claimGate)
        {
            return this.claimed.Contains(path);
        }
    }

    /// <inheritdoc />
    public void Reserve(string path)
    {
        lock (this.claimGate)
        {
            this.claimed.Add(path);
        }
    }

    /// <inheritdoc />
    public void Release(string path)
    {
        lock (this.claimGate)
        {
            this.claimed.Remove(path);
        }
    }

    public void Cancel(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        lock (this.runningGate)
        {
            if (this.running.TryGetValue(job, out CancellationTokenSource? source))
            {
                source.Cancel();
            }
        }
    }

    public void CancelAll()
    {
        List<CancellationTokenSource> sources;
        lock (this.runningGate)
        {
            sources = [.. this.running.Values];
        }

        foreach (CancellationTokenSource source in sources)
        {
            source.Cancel();
        }
    }

    /// <summary>Removes finished jobs from the list.</summary>
    public void ClearFinished()
    {
        for (int i = this.Jobs.Count - 1; i >= 0; i--)
        {
            if (this.Jobs[i].IsFinished)
            {
                this.Jobs.RemoveAt(i);
            }
        }
    }

    public bool HasActiveJobs
    {
        get
        {
            lock (this.runningGate)
            {
                return this.running.Count > 0 || this.Jobs.Any(j => j.State == ConversionState.Queued);
            }
        }
    }

    /// <summary>
    /// Stops everything in flight.
    /// </summary>
    /// <remarks>
    /// The semaphore and the token source are deliberately not disposed. A job
    /// that is still unwinding releases the one and observes the other, and
    /// taking them away underneath it throws on a thread pool thread, which ends
    /// the process rather than tidying anything up. Neither holds an unmanaged
    /// handle here, so letting the collector have them costs nothing.
    /// </remarks>
    public void Dispose()
    {
        this.shutdown.Cancel();
        this.CancelAll();
    }

    private async Task RunAsync(ConversionJob job)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(this.shutdown.Token);

        lock (this.runningGate)
        {
            this.running[job] = source;
        }

        try
        {
            await this.gate.WaitAsync(source.Token).ConfigureAwait(false);

            try
            {
                await job.RunAsync(source.Token).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    this.gate.Release();
                }
                catch (ObjectDisposedException)
                {
                    // The queue went away while this job was finishing.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The job already recorded its cancelled state.
        }
        finally
        {
            bool allDone;
            lock (this.runningGate)
            {
                this.running.Remove(job);
                allDone = this.running.Count == 0;
            }

            source.Dispose();
            this.JobFinished?.Invoke(this, job);

            if (allDone)
            {
                this.AllFinished?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
