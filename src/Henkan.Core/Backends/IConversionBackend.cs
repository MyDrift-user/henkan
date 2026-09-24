using Henkan.Core.Options;

namespace Henkan.Core.Backends;

/// <summary>Whether a backend can run, and what was found when it was probed.</summary>
/// <param name="IsAvailable">False hides every target of this backend from the UI.</param>
/// <param name="ResolvedPath">Executable that was found, null for in-process backends.</param>
/// <param name="Version">Version string parsed from the probe, when the definition asked for one.</param>
/// <param name="Reason">Why the backend is unavailable, shown to the user.</param>
public readonly record struct BackendAvailability(
    bool IsAvailable,
    string? ResolvedPath,
    string? Version,
    string? Reason)
{
    public static BackendAvailability Available(string? resolvedPath = null, string? version = null) =>
        new(true, resolvedPath, version, null);

    public static BackendAvailability Unavailable(string reason) =>
        new(false, null, null, reason);

    /// <summary>Before the first probe completes nothing is known, and nothing is offered.</summary>
    public static BackendAvailability Unknown { get; } = new(false, null, null, "Not probed yet.");
}

/// <summary>Everything a backend needs to run one file through one target.</summary>
public sealed class ConversionContext
{
    public required BackendDefinition Definition { get; init; }

    public required ConversionTarget Target { get; init; }

    public required string InputPath { get; init; }

    /// <summary>
    /// Everything being converted. One entry for an ordinary conversion, and the
    /// whole selection for a target that combines its inputs.
    /// </summary>
    public IReadOnlyList<string> Inputs { get; init; } = [];

    public required string OutputPath { get; init; }

    /// <summary>Effective option values, defaults already filled in.</summary>
    public required OptionValueSet Options { get; init; }

    /// <summary>A per-job scratch directory that the queue creates and removes.</summary>
    public required string TemporaryDirectory { get; init; }

    /// <summary>Receives values from 0 to 1, or is never called when progress is unknowable.</summary>
    public IProgress<double>? Progress { get; init; }

    /// <summary>Receives command lines and tool output for the job log.</summary>
    public Action<string>? Log { get; init; }

    public void Report(double value) => this.Progress?.Report(Math.Clamp(value, 0d, 1d));

    public void Write(string message) => this.Log?.Invoke(message);
}

/// <summary>
/// Executes conversions for one <see cref="BackendDefinition"/>. Process backends
/// all share a single implementation driven by the definition, so a user-authored
/// backend runs through exactly the same code as the built-in ones.
/// </summary>
public interface IConversionBackend
{
    BackendDefinition Definition { get; }

    /// <summary>Result of the last <see cref="ProbeAsync"/>, or <see cref="BackendAvailability.Unknown"/>.</summary>
    BackendAvailability Availability { get; }

    /// <summary>Checks whether the backend can run and caches the answer.</summary>
    Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Converts a single file. Throws <see cref="ConversionFailedException"/> on failure.</summary>
    Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether one particular target can run. For most backends this is just the
    /// backend's own availability, but a pipeline target depends on the targets it
    /// chains together, and those can be present or missing one at a time.
    /// </summary>
    bool IsTargetAvailable(ConversionTarget target) => this.Availability.IsAvailable;

    /// <summary>Why a target cannot run, or null when it can.</summary>
    string? TargetUnavailableReason(ConversionTarget target) =>
        this.IsTargetAvailable(target) ? null : this.Availability.Reason;
}

/// <summary>Raised when a conversion could not be completed.</summary>
public sealed class ConversionFailedException : Exception
{
    public ConversionFailedException(string message)
        : base(message)
    {
    }

    public ConversionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
