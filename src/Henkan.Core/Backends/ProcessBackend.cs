using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Henkan.Core.Templating;
using Henkan.Core.Tools;

namespace Henkan.Core.Backends;

/// <summary>
/// Runs an external executable described entirely by its
/// <see cref="BackendDefinition"/>. Every process backend, built-in or written by
/// the user, goes through this one implementation, so a custom ffmpeg-alike or a
/// completely different tool gets the same progress reporting, logging,
/// cancellation and timeout handling as the shipped definitions.
/// </summary>
public sealed class ProcessBackend : IConversionBackend
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly ToolLocator locator;
    private readonly Regex? durationPattern;
    private readonly Regex? positionPattern;
    private readonly Regex? percentPattern;
    private readonly Regex? versionPattern;

    public ProcessBackend(BackendDefinition definition, ToolLocator locator)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(locator);

        if (definition.Executable is null)
        {
            throw new ArgumentException(
                $"Backend \"{definition.Id}\" is a process backend but declares no executable.",
                nameof(definition));
        }

        this.Definition = definition;
        this.locator = locator;

        this.durationPattern = CompileOrNull(definition.Progress?.DurationPattern);
        this.positionPattern = CompileOrNull(definition.Progress?.PositionPattern);
        this.percentPattern = CompileOrNull(definition.Progress?.PercentPattern);
        this.versionPattern = CompileOrNull(definition.Executable.VersionPattern);
    }

    public BackendDefinition Definition { get; }

    public BackendAvailability Availability { get; private set; } = BackendAvailability.Unknown;

    public async Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        ExecutableDefinition executable = this.Definition.Executable!;

        ToolResolution resolution = this.locator.Resolve(
            this.Definition.Id,
            executable.FileName,
            executable.BundledPath,
            executable.SearchPaths);

        if (!resolution.Found)
        {
            return this.Availability = BackendAvailability.Unavailable(
                resolution.Reason ?? $"\"{executable.FileName}\" was not found.");
        }

        if (executable.ProbeArguments.Count == 0)
        {
            return this.Availability = BackendAvailability.Available(resolution.Path);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            var startInfo = new ProcessStartInfo(resolution.Path!)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in executable.ProbeArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new ConversionFailedException($"Could not start \"{resolution.Path}\".");

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> standardError = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false)
                          + await standardError.ConfigureAwait(false);

            string? version = null;
            if (this.versionPattern is not null)
            {
                Match match = this.versionPattern.Match(output);
                if (match.Success && match.Groups.Count > 1)
                {
                    version = match.Groups[1].Value;
                }
            }

            return this.Availability = BackendAvailability.Available(resolution.Path, version);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return this.Availability = BackendAvailability.Unavailable(
                $"\"{resolution.Path}\" did not respond within {ProbeTimeout.TotalSeconds:0} seconds.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return this.Availability = BackendAvailability.Unavailable(
                $"\"{resolution.Path}\" could not be started: {ex.Message}");
        }
    }

    public async Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!this.Availability.IsAvailable)
        {
            await this.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!this.Availability.IsAvailable)
        {
            throw new ConversionFailedException(
                this.Availability.Reason ?? $"The backend \"{this.Definition.Name}\" is not available.");
        }

        string executablePath = this.Availability.ResolvedPath!;

        VariableContext variables = VariableContext.Create(
            context.InputPath,
            context.OutputPath,
            context.TemporaryDirectory,
            context.Options,
            Path.GetDirectoryName(executablePath))
            .WithInputs(context.Inputs);

        IReadOnlyList<ArgumentFragment> template = this.Definition.GetArgumentTemplate(context.Target);
        IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(template, variables);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = context.TemporaryDirectory,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        context.Write(TemplateRenderer.RenderCommandLine(executablePath, template, variables));

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPath)!);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (this.Definition.TimeoutSeconds > 0)
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(this.Definition.TimeoutSeconds));
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var tracker = new ProgressTracker(this.durationPattern, this.positionPattern, this.percentPattern);
        bool progressOnError = (this.Definition.Progress?.Stream ?? ProgressStream.StandardError) == ProgressStream.StandardError;
        var tail = new TailBuffer(capacity: 40);

        void HandleLine(string? line, bool isErrorStream)
        {
            if (line is null)
            {
                return;
            }

            tail.Add(line);
            context.Write(line);

            if (isErrorStream == progressOnError && tracker.TryAdvance(line, out double fraction))
            {
                context.Report(fraction);
            }
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data, isErrorStream: false);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data, isErrorStream: true);

        if (!process.Start())
        {
            throw new ConversionFailedException($"Could not start \"{executablePath}\".");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Some tools wait on stdin when they think they are interactive; closing
        // it makes them fail fast instead of hanging the queue forever.
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The process may have exited already, which is fine.
        }

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new ConversionFailedException(
                $"\"{Path.GetFileName(executablePath)}\" exceeded the {this.Definition.TimeoutSeconds} second time limit and was stopped.");
        }

        if (!this.Definition.SuccessExitCodes.Contains(process.ExitCode))
        {
            throw new ConversionFailedException(
                $"\"{Path.GetFileName(executablePath)}\" exited with code {process.ExitCode}.{Environment.NewLine}{tail}");
        }

        if (this.Definition.OutputHandling == OutputHandling.TemporaryDirectory)
        {
            CollectFromTemporaryDirectory(context, executablePath);
        }

        if (!File.Exists(context.OutputPath))
        {
            throw new ConversionFailedException(
                $"\"{Path.GetFileName(executablePath)}\" reported success but produced no file at \"{context.OutputPath}\".");
        }

        context.Report(1d);
    }

    /// <summary>
    /// Moves the tool's self-named output into place. The file named after the
    /// input is preferred; failing that, a lone new file in the scratch directory
    /// is accepted, since some tools normalise names in ways that are hard to predict.
    /// </summary>
    private static void CollectFromTemporaryDirectory(ConversionContext context, string executablePath)
    {
        string expected = Path.Combine(
            context.TemporaryDirectory,
            Path.GetFileNameWithoutExtension(context.InputPath) + "." + context.Target.OutputExtension.TrimStart('.'));

        string? produced = File.Exists(expected) ? expected : null;

        if (produced is null)
        {
            string[] candidates = Directory.GetFiles(context.TemporaryDirectory);
            if (candidates.Length == 1)
            {
                produced = candidates[0];
            }
            else if (candidates.Length > 1)
            {
                throw new ConversionFailedException(
                    $"\"{Path.GetFileName(executablePath)}\" left {candidates.Length} files in the scratch directory and none is named \"{Path.GetFileName(expected)}\".");
            }
        }

        if (produced is null)
        {
            return;
        }

        File.Move(produced, context.OutputPath, overwrite: true);
        context.Write($"Moved \"{Path.GetFileName(produced)}\" to \"{context.OutputPath}\".");
    }

    private static Regex? CompileOrNull(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern)
            ? null
            : new Regex(pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            // The process ended between the check and the kill.
        }
    }

    /// <summary>
    /// Turns tool chatter into a fraction. A percentage pattern wins when the
    /// definition supplies one; otherwise a duration is learned once and each
    /// reported position is divided by it.
    /// </summary>
    private sealed class ProgressTracker(Regex? duration, Regex? position, Regex? percent)
    {
        private double totalSeconds;

        public bool TryAdvance(string line, out double fraction)
        {
            fraction = 0d;

            if (percent is not null)
            {
                Match match = percent.Match(line);
                if (match.Success && match.Groups.Count > 1
                    && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    fraction = value / 100d;
                    return true;
                }
            }

            if (duration is not null && this.totalSeconds <= 0d)
            {
                Match match = duration.Match(line);
                if (match.Success && TryReadClock(match, out double seconds) && seconds > 0d)
                {
                    this.totalSeconds = seconds;
                }
            }

            if (position is not null && this.totalSeconds > 0d)
            {
                Match match = position.Match(line);
                if (match.Success && TryReadClock(match, out double seconds))
                {
                    fraction = seconds / this.totalSeconds;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Reads hours, minutes and seconds from capture groups 1 to 3.</summary>
        private static bool TryReadClock(Match match, out double seconds)
        {
            seconds = 0d;

            if (match.Groups.Count < 4)
            {
                return false;
            }

            if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double hours)
                || !double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
                || !double.TryParse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rest))
            {
                return false;
            }

            seconds = (hours * 3600d) + (minutes * 60d) + rest;
            return true;
        }
    }

    /// <summary>Keeps the last few output lines so a failure message can quote them.</summary>
    private sealed class TailBuffer(int capacity)
    {
        private readonly Queue<string> lines = new();
        private readonly Lock gate = new();

        public void Add(string line)
        {
            lock (this.gate)
            {
                this.lines.Enqueue(line);
                while (this.lines.Count > capacity)
                {
                    this.lines.Dequeue();
                }
            }
        }

        public override string ToString()
        {
            lock (this.gate)
            {
                return string.Join(Environment.NewLine, this.lines);
            }
        }
    }
}
