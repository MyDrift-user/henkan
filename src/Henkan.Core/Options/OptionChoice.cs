namespace Henkan.Core.Options;

/// <summary>A single entry in a <see cref="OptionKind.Choice"/> option.</summary>
public sealed record OptionChoice
{
    /// <summary>The value written into the option set and visible to templates.</summary>
    public required string Value { get; init; }

    /// <summary>What the user sees. Falls back to <see cref="Value"/>.</summary>
    public string? Label { get; init; }

    /// <summary>Optional one-line explanation shown under the entry.</summary>
    public string? Description { get; init; }

    public string DisplayLabel => string.IsNullOrWhiteSpace(this.Label) ? this.Value : this.Label;
}
