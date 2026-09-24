using System.Collections.ObjectModel;

using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Backends.Editing;

/// <summary>One leg of a multi-step conversion, as the editor sees it.</summary>
public sealed class StepDraft : Draft
{
    private string targets = string.Empty;
    private string options = string.Empty;

    /// <summary>Conversions that can perform this step, best first, comma separated.</summary>
    public string Targets
    {
        get => this.targets;
        set => this.Set(ref this.targets, value);
    }

    /// <summary>Values handed to the step, one <c>Name=value</c> per line.</summary>
    public string Options
    {
        get => this.options;
        set => this.Set(ref this.options, value);
    }

    public static StepDraft From(PipelineStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return new StepDraft
        {
            Targets = JoinList(step.Targets),
            Options = string.Join(Environment.NewLine, (step.Options ?? new Dictionary<string, string>()).Select(o => $"{o.Key}={o.Value}")),
        };
    }

    public PipelineStep ToStep()
    {
        Dictionary<string, string> values = TargetDraft.ParsePairs(this.Options);
        return new PipelineStep(SplitList(this.Targets), values.Count == 0 ? null : values);
    }
}

/// <summary>One output format, as the editor sees it.</summary>
public sealed class TargetDraft : Draft
{
    private string id = string.Empty;
    private string label = string.Empty;
    private string? description;
    private string outputExtension = string.Empty;
    private string category = "Other";
    private string inputExtensions = "*";
    private string? operation;
    private bool acceptsFolders;
    private bool combinesInputs;
    private bool outputIsDirectory;
    private string settings = string.Empty;

    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    public string Label
    {
        get => this.label;
        set => this.Set(ref this.label, value);
    }

    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    public string OutputExtension
    {
        get => this.outputExtension;
        set => this.Set(ref this.outputExtension, value);
    }

    public string Category
    {
        get => this.category;
        set => this.Set(ref this.category, value);
    }

    /// <summary>
    /// Comma separated, as typed. Family names such as <c>$audio</c> belong here
    /// too, which is why it stays text rather than becoming a picker.
    /// </summary>
    public string InputExtensions
    {
        get => this.inputExtensions;
        set => this.Set(ref this.inputExtensions, value);
    }

    public string? Operation
    {
        get => this.operation;
        set => this.Set(ref this.operation, value);
    }

    public bool AcceptsFolders
    {
        get => this.acceptsFolders;
        set => this.Set(ref this.acceptsFolders, value);
    }

    public bool CombinesInputs
    {
        get => this.combinesInputs;
        set => this.Set(ref this.combinesInputs, value);
    }

    public bool OutputIsDirectory
    {
        get => this.outputIsDirectory;
        set => this.Set(ref this.outputIsDirectory, value);
    }

    /// <summary>Backend specific values, one <c>name=value</c> per line.</summary>
    public string Settings
    {
        get => this.settings;
        set => this.Set(ref this.settings, value);
    }

    public ObservableCollection<OptionDraft> Options { get; } = [];

    public ObservableCollection<FragmentDraft> Arguments { get; } = [];

    /// <summary>The legs, for a target of a multi-step backend.</summary>
    public ObservableCollection<StepDraft> Steps { get; } = [];

    /// <summary>What the list shows for this target.</summary>
    public string Summary =>
        $"{(string.IsNullOrWhiteSpace(this.OutputExtension) ? "folder" : "." + this.OutputExtension)}  ·  {this.Options.Count} options";

    public static TargetDraft From(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var draft = new TargetDraft
        {
            Id = target.Id,
            Label = target.Label,
            Description = target.Description,
            OutputExtension = target.OutputExtension,
            Category = target.Category,
            InputExtensions = JoinList(target.InputExtensions),
            Operation = target.Operation,
            AcceptsFolders = target.AcceptsFolders,
            CombinesInputs = target.CombinesInputs,
            OutputIsDirectory = target.OutputIsDirectory,
            Settings = string.Join(Environment.NewLine, target.Settings.Select(s => $"{s.Key}={s.Value}")),
        };

        foreach (OptionDescriptor option in target.Options)
        {
            draft.Options.Add(OptionDraft.From(option));
        }

        foreach (ArgumentFragment fragment in target.Arguments)
        {
            draft.Arguments.Add(FragmentDraft.From(fragment));
        }

        foreach (PipelineStep step in target.Steps)
        {
            draft.Steps.Add(StepDraft.From(step));
        }

        return draft;
    }

