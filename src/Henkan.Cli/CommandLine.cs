namespace Henkan.Cli;

/// <summary>
/// A small argument parser: <c>--name value</c>, <c>--name=value</c>, bare flags,
/// and everything else collected in order as positional arguments.
/// </summary>
internal sealed class CommandLine
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "json", "all", "overwrite", "keep", "verbose",
    };

    private readonly Dictionary<string, List<string>> named = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> positional = [];

    public IReadOnlyList<string> Positional => this.positional;

    public bool Json => this.Has("json");

    public bool All => this.Has("all");

    public static CommandLine Parse(IEnumerable<string> args)
    {
        var result = new CommandLine();
        using IEnumerator<string> cursor = args.GetEnumerator();

        while (cursor.MoveNext())
        {
            string argument = cursor.Current;

            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                result.positional.Add(argument);
                continue;
            }

            string name = argument[2..];
            string? value = null;

            int equals = name.IndexOf('=', StringComparison.Ordinal);
            if (equals >= 0)
            {
                value = name[(equals + 1)..];
                name = name[..equals];
            }
            else if (!Flags.Contains(name) && cursor.MoveNext())
            {
                value = cursor.Current;
            }

            result.Add(name, value ?? string.Empty);
        }

        return result;
    }

    public bool Has(string name) => this.named.ContainsKey(name);

    public string? Get(string name) =>
        this.named.TryGetValue(name, out List<string>? values) && values.Count > 0 ? values[^1] : null;

    public IReadOnlyList<string> GetAll(string name) =>
        this.named.TryGetValue(name, out List<string>? values) ? values : [];

    /// <summary>The value of a required option, or a usage failure naming it.</summary>
    public string Require(string name) =>
        this.Get(name) ?? throw new CommandException($"--{name} is required.", Cli.Usage);

    /// <summary>Positional argument, or a usage failure describing what was wanted.</summary>
    public string RequirePositional(int index, string what) =>
        index < this.positional.Count
            ? this.positional[index]
            : throw new CommandException($"Expected {what}.", Cli.Usage);

    /// <summary>Parses repeated <c>--set Name=Value</c> pairs.</summary>
    public Dictionary<string, string> GetSettings()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string entry in this.GetAll("set"))
        {
            int equals = entry.IndexOf('=', StringComparison.Ordinal);

            if (equals <= 0)
            {
                throw new CommandException($"--set expects Name=Value, got \"{entry}\".", Cli.Usage);
            }

            result[entry[..equals]] = entry[(equals + 1)..];
        }

        return result;
    }

    private void Add(string name, string value)
    {
        if (!this.named.TryGetValue(name, out List<string>? values))
        {
            values = [];
            this.named[name] = values;
        }

        values.Add(value);
    }
}
