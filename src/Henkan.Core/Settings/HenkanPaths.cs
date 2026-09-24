namespace Henkan.Core.Settings;

/// <summary>
/// Where Henkan keeps its data.
/// </summary>
/// <remarks>
/// Everything lives under one folder in LocalAppData so the whole configuration
/// can be copied to another machine, and so an MSIX install and a plain build
/// share the same presets rather than each having their own.
/// </remarks>
public static class HenkanPaths
{
    /// <summary>Root of all user data.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Henkan");

    /// <summary>User-authored and user-overridden backend definitions.</summary>
    public static string BackendsDirectory { get; } = Path.Combine(Root, "backends");

    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");

    public static string PresetsFile { get; } = Path.Combine(Root, "presets.json");

    public static string LogsDirectory { get; } = Path.Combine(Root, "logs");

    /// <summary>Scratch space for in-progress conversions, cleared on startup.</summary>
    public static string TemporaryDirectory { get; } = Path.Combine(Path.GetTempPath(), "Henkan");

    /// <summary>Creates every directory the application writes to.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(BackendsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(TemporaryDirectory);
    }

    /// <summary>
    /// Removes scratch directories left behind by a previous run that was killed
    /// before its jobs could clean up after themselves.
    /// </summary>
    public static void CleanTemporaryDirectory()
    {
        if (!Directory.Exists(TemporaryDirectory))
        {
            return;
        }

        foreach (string directory in Directory.EnumerateDirectories(TemporaryDirectory))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another instance may still be using it.
            }
        }
    }
}
