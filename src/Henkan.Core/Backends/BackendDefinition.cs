using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Backends;

/// <summary>How a backend performs work.</summary>
public enum BackendKind
{
    /// <summary>
    /// Runs an external executable with a templated argument list. This is the
    /// only kind a user can add, and it is what the built-in ffmpeg and
    /// Ghostscript definitions use, so a hand-written backend is a first-class
    /// citizen rather than a lesser plugin.
    /// </summary>
    Process,

    /// <summary>In-process image conversion through Magick.NET.</summary>
    ImageMagick,

    /// <summary>
    /// Several existing targets run in sequence, each handing its output to the
    /// next. This is how a spreadsheet reaches a PNG: nothing converts a .csv to
    /// an image directly, but going through a PDF does.
    /// </summary>
    Pipeline,

    /// <summary>Office document export through COM automation of an installed Office.</summary>
    Office,

    /// <summary>
    /// Archive packing and repacking through 7-Zip. Converting one archive into
    /// another is an unpack followed by a pack, with a directory in between, which
    /// is a shape neither a single command nor a pipeline can carry.
    /// </summary>
    Archive,
}

/// <summary>Which stream a process backend parses for progress.</summary>
public enum ProgressStream
{
    StandardError,
    StandardOutput,
}

/// <summary>How a process backend delivers its result.</summary>
public enum OutputHandling
{
    /// <summary>The tool writes to <c>{output}</c> directly. The normal case.</summary>
    Path,

    /// <summary>
    /// The tool insists on naming the file itself, so it is pointed at
    /// <c>{tempDir}</c> and the engine moves whatever it produced to the real
    /// output path afterwards. LibreOffice's <c>--convert-to</c> works this way.
    /// </summary>
    TemporaryDirectory,
}

/// <summary>Locates the executable a <see cref="BackendKind.Process"/> backend runs.</summary>
public sealed record ExecutableDefinition
{
    /// <summary>File name to look for on PATH, for example <c>ffmpeg.exe</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Path relative to the bundled tools directory, for example
    /// <c>ffmpeg/ffmpeg.exe</c>. Null means the tool is never bundled and must
    /// come from PATH or an explicit override.
    /// </summary>
    public string? BundledPath { get; init; }

    /// <summary>
    /// Absolute paths tried after the bundled copy and before PATH. Environment
    /// variables in the form <c>%ProgramFiles%</c> are expanded, which is how
    /// the LibreOffice definition finds a default install without configuration.
    /// </summary>
    public IReadOnlyList<string> SearchPaths { get; init; } = [];

    /// <summary>Arguments used to confirm the tool runs, for example <c>-version</c>.</summary>
    public IReadOnlyList<string> ProbeArguments { get; init; } = [];

    /// <summary>Regex with one capture group pulling a version out of the probe output.</summary>
    public string? VersionPattern { get; init; }

    /// <summary>Shown in the UI when the tool is missing, so the user knows where to get it.</summary>
    public string? HomepageUrl { get; init; }
}

/// <summary>
/// Regexes that turn a process's chatter into a progress percentage. All parts
/// are optional; a backend with no usable progress output simply reports
/// indeterminate progress.
/// </summary>
public sealed record ProgressDefinition
{
    public ProgressStream Stream { get; init; } = ProgressStream.StandardError;

    /// <summary>
    /// Matched once to learn the total length. Must capture hours, minutes and
    /// seconds in groups 1 to 3, where seconds may be fractional.
    /// </summary>
    public string? DurationPattern { get; init; }

    /// <summary>Matched repeatedly for the current position, same group layout as the duration.</summary>
    public string? PositionPattern { get; init; }

    /// <summary>Matched repeatedly for a direct percentage in capture group 1.</summary>
    public string? PercentPattern { get; init; }
}

/// <summary>
/// One leg of a <see cref="BackendKind.Pipeline"/> conversion.
/// </summary>
/// <param name="Targets">
/// Targets that can perform this step, best first. The first one that is
/// available wins, which is how a document reaches a PDF through Microsoft
/// Office on one machine and LibreOffice on another with a single definition.
/// </param>
/// <param name="Options">
/// Option values handed to the step. Values may reference the pipeline target's
/// own options as <c>{Name}</c>, so a resolution asked for once is passed to
/// whichever leg actually decides it.
/// </param>
public sealed record PipelineStep(
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, string>? Options = null);

/// <summary>
/// One output format a backend can produce, with the option schema and argument
/// template that belong to it. Adding a target to a definition file is enough to
/// make a new conversion appear in the application.
/// </summary>
public sealed record ConversionTarget
{
    /// <summary>Stable identifier, unique within the backend.</summary>
    public required string Id { get; init; }

    /// <summary>Shown in the format picker, for example "MP3 audio".</summary>
    public required string Label { get; init; }

    /// <summary>Extension given to the produced file, without a leading dot.</summary>
    public required string OutputExtension { get; init; }

    /// <summary>Grouping heading in the format picker, for example "Audio".</summary>
    public string Category { get; init; } = "Other";

    public string? Description { get; init; }

    /// <summary>
    /// Input extensions this target accepts, without dots. A family name such as
    /// <c>$audio</c> stands for the whole group, and a single <c>*</c> accepts
    /// anything the backend is willing to read. Naming the real inputs rather
    /// than reaching for <c>*</c> is what keeps an image out of the audio
    /// conversions in the Explorer menu.
    /// </summary>
    /// <seealso cref="FormatFamilies"/>
    public IReadOnlyList<string> InputExtensions { get; init; } = ["*"];

