namespace Henkan.Core.Tools;

/// <summary>
/// Finds the executable behind a process backend.
/// </summary>
/// <remarks>
/// Nothing here is required for the build to succeed. When a tool is absent the
/// locator simply returns null, the backend reports itself unavailable, and its
/// conversion targets are left out of the picker. Search order, first hit wins:
/// <list type="number">
///   <item>an explicit path the user set in settings,</item>
///   <item>the bundled copy next to the application, placed by tools/fetch-deps.ps1,</item>
///   <item>the repository build output, so a developer build finds it without installing,</item>
///   <item>the PATH environment variable.</item>
/// </list>
/// </remarks>
public sealed class ToolLocator
{
    private readonly Func<string, string?> overrideLookup;
    private readonly IReadOnlyList<string> bundleRoots;

    public ToolLocator(Func<string, string?>? overrideLookup = null, IEnumerable<string>? extraBundleRoots = null)
    {
        this.overrideLookup = overrideLookup ?? (_ => null);

        var roots = new List<string>();
        string baseDirectory = AppContext.BaseDirectory;
        roots.Add(Path.Combine(baseDirectory, "tools"));

        // During a developer build the binaries live in the repository rather
        // than beside the executable, so walk up looking for build\tools.
        for (DirectoryInfo? directory = new(baseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "build", "tools");
            if (Directory.Exists(candidate))
            {
                roots.Add(candidate);
                break;
            }
        }

        if (extraBundleRoots is not null)
        {
            roots.AddRange(extraBundleRoots);
        }

        this.bundleRoots = roots;
    }

    /// <summary>Directories searched for bundled tools, in order. Exposed for diagnostics.</summary>
    public IReadOnlyList<string> BundleRoots => this.bundleRoots;

    /// <summary>
    /// Resolves the executable for a backend, or returns null when it cannot be found.
    /// </summary>
    /// <param name="backendId">Used to look up a user-configured override.</param>
    /// <param name="fileName">Executable name, for example <c>ffmpeg.exe</c>.</param>
    /// <param name="bundledPath">Path under the tools directory, or null if never bundled.</param>
    /// <param name="searchPaths">Absolute candidates, with <c>%VAR%</c> expanded, tried before PATH.</param>
    public ToolResolution Resolve(
        string backendId,
        string fileName,
        string? bundledPath,
        IReadOnlyList<string>? searchPaths = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backendId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        string? configured = this.overrideLookup(backendId);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured)
                ? new ToolResolution(configured, ToolSource.UserConfigured, null)
                : new ToolResolution(null, ToolSource.None, $"The configured path \"{configured}\" does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(bundledPath))
        {
            foreach (string root in this.bundleRoots)
            {
                string candidate = Path.Combine(root, bundledPath);
                if (File.Exists(candidate))
                {
                    return new ToolResolution(candidate, ToolSource.Bundled, null);
                }
            }
        }

        if (searchPaths is not null)
        {
            foreach (string searchPath in searchPaths)
            {
                string expanded = Environment.ExpandEnvironmentVariables(searchPath);
                if (File.Exists(expanded))
                {
                    return new ToolResolution(expanded, ToolSource.SearchPath, null);
                }
            }
        }

        string? onPath = FindOnPath(fileName);
        if (onPath is not null)
        {
            return new ToolResolution(onPath, ToolSource.SystemPath, null);
        }

        return new ToolResolution(
            null,
            ToolSource.None,
            $"\"{fileName}\" was not found. Run tools\\fetch-deps.ps1 to download it, install it and put it on PATH, or set the path in Settings.");
    }

    /// <summary>Searches PATH, honouring PATHEXT for names given without an extension.</summary>
    public static string? FindOnPath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Path.IsPathRooted(fileName))
        {
            return File.Exists(fileName) ? fileName : null;
        }

        string[] extensions = Path.HasExtension(fileName)
            ? [string.Empty]
            : (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = directory.Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (string extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(trimmed, fileName + extension);
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry should not take the whole search down.
                    break;
                }

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}

/// <summary>Where a resolved tool came from.</summary>
public enum ToolSource
{
    None,
    UserConfigured,
    Bundled,
    SearchPath,
    SystemPath,
}

/// <summary>Outcome of a tool lookup.</summary>
/// <param name="Path">Full path to the executable, or null when it was not found.</param>
/// <param name="Source">Which search step produced the hit.</param>
/// <param name="Reason">Why the lookup failed, for display in the UI.</param>
public readonly record struct ToolResolution(string? Path, ToolSource Source, string? Reason)
{
    public bool Found => this.Path is not null;
}