    public ConversionTarget ToTarget() => new()
    {
        Id = this.Id.Trim(),
        Label = string.IsNullOrWhiteSpace(this.Label) ? this.Id.Trim() : this.Label.Trim(),
        Description = Blank(this.Description),
        OutputExtension = this.OutputExtension.Trim().TrimStart('.'),
        Category = string.IsNullOrWhiteSpace(this.Category) ? "Other" : this.Category.Trim(),
        InputExtensions = SplitList(this.InputExtensions),
        Operation = Blank(this.Operation),
        AcceptsFolders = this.AcceptsFolders,
        CombinesInputs = this.CombinesInputs,
        OutputIsDirectory = this.OutputIsDirectory,
        Settings = ParsePairs(this.Settings),
        Options = [.. this.Options.Where(o => !string.IsNullOrWhiteSpace(o.Id)).Select(o => o.ToDescriptor())],
        Arguments = [.. this.Arguments.Where(a => !string.IsNullOrWhiteSpace(a.Value)).Select(a => a.ToFragment())],
        Steps = [.. this.Steps.Where(st => !string.IsNullOrWhiteSpace(st.Targets)).Select(st => st.ToStep())],
    };

    /// <summary>Reads <c>name=value</c> lines, ignoring anything without an equals sign.</summary>
    internal static Dictionary<string, string> ParsePairs(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string line in text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = line.IndexOf('=', StringComparison.Ordinal);

            if (equals > 0)
            {
                result[line[..equals].Trim()] = line[(equals + 1)..].Trim();
            }
        }

        return result;
    }
}

/// <summary>A whole backend, as the editor sees it.</summary>
public sealed class BackendDraft : Draft
{
    private string id = string.Empty;
    private string name = string.Empty;
    private string? description;
    private BackendKind kind = BackendKind.Process;
    private int timeoutSeconds;
    private string fileName = string.Empty;
    private string? bundledPath;
    private string searchPaths = string.Empty;
    private string probeArguments = string.Empty;
    private string? versionPattern;
    private string? homepageUrl;
    private string successExitCodes = "0";
    private string progressStream = nameof(Henkan.Core.Backends.ProgressStream.StandardError);
    private string? durationPattern;
    private string? positionPattern;
    private string? percentPattern;
    private bool usesTemporaryDirectory;

    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    public string Name
    {
        get => this.name;
        set => this.Set(ref this.name, value);
    }

    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    public BackendKind Kind
    {
        get => this.kind;
        set
        {
            if (this.Set(ref this.kind, value))
            {
                this.Notify(nameof(this.KindName));
                this.Notify(nameof(this.NeedsExecutable));
                this.Notify(nameof(this.IsProcess));
                this.Notify(nameof(this.IsPipeline));
                this.Notify(nameof(this.UsesOperation));
            }
        }
    }

    public int TimeoutSeconds
    {
        get => this.timeoutSeconds;
        set => this.Set(ref this.timeoutSeconds, value);
    }

    /// <summary>
    /// The kind by name, so a combo box of strings can drive it. An unknown name
    /// leaves the kind alone rather than throwing at a half-typed value.
    /// </summary>
    public string KindName
    {
        get => this.Kind.ToString();
        set
        {
            if (Enum.TryParse(value, ignoreCase: true, out BackendKind parsed))
            {
                this.Kind = parsed;
            }
        }
    }

    /// <summary>True for the kinds that run an executable and therefore need one.</summary>
    public bool NeedsExecutable => this.Kind is BackendKind.Process or BackendKind.Archive;

    /// <summary>True when conversions are built from command line fragments.</summary>
    public bool IsProcess => this.Kind == BackendKind.Process;

    /// <summary>True when conversions are chains of other conversions.</summary>
    public bool IsPipeline => this.Kind == BackendKind.Pipeline;

