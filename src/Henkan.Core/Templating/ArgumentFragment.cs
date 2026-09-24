namespace Henkan.Core.Templating;

/// <summary>
/// One piece of a command line. A target's argument list is built by rendering
/// every fragment whose <see cref="When"/> condition holds and concatenating the
/// resulting tokens, which keeps optional flags out of the command entirely
/// rather than passing them with empty values.
/// </summary>
public sealed record ArgumentFragment
{
    /// <summary>
    /// The template text, for example <c>-b:a {AudioBitrate}k</c>. Quoting is
    /// resolved before variables are substituted, so a value containing spaces
    /// can never split into two arguments.
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// Condition over the option values. Null or empty means always include.
    /// </summary>
    public string? When { get; init; }

    /// <summary>
    /// Splits the fragment on whitespace <em>after</em> substitution instead of
    /// before. Needed only where a single option holds several arguments, such
    /// as a free-form "extra arguments" box. Off by default because it makes
    /// values containing spaces split unexpectedly.
    /// </summary>
    public bool SplitAfterExpansion { get; init; }

    /// <summary>Optional note shown in the backend editor.</summary>
    public string? Comment { get; init; }
}
