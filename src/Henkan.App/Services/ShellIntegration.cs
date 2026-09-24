using System.Text.Json;
using Henkan.Core.Backends;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Henkan.App.Services;

/// <summary>
/// The hand-off between the app and the Explorer context menu.
/// </summary>
/// <remarks>
/// The shell extension runs in a system surrogate with no access to the app's
/// objects, so the app writes a compact menu description to a fixed path and the
/// extension reads it on every right click. Keeping it a separate, tiny file
/// rather than presets.json means the extension never has to understand the
/// preset format and a broken preset file cannot break the menu.
/// </remarks>
public static class ShellIntegration
{
    /// <summary>Read by Henkan.ShellExtension. Changing the shape requires changing both sides.</summary>
    public static string MenuFile { get; } = Path.Combine(HenkanPaths.Root, "context-menu.json");

    /// <summary>
    /// Writes the menu for the current presets.
    /// </summary>
    /// <remarks>
    /// Two things are resolved here rather than in the extension, which has no
    /// backends and must stay small. A preset that names no extensions of its own
    /// inherits the ones its target can actually read, so nothing offers to turn
    /// a photograph into an MP3. And a preset whose backend did not probe is left
    /// out entirely, because an entry that can only fail is worse than no entry.
    /// </remarks>
    public static void PublishPresets(IEnumerable<Preset> presets, BackendRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(presets);
        ArgumentNullException.ThrowIfNull(registry);

        var entries = new List<MenuEntry>();

        foreach (Preset preset in presets.Where(p => p.ShowInContextMenu).OrderBy(p => p.SortOrder))
        {
            TargetDescriptor? descriptor = registry.FindTarget(preset.TargetKey);

            if (descriptor is not { IsAvailable: true })
            {
                continue;
            }

            IReadOnlyList<string>? extensions = preset.ResolveInputExtensions(descriptor.Value.Target);

            if (extensions is not null)
            {
                entries.Add(new MenuEntry(
                    preset.Id,
                    preset.Name,
                    extensions,
                    descriptor.Value.Target.AcceptsFolders,
                    GroupName(descriptor.Value.Target.Category)));
            }
        }

        var menu = new MenuFileContent(1, Environment.ProcessPath ?? "Henkan.exe", entries);

        try
        {
            Directory.CreateDirectory(HenkanPaths.Root);
            string temporary = MenuFile + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(menu, JsonDefaults.Options));
            File.Move(temporary, MenuFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppServices.Log.LogWarning(ex, "Could not write the context menu file");
        }
    }

    /// <summary>
    /// The kind an entry belongs to, which keeps entries of one kind together
    /// in the menu. The target's category, in the plural.
    /// </summary>
    private static string GroupName(string category) => category switch
    {
        "Document" => "Documents",
        "Image" => "Images",
        "Archive" => "Archives",
        "" => "Other",
        _ => category,
    };

    /// <param name="Version">Schema version, so the extension can refuse a file it does not understand.</param>
    /// <param name="Executable">Full path to Henkan.exe, so the extension need not resolve the package.</param>
    /// <param name="Entries">Menu entries in display order.</param>
    public sealed record MenuFileContent(int Version, string Executable, IReadOnlyList<MenuEntry> Entries);

    /// <param name="Id">Preset id, passed back as <c>--preset</c>.</param>
    /// <param name="Name">Menu label.</param>
    /// <param name="Extensions">Lower-case extensions without dots; empty means any file.</param>
    /// <param name="Folders">True when the entry accepts a folder rather than a file.</param>
    /// <param name="Group">The kind of entry, so the menu keeps each kind together.</param>
    public sealed record MenuEntry(string Id, string Name, IReadOnlyList<string> Extensions, bool Folders, string Group);
}