    /// <summary>
    /// True for the kinds whose behaviour is code, steered by an operation name
    /// and a few settings rather than by a command line.
    /// </summary>
    public bool UsesOperation => this.Kind is BackendKind.ImageMagick or BackendKind.Office or BackendKind.Archive;

    public string FileName
    {
        get => this.fileName;
        set => this.Set(ref this.fileName, value);
    }

    public string? BundledPath
    {
        get => this.bundledPath;
        set => this.Set(ref this.bundledPath, value);
    }

    /// <summary>One path per line. Environment variables are expanded when used.</summary>
    public string SearchPaths
    {
        get => this.searchPaths;
        set => this.Set(ref this.searchPaths, value);
    }

    public string ProbeArguments
    {
        get => this.probeArguments;
        set => this.Set(ref this.probeArguments, value);
    }

    public string? VersionPattern
    {
        get => this.versionPattern;
        set => this.Set(ref this.versionPattern, value);
    }

    public string? HomepageUrl
    {
        get => this.homepageUrl;
        set => this.Set(ref this.homepageUrl, value);
    }

    public string SuccessExitCodes
    {
        get => this.successExitCodes;
        set => this.Set(ref this.successExitCodes, value);
    }

    /// <summary>Which output of the tool carries its progress: StandardError or StandardOutput.</summary>
    public string ProgressStream
    {
        get => this.progressStream;
        set => this.Set(ref this.progressStream, value);
    }

    /// <summary>Matched once for the total length; hours, minutes and seconds in groups 1 to 3.</summary>
    public string? DurationPattern
    {
        get => this.durationPattern;
        set => this.Set(ref this.durationPattern, value);
    }

    /// <summary>Matched repeatedly for the current position, laid out like the duration.</summary>
    public string? PositionPattern
    {
        get => this.positionPattern;
        set => this.Set(ref this.positionPattern, value);
    }

    /// <summary>Matched repeatedly for a percentage in group 1.</summary>
    public string? PercentPattern
    {
        get => this.percentPattern;
        set => this.Set(ref this.percentPattern, value);
    }

    /// <summary>
    /// For a tool that insists on naming its own output: it is pointed at a
    /// scratch folder and whatever appears there is moved into place.
    /// </summary>
    public bool UsesTemporaryDirectory
    {
        get => this.usesTemporaryDirectory;
        set => this.Set(ref this.usesTemporaryDirectory, value);
    }

    public ObservableCollection<OptionDraft> SharedOptions { get; } = [];

    public ObservableCollection<FragmentDraft> ArgumentPrefix { get; } = [];

    public ObservableCollection<FragmentDraft> ArgumentSuffix { get; } = [];

    public ObservableCollection<TargetDraft> Targets { get; } = [];

