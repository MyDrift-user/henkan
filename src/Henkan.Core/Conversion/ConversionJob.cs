using System.ComponentModel;
using System.Runtime.CompilerServices;
using Henkan.Core.Backends;
using Henkan.Core.Options;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Templating;

namespace Henkan.Core.Conversion;

public enum ConversionState
{
    Queued,
    Running,
    Succeeded,
    Skipped,
    Failed,
    Cancelled,
}

/// <summary>
/// One input file going through one target. Observable so the UI can bind a row
/// to it directly; the queue drives it and nothing else mutates it.
/// </summary>
public sealed class ConversionJob : INotifyPropertyChanged
{
    private readonly List<string> log = [];
    private readonly Lock logGate = new();
    private readonly SynchronizationContext? notifications;
    private readonly IOutputReservation? reservation;
    private ConversionState state = ConversionState.Queued;
    private double progress;
    private string? outputPath;
    private string? message;
    private DateTimeOffset? started;
    private DateTimeOffset? finished;

    /// <param name="notifications">
    /// Where <see cref="PropertyChanged"/> is raised. A job runs on the thread
    /// pool, and a user interface bound to it almost certainly cannot be touched
    /// from there, so the caller passes the context its bindings live on.
    /// </param>
    public ConversionJob(
        string inputPath,
        TargetDescriptor target,
        Preset preset,
        OptionValueSet options,
        SynchronizationContext? notifications = null,
        IOutputReservation? reservation = null)
        : this([inputPath], target, preset, options, notifications, reservation)
    {
    }

