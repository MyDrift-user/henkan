using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Henkan.App.Services;

/// <summary>
/// What the process was asked to do on startup.
/// </summary>
/// <remarks>
/// Command line shape, produced by the shell extension and usable by hand:
/// <code>Henkan.exe [--preset &lt;id&gt;] [--run] file1 file2 ...</code>
/// <c>--preset</c> picks the preset, <c>--run</c> starts converting without
/// waiting for the user to click. Files alone open the Convert page with them
/// listed.
/// </remarks>
public sealed record LaunchArguments(IReadOnlyList<string> Files, string? PresetId, bool Run)
{
    public static LaunchArguments Empty { get; } = new([], null, false);

    private static string? ExecutableName { get; } =
        Environment.ProcessPath is { } path ? Path.GetFileName(path) : null;

    public bool HasWork => this.Files.Count > 0;

    public static LaunchArguments Parse(IEnumerable<string> args)
    {
        var files = new List<string>();
        string? presetId = null;
        bool run = false;

        using IEnumerator<string> cursor = args.GetEnumerator();
        while (cursor.MoveNext())
        {
            string arg = cursor.Current;

            if (arg.Equals("--preset", StringComparison.OrdinalIgnoreCase) && cursor.MoveNext())
            {
                presetId = cursor.Current;
                continue;
            }

            if (arg.StartsWith("--preset=", StringComparison.OrdinalIgnoreCase))
            {
                presetId = arg["--preset=".Length..];
                continue;
            }

            if (arg.Equals("--run", StringComparison.OrdinalIgnoreCase))
            {
                run = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            // A folder is a legitimate thing to archive, so both are accepted and
            // the target decides which it can take.
            if ((File.Exists(arg) || Directory.Exists(arg)) && !IsSelf(arg))
            {
                files.Add(Path.GetFullPath(arg));
            }
        }

        return new LaunchArguments(files, presetId, run);
    }

    public static LaunchArguments FromActivation(AppActivationArguments activation)
    {
        return activation.Kind switch
        {
            ExtendedActivationKind.Launch when activation.Data is ILaunchActivatedEventArgs launch =>
                Parse(StripExecutable(SplitCommandLine(launch.Arguments))),

            ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs files =>
                new LaunchArguments([.. files.Files.Select(f => f.Path)], null, false),

            _ => Empty,
        };
    }

    /// <summary>
    /// Drops a leading argv[0].
    /// </summary>
    /// <remarks>
    /// A packaged activation hands over the whole command line, executable and
    /// all, while <see cref="Environment.GetCommandLineArgs"/> is skipped past it
    /// by the caller. Without this the executable itself is a path that exists,
    /// so it gets queued as a file to convert alongside the real selection.
    /// </remarks>
    public static IReadOnlyList<string> StripExecutable(IReadOnlyList<string> tokens) =>
        tokens.Count > 0 && IsSelf(tokens[0]) ? [.. tokens.Skip(1)] : tokens;

    /// <summary>
    /// True for the running executable. The path is compared by file name as well,
    /// because the execution alias and the package install directory are two
    /// different paths to the same program.
    /// </summary>
    private static bool IsSelf(string path)
    {
        if (ExecutableName is null)
        {
            return false;
        }

        return Path.GetFileName(path).Equals(ExecutableName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Splits a raw command line the way the C runtime does: whitespace separates,
    /// double quotes group, a backslash escapes only a following quote.
    /// </summary>
    public static IReadOnlyList<string> SplitCommandLine(string commandLine)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        for (int i = 0; i < commandLine.Length; i++)
        {
            char c = commandLine[i];

            if (c == '\\' && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
            {
                current.Append('"');
                hasToken = true;
                i++;
                continue;
            }

            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(c);
            hasToken = true;
        }

        if (hasToken)
        {
            result.Add(current.ToString());
        }

        return result;
    }
}
