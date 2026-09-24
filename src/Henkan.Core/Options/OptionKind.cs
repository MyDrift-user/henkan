namespace Henkan.Core.Options;

/// <summary>
/// The editor the UI generates for an option. The conversion engine itself is
/// indifferent to this; every value travels as a string and is coerced on use.
/// </summary>
public enum OptionKind
{
    /// <summary>Free text, rendered as a single-line text box.</summary>
    Text,

    /// <summary>True/false, rendered as a toggle switch.</summary>
    Boolean,

    /// <summary>Whole number, rendered as a number box with optional bounds.</summary>
    Integer,

    /// <summary>Fractional number, rendered as a number box with optional bounds.</summary>
    Decimal,

    /// <summary>One of <see cref="OptionDescriptor.Choices"/>, rendered as a combo box.</summary>
    Choice,

    /// <summary>Bounded number, rendered as a slider. Requires Minimum and Maximum.</summary>
    Range,

    /// <summary>Path to an existing file, rendered with a browse button.</summary>
    FilePath,

    /// <summary>Path to a directory, rendered with a browse button.</summary>
    DirectoryPath,
}
