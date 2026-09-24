namespace Henkan.Core.Options;

/// <summary>
/// Declares one user-facing conversion option. The settings UI is generated
/// entirely from a list of these, so adding an option to a backend definition
/// is enough to make it appear in the application with no UI code.
/// </summary>
public sealed record OptionDescriptor
{
    /// <summary>
    /// Identifier used in argument templates as <c>{Id}</c> and in conditions.
    /// Must be unique within a target and should be a plain identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Label shown next to the editor.</summary>
    public required string Label { get; init; }

    /// <summary>Optional help text shown beneath the editor.</summary>
    public string? Description { get; init; }

    public OptionKind Kind { get; init; } = OptionKind.Text;

    /// <summary>Heading the option is filed under in the UI. Null means "General".</summary>
    public string? Group { get; init; }

    /// <summary>Value used when the user has not set one. Always stored as a string.</summary>
    public string? Default { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Step { get; init; }

    /// <summary>Suffix shown after the editor, for example "kbit/s" or "fps".</summary>
    public string? Unit { get; init; }

    public IReadOnlyList<OptionChoice> Choices { get; init; } = [];

    /// <summary>
    /// Condition over the other options in the same target. When it evaluates
    /// false the option is hidden and its argument fragments are skipped.
    /// Example: <c>EnableVideo &amp;&amp; VideoMode == 'Bitrate'</c>.
    /// </summary>
    public string? VisibleWhen { get; init; }

    /// <summary>Options marked advanced are collapsed behind a disclosure by default.</summary>
    public bool Advanced { get; init; }
}