    public static BackendDraft From(BackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var draft = new BackendDraft
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Kind = definition.Kind,
            TimeoutSeconds = definition.TimeoutSeconds,
            FileName = definition.Executable?.FileName ?? string.Empty,
            BundledPath = definition.Executable?.BundledPath,
            SearchPaths = string.Join(Environment.NewLine, definition.Executable?.SearchPaths ?? []),
            ProbeArguments = JoinList(definition.Executable?.ProbeArguments ?? []),
            VersionPattern = definition.Executable?.VersionPattern,
            HomepageUrl = definition.Executable?.HomepageUrl,
            SuccessExitCodes = JoinList(definition.SuccessExitCodes.Select(c => c.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            ProgressStream = (definition.Progress?.Stream ?? Henkan.Core.Backends.ProgressStream.StandardError).ToString(),
            DurationPattern = definition.Progress?.DurationPattern,
            PositionPattern = definition.Progress?.PositionPattern,
            PercentPattern = definition.Progress?.PercentPattern,
            UsesTemporaryDirectory = definition.OutputHandling == OutputHandling.TemporaryDirectory,
        };

        foreach (OptionDescriptor option in definition.SharedOptions)
        {
            draft.SharedOptions.Add(OptionDraft.From(option));
        }

        foreach (ArgumentFragment fragment in definition.ArgumentPrefix)
        {
            draft.ArgumentPrefix.Add(FragmentDraft.From(fragment));
        }

        foreach (ArgumentFragment fragment in definition.ArgumentSuffix)
        {
            draft.ArgumentSuffix.Add(FragmentDraft.From(fragment));
        }

        foreach (ConversionTarget target in definition.Targets)
        {
            draft.Targets.Add(TargetDraft.From(target));
        }

        return draft;
    }

    /// <summary>
    /// Rebuilds the definition. The runtime flags are left alone: whether a
    /// definition is built in and where it came from are facts about the file,
    /// not things the editor gets to decide.
    /// </summary>
    public BackendDefinition ToDefinition()
    {
        ExecutableDefinition? executable = this.NeedsExecutable && !string.IsNullOrWhiteSpace(this.FileName)
            ? new ExecutableDefinition
            {
                FileName = this.FileName.Trim(),
                BundledPath = Blank(this.BundledPath),
                SearchPaths = SplitLines(this.SearchPaths),
                ProbeArguments = SplitList(this.ProbeArguments),
                VersionPattern = Blank(this.VersionPattern),
                HomepageUrl = Blank(this.HomepageUrl),
            }
            : null;

        bool hasProgress = !string.IsNullOrWhiteSpace(this.DurationPattern)
            || !string.IsNullOrWhiteSpace(this.PositionPattern)
            || !string.IsNullOrWhiteSpace(this.PercentPattern);

        ProgressDefinition? progress = hasProgress
            ? new ProgressDefinition
            {
                Stream = Enum.TryParse(this.ProgressStream, ignoreCase: true, out Henkan.Core.Backends.ProgressStream stream)
                    ? stream
                    : Henkan.Core.Backends.ProgressStream.StandardError,
                DurationPattern = Blank(this.DurationPattern),
                PositionPattern = Blank(this.PositionPattern),
                PercentPattern = Blank(this.PercentPattern),
            }
            : null;

        return new BackendDefinition
        {
            Id = this.Id.Trim(),
            Progress = progress,
            OutputHandling = this.UsesTemporaryDirectory ? OutputHandling.TemporaryDirectory : OutputHandling.Path,
            Name = string.IsNullOrWhiteSpace(this.Name) ? this.Id.Trim() : this.Name.Trim(),
            Description = Blank(this.Description),
            Kind = this.Kind,
            Executable = executable,
            SuccessExitCodes = [.. SplitList(this.SuccessExitCodes)
                .Select(c => int.TryParse(c, out int code) ? code : 0)
                .DefaultIfEmpty(0)],
            SharedOptions = [.. this.SharedOptions.Where(o => !string.IsNullOrWhiteSpace(o.Id)).Select(o => o.ToDescriptor())],
            ArgumentPrefix = [.. this.ArgumentPrefix.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => f.ToFragment())],
            ArgumentSuffix = [.. this.ArgumentSuffix.Where(f => !string.IsNullOrWhiteSpace(f.Value)).Select(f => f.ToFragment())],
            Targets = [.. this.Targets.Where(t => !string.IsNullOrWhiteSpace(t.Id)).Select(t => t.ToTarget())],
            TimeoutSeconds = this.TimeoutSeconds,
        };
    }

    /// <summary>A starting point that already validates, so a new backend is editable rather than broken.</summary>
    public static BackendDraft CreateNew()
    {
        var draft = new BackendDraft
        {
            Id = "my-tool",
            Name = "My tool",
            Description = "What this backend does.",
            Kind = BackendKind.Process,
            FileName = "tool.exe",
            ProbeArguments = "--version",
        };

        draft.ArgumentPrefix.Add(new FragmentDraft { Value = "-i \"{input}\"" });
        draft.ArgumentSuffix.Add(new FragmentDraft { Value = "\"{output}\"" });

        var target = new TargetDraft
        {
            Id = "example",
            Label = "Example output",
            OutputExtension = "out",
            Category = "Other",
            InputExtensions = "*",
        };

        draft.Targets.Add(target);
        return draft;
    }

    private static IReadOnlyList<string> SplitLines(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