    /// <summary>
    /// True when a folder can be given to this target instead of a file. Only
    /// archiving means anything for a folder, so almost nothing sets it.
    /// </summary>
    public bool AcceptsFolders { get; init; }

    /// <summary>
    /// True when a selection of several files is one conversion rather than one
    /// each. Archiving is the case that needs it: picking five files and asking
    /// for a zip means one zip holding five files, not five zips.
    /// </summary>
    public bool CombinesInputs { get; init; }

    /// <summary>
    /// True when the result is a folder rather than a file, which is what
    /// extracting an archive produces. The output path is then built without an
    /// extension and the conflict policy counts existing folders.
    /// </summary>
    public bool OutputIsDirectory { get; init; }

    /// <summary>Options offered for this target, on top of the backend's shared options.</summary>
    public IReadOnlyList<OptionDescriptor> Options { get; init; } = [];

    /// <summary>
    /// The middle of the command line. The backend's prefix and suffix wrap it,
    /// which keeps <c>-i "{input}"</c> and <c>"{output}"</c> declared once.
    /// </summary>
    public IReadOnlyList<ArgumentFragment> Arguments { get; init; } = [];

    /// <summary>
    /// Selects the code path for in-process backends. Ignored by
    /// <see cref="BackendKind.Process"/>.
    /// </summary>
    public string? Operation { get; init; }

    /// <summary>
    /// Values the backend reads that are not user-facing options: file format
    /// codes, a mode switch, anything specific to one backend. Keeping them here
    /// rather than inventing a field per backend means a new target is still just
    /// data.
    /// </summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The legs of a <see cref="BackendKind.Pipeline"/> target, in order.</summary>
    public IReadOnlyList<PipelineStep> Steps { get; init; } = [];

    /// <summary>Reads a backend setting, or the fallback when it is absent.</summary>
    public string? Setting(string name) =>
        this.Settings.TryGetValue(name, out string? value) ? value : null;

    public bool Accepts(string inputExtension) =>
        FormatFamilies.Matches(this.InputExtensions, inputExtension);

    /// <summary>
    /// The accepted extensions with every family resolved, for callers that need
    /// the concrete list rather than a yes or no. Empty means every extension,
    /// which is what both an empty declaration and <c>*</c> come to.
    /// </summary>
    public IReadOnlyList<string> ExpandInputExtensions()
    {
        IReadOnlyList<string> expanded = FormatFamilies.Expand(this.InputExtensions);
        return expanded.Contains(FormatFamilies.Any) ? [] : expanded;
    }
}

/// <summary>
/// A complete backend: what to run, what options it offers, and how to build its
/// command line. Built-in definitions are embedded JSON of exactly this shape,
/// and user definitions dropped into the backends folder are loaded the same way.
/// </summary>
public sealed record BackendDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public BackendKind Kind { get; init; } = BackendKind.Process;

    /// <summary>Required for <see cref="BackendKind.Process"/>, ignored otherwise.</summary>
    public ExecutableDefinition? Executable { get; init; }

    /// <summary>Exit codes treated as success. Defaults to zero only.</summary>
    public IReadOnlyList<int> SuccessExitCodes { get; init; } = [0];

    public ProgressDefinition? Progress { get; init; }

    public OutputHandling OutputHandling { get; init; } = OutputHandling.Path;

    /// <summary>Options merged into every target, for things like hardware acceleration.</summary>
    public IReadOnlyList<OptionDescriptor> SharedOptions { get; init; } = [];

    /// <summary>Argument fragments placed before the target's own, typically the input file.</summary>
    public IReadOnlyList<ArgumentFragment> ArgumentPrefix { get; init; } = [];

    /// <summary>Argument fragments placed after the target's own, typically the output file.</summary>
    public IReadOnlyList<ArgumentFragment> ArgumentSuffix { get; init; } = [];

    public IReadOnlyList<ConversionTarget> Targets { get; init; } = [];

    /// <summary>Seconds before a run is abandoned. Zero or less means no limit.</summary>
    public int TimeoutSeconds { get; init; }

    /// <summary>
    /// True for definitions shipped with the application. Built-ins cannot be
    /// deleted, but they can be copied into an editable user backend.
    /// </summary>
    public bool IsBuiltIn { get; init; }

    /// <summary>Where this definition was loaded from, null for embedded built-ins.</summary>
    public string? SourcePath { get; init; }

    public ConversionTarget? FindTarget(string targetId) =>
        this.Targets.FirstOrDefault(t => t.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The full option schema for a target: shared options first, then the target's own.</summary>
    public IReadOnlyList<OptionDescriptor> GetOptionSchema(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (this.SharedOptions.Count == 0)
        {
            return target.Options;
        }

        var schema = new List<OptionDescriptor>(this.SharedOptions.Count + target.Options.Count);
        schema.AddRange(this.SharedOptions);
        schema.AddRange(target.Options);
        return schema;
    }

    /// <summary>The whole command line for a target, prefix then target then suffix.</summary>
    public IReadOnlyList<ArgumentFragment> GetArgumentTemplate(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var fragments = new List<ArgumentFragment>(
            this.ArgumentPrefix.Count + target.Arguments.Count + this.ArgumentSuffix.Count);
        fragments.AddRange(this.ArgumentPrefix);
        fragments.AddRange(target.Arguments);
        fragments.AddRange(this.ArgumentSuffix);
        return fragments;
    }
}