    /// <param name="inputs">
    /// Everything this one job converts. More than one only for a target that
    /// combines its inputs, such as archiving a selection into a single zip.
    /// </param>
    public ConversionJob(
        IReadOnlyList<string> inputs,
        TargetDescriptor target,
        Preset preset,
        OptionValueSet options,
        SynchronizationContext? notifications = null,
        IOutputReservation? reservation = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(options);

        if (inputs.Count == 0)
        {
            throw new ArgumentException("A job needs something to convert.", nameof(inputs));
        }

        this.Inputs = [.. inputs.Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))];
        this.InputPath = this.Inputs[0];
        this.Target = target;
        this.Preset = preset;
        this.Options = options;
        this.notifications = notifications;
        this.reservation = reservation;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InputPath { get; }

    /// <summary>Everything this job converts, in the order it was given.</summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>What to call this job on screen.</summary>
    public string InputFileName => this.Inputs.Count == 1
        ? Path.GetFileName(this.InputPath)
        : $"{this.Inputs.Count} items";

    /// <summary>True when a whole folder was handed in rather than one file.</summary>
    public bool InputIsDirectory => Directory.Exists(this.InputPath);

    public TargetDescriptor Target { get; }

    public Preset Preset { get; }

    /// <summary>Effective values, defaults already applied, as the backend will see them.</summary>
    public OptionValueSet Options { get; }

    public ConversionState State
    {
        get => this.state;
        private set => this.SetField(ref this.state, value);
    }

    /// <summary>0 to 1. Stays at 0 for backends that cannot report progress.</summary>
    public double Progress
    {
        get => this.progress;
        private set => this.SetField(ref this.progress, value);
    }

    public string? OutputPath
    {
        get => this.outputPath;
        private set => this.SetField(ref this.outputPath, value);
    }

    /// <summary>Failure reason, skip reason, or null.</summary>
    public string? Message
    {
        get => this.message;
        private set => this.SetField(ref this.message, value);
    }

    public DateTimeOffset? Started
    {
        get => this.started;
        private set => this.SetField(ref this.started, value);
    }

    public DateTimeOffset? Finished
    {
        get => this.finished;
        private set => this.SetField(ref this.finished, value);
    }

    public bool IsFinished => this.State is ConversionState.Succeeded or ConversionState.Skipped
        or ConversionState.Failed or ConversionState.Cancelled;

    public TimeSpan? Elapsed => this.Started is { } begin
        ? (this.Finished ?? DateTimeOffset.UtcNow) - begin
        : null;

    /// <summary>Everything the backend wrote, plus the command line it ran.</summary>
    public IReadOnlyList<string> Log
    {
        get
        {
            lock (this.logGate)
            {
                return [.. this.log];
            }
        }
    }

    /// <summary>Marks a job as failed before it ever runs, with the reason why.</summary>
    internal void Reject(string reason)
    {
        this.Message = reason;
        this.State = ConversionState.Failed;
        this.Finished = DateTimeOffset.UtcNow;
        this.OnPropertyChanged(nameof(this.IsFinished));
    }

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        if (this.State != ConversionState.Queued)
        {
            return;
        }

        this.Started = DateTimeOffset.UtcNow;
        this.State = ConversionState.Running;

        string scratch = Path.Combine(HenkanPaths.TemporaryDirectory, Guid.NewGuid().ToString("n"));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? destination = OutputPathResolver.Resolve(
                this.Preset.OutputPathTemplate,
                this.NamingPath,
                this.Target.Target.OutputExtension,
                this.Preset.ConflictPolicy,
                this.Options,
                this.Target.Target.OutputIsDirectory,
                this.reservation is { } claims ? claims.IsReserved : null);

            if (destination is null)
            {
                this.Message = this.Target.Target.OutputIsDirectory
                    ? "The output folder already exists."
                    : "The output file already exists.";
                this.State = ConversionState.Skipped;
                return;
            }

            this.OutputPath = destination;
            this.reservation?.Reserve(destination);
            Directory.CreateDirectory(scratch);

            var context = new ConversionContext
            {
                Definition = this.Target.Backend.Definition,
                Target = this.Target.Target,
                InputPath = this.InputPath,
                Inputs = this.Inputs,
                OutputPath = destination,
                Options = this.Options,
                TemporaryDirectory = scratch,
                Progress = new Progress<double>(value => this.Progress = value),
                Log = this.Append,
            };

            await this.Target.Backend.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

            this.ApplyInputAction();

            this.Progress = 1d;
            this.State = ConversionState.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            this.DeletePartialOutput();
            this.Message = "Cancelled.";
            this.State = ConversionState.Cancelled;
        }
        catch (ConversionSkippedException ex)
        {
            this.Message = ex.Message;
            this.State = ConversionState.Skipped;
        }
        catch (Exception ex)
        {
            this.Append(ex.ToString());

            // Only a conversion that did not finish leaves a half-written file
            // worth removing. Throwing away a result that was already complete,
            // because something after it went wrong, is how a working conversion
            // turns into a silent one.
            if (this.State != ConversionState.Succeeded)
            {
                this.DeletePartialOutput();
                this.Message = ex.Message;
                this.State = ConversionState.Failed;
            }
        }
        finally
        {
            if (this.OutputPath is not null)
            {
                this.reservation?.Release(this.OutputPath);
            }

            this.Finished = DateTimeOffset.UtcNow;
            this.OnPropertyChanged(nameof(this.IsFinished));
            this.OnPropertyChanged(nameof(this.Elapsed));

            try
            {
                if (Directory.Exists(scratch))
                {
                    Directory.Delete(scratch, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Startup cleanup will get it next time.
            }
        }
    }

    /// <summary>
    /// The path the output name is derived from.
    /// </summary>
    /// <remarks>
    /// One file names the result after itself. A selection has no single name to
    /// take, so it is named after the folder they came from, which is what every
    /// other archiver does and what the user would have typed anyway.
    /// </remarks>
    private string NamingPath
    {
        get
        {
            if (this.Inputs.Count == 1)
            {
                return this.InputPath;
            }

            string parent = Path.GetDirectoryName(this.InputPath) ?? string.Empty;
            string leaf = Path.GetFileName(parent);

            return Path.Combine(parent, (leaf.Length > 0 ? leaf : "archive") + ".out");
        }
    }

    private void ApplyInputAction()
    {
        switch (this.Preset.AfterConversion)
        {
            case InputAction.Keep:
                return;

            case InputAction.Delete:
                File.Delete(this.InputPath);
                this.Append("Deleted the original.");
                return;

            case InputAction.Recycle:
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    this.InputPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                this.Append("Moved the original to the recycle bin.");
                return;

            case InputAction.Archive:
                VariableContext context = VariableContext.Create(
                    this.InputPath,
                    this.OutputPath!,
                    HenkanPaths.TemporaryDirectory,
                    this.Options);

                string archive = TemplateRenderer.RenderText(this.Preset.ArchivePathTemplate, context.Lookup);
                if (!Path.IsPathRooted(archive))
                {
                    archive = Path.Combine(Path.GetDirectoryName(this.InputPath) ?? string.Empty, archive);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
                File.Move(this.InputPath, File.Exists(archive) ? OutputPathResolver.NextFreeName(archive) : archive);
                this.Append($"Moved the original to \"{archive}\".");
                return;
        }
    }

    private void DeletePartialOutput()
    {
        if (this.OutputPath is null)
        {
            return;
        }

        try
        {
            if (this.Target.Target.OutputIsDirectory)
            {
                if (Directory.Exists(this.OutputPath))
                {
                    Directory.Delete(this.OutputPath, recursive: true);
                }
            }
            else if (File.Exists(this.OutputPath))
            {
                File.Delete(this.OutputPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.Append($"Could not remove the partial output \"{this.OutputPath}\".");
        }
    }

    private void Append(string line)
    {
        lock (this.logGate)
        {
            this.log.Add(line);
        }
    }

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        this.OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> on the context the listener asked for,
    /// and never lets a listener's failure reach the conversion.
    /// </summary>
    /// <remarks>
    /// Both halves of this matter. A XAML binding updated from a worker thread
    /// throws RPC_E_WRONG_THREAD, and that exception used to surface inside the
    /// job, where it was caught as a failed conversion and the finished output
    /// was deleted. The conversion had already worked; only the telling of it had
    /// not.
    /// </remarks>
    private void OnPropertyChanged(string? propertyName)
    {
        if (this.PropertyChanged is not { } handlers)
        {
            return;
        }

        var arguments = new PropertyChangedEventArgs(propertyName);

        if (this.notifications is not null && this.notifications != SynchronizationContext.Current)
        {
            this.notifications.Post(_ => Raise(handlers, this, arguments, this.Append), null);
            return;
        }

        Raise(handlers, this, arguments, this.Append);
    }

    private static void Raise(
        PropertyChangedEventHandler handlers,
        object sender,
        PropertyChangedEventArgs arguments,
        Action<string> report)
    {
        try
        {
            handlers(sender, arguments);
        }
        catch (Exception ex)
        {
            report($"A listener failed while being told about {arguments.PropertyName}: {ex.Message}");
        }
    }
}
