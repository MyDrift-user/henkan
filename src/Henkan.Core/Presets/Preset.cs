using Henkan.Core.Backends;
using Henkan.Core.Settings;

namespace Henkan.Core.Presets;

/// <summary>
/// A named, ready-to-run conversion: which target to use, the option values that
/// differ from the target's defaults, and where the result goes.
/// </summary>
/// <remarks>
/// Only overridden options are stored. A preset therefore records what the user
/// actually chose, and an option they never touched keeps following the backend
/// definition even after that definition is edited.
/// </remarks>
public sealed record Preset
{
    public required string Id { get; init; }

    /// <summary>Shown in the preset list and in the Explorer context menu.</summary>
    public required string Name { get; init; }

    /// <summary>The <c>backendId/targetId</c> pair this preset runs.</summary>
    public required string TargetKey { get; init; }

    /// <summary>Option values that differ from the target's defaults.</summary>
    public Dictionary<string, string> Options { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Input extensions, without dots, this preset offers itself for. Family
    /// names such as <c>$audio</c> are allowed here too. Empty means everything
    /// the target accepts, which is the normal case: the target already knows
    /// what it can read, and repeating it in the preset only creates a second
    /// list to keep in step.
    /// </summary>
    public List<string> InputExtensions { get; init; } = [];

    /// <summary>
    /// Where the output goes, as a path template. Supports the same variables as
    /// argument templates, so <c>{inputDir}\converted\{inputName}.{outputExt}</c>
    /// works as written.
    /// </summary>
    public string OutputPathTemplate { get; init; } = "{inputDir}\\{inputName}.{outputExt}";

    public FileConflictPolicy ConflictPolicy { get; init; } = FileConflictPolicy.Rename;

    public InputAction AfterConversion { get; init; } = InputAction.Keep;

    /// <summary>Destination when <see cref="AfterConversion"/> is <see cref="InputAction.Archive"/>.</summary>
    public string ArchivePathTemplate { get; init; } = "{inputDir}\\originals\\{inputFileName}";

    public bool ShowInContextMenu { get; init; } = true;

    /// <summary>
    /// False for a conversion's own entry, of which there is at most one per
    /// target; true for a further, named set of values on the same target, such
    /// as "MP3 320k" next to "To MP3".
    /// </summary>
    /// <remarks>
    /// A variant stores its values in full rather than as changes to the entry
    /// it sits under. It then runs exactly as it reads, and changing the
    /// conversion's own entry never changes a variant behind the user's back.
    /// </remarks>
    public bool IsVariant { get; init; }

    /// <summary>Position in the preset list and the context menu.</summary>
    public int SortOrder { get; init; }

    /// <summary>
    /// True when this preset's own extension list accepts a file. A preset that
    /// names nothing accepts everything here and is narrowed by its target
    /// instead.
    /// </summary>
    public bool Accepts(string inputExtension) =>
        FormatFamilies.Matches(this.InputExtensions, inputExtension);

    /// <summary>
    /// The extensions this preset offers itself for once its target has had its
    /// say, or null when the two agree on nothing and the preset should not be
    /// offered at all. An empty list means every extension.
    /// </summary>
    /// <remarks>
    /// A preset narrows its target and never widens it. Offering a preset for a
    /// file the backend cannot read would only move the failure out of the menu
    /// and into the conversion, so the intersection is what gets used.
    /// </remarks>
    public IReadOnlyList<string>? ResolveInputExtensions(ConversionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        IReadOnlyList<string> fromTarget = target.ExpandInputExtensions();

        if (this.InputExtensions.Count == 0)
        {
            return fromTarget;
        }

        IReadOnlyList<string> fromPreset = FormatFamilies.Expand(this.InputExtensions);

        if (fromPreset.Contains(FormatFamilies.Any))
        {
            return fromTarget;
        }

        if (fromTarget.Count == 0)
        {
            return fromPreset;
        }

        string[] both = [.. fromPreset.Intersect(fromTarget, StringComparer.OrdinalIgnoreCase)];
        return both.Length > 0 ? both : null;
    }

    public static Preset Create(string name, string targetKey) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Name = name,
        TargetKey = targetKey,
    };
}
