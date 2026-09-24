using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Backends;

/// <summary>
/// Runs several existing targets in sequence, each one reading what the last
/// produced.
/// </summary>
/// <remarks>
/// <para>
/// Nothing turns a spreadsheet into a PNG in one move, which is why the obvious
/// conversions were the ones that did not work. Going through an intermediate
/// format does: the spreadsheet becomes a PDF, and the PDF becomes a PNG. Both
/// legs already existed as targets; only the joining up was missing.
/// </para>
/// <para>
/// A step may name several targets. The first available one wins, so a single
/// definition covers a machine with Microsoft Office and a machine with
/// LibreOffice without either being mentioned by the user.
/// </para>
/// </remarks>
public sealed class PipelineBackend : IConversionBackend
{
    private readonly BackendRegistry registry;

    public PipelineBackend(BackendDefinition definition, BackendRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(registry);

        this.Definition = definition;
        this.registry = registry;
    }

    public BackendDefinition Definition { get; }

    public BackendAvailability Availability { get; private set; } = BackendAvailability.Unknown;

    public Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        int runnable = this.Definition.Targets.Count(this.IsTargetAvailable);

        this.Availability = runnable > 0
            ? BackendAvailability.Available(null, $"{runnable} of {this.Definition.Targets.Count} routes")
            : BackendAvailability.Unavailable(
                "None of the multi-step conversions can run, because the backends they pass through are missing.");

        return Task.FromResult(this.Availability);
    }

    public bool IsTargetAvailable(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.Steps.Count > 0 && target.Steps.All(step => this.Resolve(step) is not null);
    }

    public string? TargetUnavailableReason(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target.Steps.Count == 0)
        {
            return "This conversion declares no steps.";
        }

        foreach (PipelineStep step in target.Steps)
        {
            if (this.Resolve(step) is null)
            {
                return $"Needs one of {string.Join(" or ", step.Targets)}, and none of them is available.";
            }
        }

        return null;
    }

    public async Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IReadOnlyList<PipelineStep> steps = context.Target.Steps;

        if (steps.Count == 0)
        {
            throw new ConversionFailedException($"Target \"{context.Target.Id}\" declares no steps.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPath)!);

        string current = context.InputPath;
        var intermediates = new List<string>();

        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                PipelineStep step = steps[i];
                TargetDescriptor leg = this.Resolve(step)
                    ?? throw new ConversionFailedException(
                        $"Step {i + 1} needs one of {string.Join(" or ", step.Targets)}, and none of them is available.");

                bool last = i == steps.Count - 1;

                // Everything but the final leg writes into the job's scratch
                // directory, which the queue removes when the job is done.
                string destination = last
                    ? context.OutputPath
                    : Path.Combine(
                        context.TemporaryDirectory,
                        $"step{i + 1}-{Path.GetFileNameWithoutExtension(context.InputPath)}.{leg.Target.OutputExtension}");

                if (!last)
                {
                    intermediates.Add(destination);
                }

                context.Write($"Step {i + 1} of {steps.Count}: {leg.Key} -> \"{Path.GetFileName(destination)}\"");

                await this.RunStepAsync(context, step, leg, current, destination, cancellationToken).ConfigureAwait(false);

                if (!File.Exists(destination))
                {
                    throw new ConversionFailedException(
                        $"Step {i + 1} ({leg.Key}) produced no file.");
                }

                current = destination;
                context.Report((i + 1d) / steps.Count);
            }
        }
        finally
        {
            foreach (string intermediate in intermediates)
            {
                try
                {
                    File.Delete(intermediate);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The scratch directory goes away with the job either way.
                }
            }
        }
    }

    private async Task RunStepAsync(
        ConversionContext context,
        PipelineStep step,
        TargetDescriptor leg,
        string input,
        string output,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<OptionDescriptor> schema = leg.Backend.Definition.GetOptionSchema(leg.Target);
        OptionValueSet values = OptionValueSet.FromSchema(schema, RenderOptions(step, context.Options));

        var inner = new ConversionContext
        {
            Definition = leg.Backend.Definition,
            Target = leg.Target,
            InputPath = input,
            OutputPath = output,
            Options = values,
            TemporaryDirectory = context.TemporaryDirectory,

            // Progress is reported per completed step instead; a leg that
            // reported its own would keep resetting the bar to zero.
            Log = context.Log,
        };

        await leg.Backend.ExecuteAsync(inner, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Fills a step's option values in from the pipeline target's own options, so
    /// <c>"Resolution": "{Resolution}"</c> passes the value the user chose down to
    /// the leg that acts on it.
    /// </summary>
    private static Dictionary<string, string> RenderOptions(PipelineStep step, OptionValueSet outer)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (step.Options is null)
        {
            return result;
        }

        foreach ((string name, string value) in step.Options)
        {
            try
            {
                result[name] = TemplateRenderer.RenderText(value, key => outer.GetString(key));
            }
            catch (TemplateException)
            {
                // An unresolved reference is better passed through untouched than
                // allowed to take the whole conversion down.
                result[name] = value;
            }
        }

        return result;
    }

    private TargetDescriptor? Resolve(PipelineStep step)
    {
        foreach (string key in step.Targets)
        {
            if (this.registry.FindTarget(key) is { } candidate && candidate.IsAvailable)
            {
                return candidate;
            }
        }

        return null;
    }
}
