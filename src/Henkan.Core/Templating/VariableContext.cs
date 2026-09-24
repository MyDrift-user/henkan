namespace Henkan.Core.Templating;

/// <summary>
/// Supplies the values a template may reference: the paths involved in a single
/// conversion, plus every option value of the target being run.
/// </summary>
/// <remarks>
/// Built-in names take precedence over option identifiers, so an option must not
/// be called <c>input</c>, <c>output</c> and so on. <see cref="ReservedNames"/>
/// lists them and the backend editor validates against it.
/// </remarks>
public sealed class VariableContext
{
    private readonly Dictionary<string, string> builtIns;
    private readonly IReadOnlyDictionary<string, string> options;

    private VariableContext(Dictionary<string, string> builtIns, IReadOnlyDictionary<string, string> options)
    {
        this.builtIns = builtIns;
        this.options = options;
        this.Inputs = [builtIns["input"]];
    }

    /// <summary>Names the engine defines itself, which an option may not shadow.</summary>
    public static IReadOnlyList<string> ReservedNames { get; } =
    [
        "input",
        "inputs",
        "inputDir",
        "inputName",
        "inputExt",
        "inputFileName",
        "output",
        "outputDir",
        "outputName",
        "outputExt",
        "outputFileName",
        "tempDir",
        "toolDir",
    ];

    /// <summary>
    /// Every file of a conversion that takes a whole selection at once, such as
    /// merging PDFs. A command line fragment that is exactly <c>{inputs}</c>
    /// becomes one argument per file; anywhere else the name reads as the paths
    /// joined by spaces. For an ordinary conversion it holds the one input.
    /// </summary>
    public IReadOnlyList<string> Inputs { get; private set; }

    /// <summary>Gives the context the whole selection, for a conversion that combines its inputs.</summary>
    public VariableContext WithInputs(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count > 0)
        {
            this.Inputs = inputs;
        }

        return this;
    }

    /// <summary>The path variables only, useful for previewing an output path template.</summary>
    public IReadOnlyDictionary<string, string> BuiltIns => this.builtIns;

    public static VariableContext Create(
        string inputPath,
        string outputPath,
        string temporaryDirectory,
        IReadOnlyDictionary<string, string>? optionValues = null,
        string? toolDirectory = null,
        string? outputExtension = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        // An extension with a dot in it, such as tar.gz, is more than
        // Path.GetExtension sees. Told the real one, the name and the extension
        // split where the format says rather than at the last dot.
        string outputFileName = Path.GetFileName(outputPath);
        string outputName = Path.GetFileNameWithoutExtension(outputPath);
        string outputExt = TrimLeadingDot(Path.GetExtension(outputPath));
        string suffix = "." + TrimLeadingDot(outputExtension ?? string.Empty);

        if (suffix.Length > 1 && outputFileName.Length > suffix.Length && outputFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            outputName = outputFileName[..^suffix.Length];
            outputExt = suffix[1..];
        }

        var builtIns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["input"] = inputPath,
            ["inputDir"] = Path.GetDirectoryName(inputPath) ?? string.Empty,
            ["inputName"] = Path.GetFileNameWithoutExtension(inputPath),
            ["inputExt"] = TrimLeadingDot(Path.GetExtension(inputPath)),
            ["inputFileName"] = Path.GetFileName(inputPath),
            ["output"] = outputPath,
            ["outputDir"] = Path.GetDirectoryName(outputPath) ?? string.Empty,
            ["outputName"] = outputName,
            ["outputExt"] = outputExt,
            ["outputFileName"] = outputFileName,
            ["tempDir"] = temporaryDirectory,
            ["toolDir"] = toolDirectory ?? string.Empty,
        };

        return new VariableContext(
            builtIns,
            optionValues ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Builds a context for previewing a template before any file is chosen, so
    /// the backend editor can show a realistic command line as the user types.
    /// </summary>
    public static VariableContext CreateSample(
        string outputExtension,
        IReadOnlyDictionary<string, string>? optionValues = null)
    {
        string input = Path.Combine(@"C:\Users\Example\Videos", "holiday clip.mov");
        string output = Path.Combine(@"C:\Users\Example\Videos", $"holiday clip.{TrimLeadingDot(outputExtension)}");
        return Create(input, output, Path.GetTempPath(), optionValues, @"C:\Program Files\Henkan\tools\ffmpeg", outputExtension);
    }

    /// <summary>Resolves a template variable, or returns null when the name is unknown.</summary>
    public string? Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // "option:Foo" forces the option namespace, for the rare case where a
        // future built-in would otherwise shadow an existing option.
        if (name.StartsWith("option:", StringComparison.OrdinalIgnoreCase))
        {
            string optionName = name["option:".Length..];
            return this.options.TryGetValue(optionName, out string? scoped) ? scoped : null;
        }

        if (name.Equals("inputs", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(" ", this.Inputs);
        }

        if (this.builtIns.TryGetValue(name, out string? builtIn))
        {
            return builtIn;
        }

        return this.options.TryGetValue(name, out string? value) ? value : null;
    }

    /// <summary>Lookup delegate shape used by the renderer and the condition evaluator.</summary>
    public Func<string, string?> Lookup => this.Resolve;

    private static string TrimLeadingDot(string extension) =>
        extension.StartsWith('.') ? extension[1..] : extension;
}
