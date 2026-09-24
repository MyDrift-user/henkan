using System.Reflection;
using System.Runtime.Versioning;
using Henkan.Core.Tools;

namespace Henkan.Core.Backends;

/// <summary>A backend paired with one of its targets, which is what a preset points at.</summary>
/// <param name="Backend">The backend that will run the conversion.</param>
/// <param name="Target">The output format within that backend.</param>
public readonly record struct TargetDescriptor(IConversionBackend Backend, ConversionTarget Target)
{
    /// <summary>Stable identifier of the pair, stored in presets.</summary>
    public string Key => $"{this.Backend.Definition.Id}/{this.Target.Id}";

    public string Category => this.Target.Category;

    public bool IsAvailable => this.Backend.IsTargetAvailable(this.Target);

    /// <summary>Why this pairing cannot run, or null when it can.</summary>
    public string? UnavailableReason => this.Backend.TargetUnavailableReason(this.Target);
}

/// <summary>Something wrong with a definition file, surfaced in the UI rather than swallowed.</summary>
/// <param name="Source">File path, or the resource name of a built-in.</param>
/// <param name="Message">What went wrong.</param>
public readonly record struct BackendLoadProblem(string Source, string Message);

/// <summary>
/// Owns the set of backends the application knows about.
/// </summary>
/// <remarks>
/// Definitions come from two places: JSON embedded in the assembly for the
/// built-ins, and loose JSON files in the user's backends folder. A user file
/// whose id matches a built-in replaces it outright, so any shipped backend can
/// be retuned without patching the application.
/// </remarks>
public sealed class BackendRegistry
{
    private readonly ToolLocator locator;
    private readonly string? userDirectory;
    private List<IConversionBackend> backends = [];
    private List<BackendLoadProblem> problems = [];

    public BackendRegistry(ToolLocator locator, string? userDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(locator);

        this.locator = locator;
        this.userDirectory = userDirectory;
    }

    public IReadOnlyList<IConversionBackend> Backends => this.backends;

    public IReadOnlyList<BackendLoadProblem> Problems => this.problems;

    /// <summary>Reloads every definition and re-probes availability.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = new Dictionary<string, BackendDefinition>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<BackendLoadProblem>();

        foreach ((string source, BackendDefinition definition) in LoadBuiltInDefinitions(failures))
        {
            Accept(definition with { IsBuiltIn = true }, source, loaded, failures);
        }

        if (!string.IsNullOrWhiteSpace(this.userDirectory) && Directory.Exists(this.userDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(this.userDirectory, "*.json", SearchOption.TopDirectoryOnly).Order())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    BackendDefinition definition = await BackendSerializer.LoadAsync(file, cancellationToken).ConfigureAwait(false);
                    Accept(definition, file, loaded, failures);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new BackendLoadProblem(file, ex.Message));
                }
            }
        }

        var created = new List<IConversionBackend>(loaded.Count);

        foreach (BackendDefinition definition in loaded.Values.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            try
            {
                created.Add(this.CreateBackend(definition));
            }
            catch (Exception ex)
            {
                failures.Add(new BackendLoadProblem(definition.SourcePath ?? definition.Id, ex.Message));
            }
        }

        // Pipelines are probed last: what they can do depends on which of the
        // targets they chain together turned out to be available.
        await Task.WhenAll(created
            .Where(b => b.Definition.Kind != BackendKind.Pipeline)
            .Select(backend => backend.ProbeAsync(cancellationToken))).ConfigureAwait(false);

        this.backends = created;

        await Task.WhenAll(created
            .Where(b => b.Definition.Kind == BackendKind.Pipeline)
            .Select(backend => backend.ProbeAsync(cancellationToken))).ConfigureAwait(false);

        this.backends = created;
        this.problems = failures;
    }

    public IConversionBackend? Find(string backendId) =>
        this.backends.FirstOrDefault(b => b.Definition.Id.Equals(backendId, StringComparison.OrdinalIgnoreCase));

    /// <summary>Resolves a preset's <c>backendId/targetId</c> key.</summary>
    public TargetDescriptor? FindTarget(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string[] parts = key.Split('/', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        IConversionBackend? backend = this.Find(parts[0]);
        ConversionTarget? target = backend?.Definition.FindTarget(parts[1]);

        return backend is not null && target is not null ? new TargetDescriptor(backend, target) : null;
    }

    /// <summary>
    /// Every target that can run right now, optionally narrowed to those that
    /// accept a given input extension. Backends that failed to probe are left out,
    /// which is how a missing ffmpeg quietly removes the audio and video formats
    /// instead of offering conversions that would fail.
    /// </summary>
    public IEnumerable<TargetDescriptor> GetTargets(string? inputExtension = null, bool availableOnly = true)
    {
        string? extension = inputExtension?.TrimStart('.');

        foreach (IConversionBackend backend in this.backends)
        {
            if (availableOnly && !backend.Availability.IsAvailable)
            {
                continue;
            }

            foreach (ConversionTarget target in backend.Definition.Targets)
            {
                if (extension is not null && !target.Accepts(extension))
                {
                    continue;
                }

                if (availableOnly && !backend.IsTargetAvailable(target))
                {
                    continue;
                }

                yield return new TargetDescriptor(backend, target);
            }
        }
    }

    /// <summary>Reads the definitions embedded in this assembly.</summary>
    public static IEnumerable<(string Source, BackendDefinition Definition)> LoadBuiltInDefinitions(
        List<BackendLoadProblem>? failures = null)
    {
        Assembly assembly = typeof(BackendRegistry).Assembly;

        foreach (string name in assembly.GetManifestResourceNames()
                     .Where(n => n.Contains(".Definitions.", StringComparison.Ordinal)
                              && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                     .Order())
        {
            BackendDefinition? definition = null;

            try
            {
                using Stream? stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    continue;
                }

                using var reader = new StreamReader(stream);
                definition = BackendSerializer.Deserialize(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                failures?.Add(new BackendLoadProblem(name, ex.Message));
            }

            if (definition is not null)
            {
                yield return (name, definition);
            }
        }
    }

    private IConversionBackend CreateBackend(BackendDefinition definition) => definition.Kind switch
    {
        BackendKind.Process => new ProcessBackend(definition, this.locator),
        BackendKind.ImageMagick => new ImageMagickBackend(definition),
        BackendKind.Pipeline => new PipelineBackend(definition, this),
        BackendKind.Archive => new ArchiveBackend(definition, this.locator),
        BackendKind.Office => CreateOfficeBackend(definition),
        _ => throw new NotSupportedException($"Unknown backend kind \"{definition.Kind}\"."),
    };

    [SupportedOSPlatform("windows")]
    private static IConversionBackend CreateOfficeBackend(BackendDefinition definition) => new OfficeBackend(definition);

    private static void Accept(
        BackendDefinition definition,
        string source,
        Dictionary<string, BackendDefinition> loaded,
        List<BackendLoadProblem> failures)
    {
        IReadOnlyList<ValidationMessage> messages = BackendValidator.Validate(definition);

        foreach (ValidationMessage message in messages)
        {
            failures.Add(new BackendLoadProblem(source, message.ToString()));
        }

        if (!BackendValidator.IsUsable(messages))
        {
            return;
        }

        loaded[definition.Id] = definition with { SourcePath = definition.SourcePath ?? source };
    }
}
