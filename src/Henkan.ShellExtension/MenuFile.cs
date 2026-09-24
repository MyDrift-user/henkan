using System.Text.Json;
using System.Text.Json.Serialization;

namespace Henkan.ShellExtension;

/// <summary>
/// The file the app writes for the menu. Mirrors
/// <c>Henkan.App.Services.ShellIntegration.MenuFileContent</c>; the two are kept
/// in step by hand because the extension must not depend on the app.
/// </summary>
internal sealed class MenuFile
{
    public int Version { get; set; }

    public string Executable { get; set; } = string.Empty;

    public List<MenuEntry> Entries { get; set; } = [];

    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Henkan",
        "context-menu.json");

    /// <summary>Reads the file, or returns an empty menu when it is missing or unreadable.</summary>
    public static MenuFile Load()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return new MenuFile();
            }

            using FileStream stream = File.OpenRead(Path);
            MenuFile? loaded = JsonSerializer.Deserialize(stream, MenuFileJsonContext.Default.MenuFile);
            return loaded is { Version: 1 } ? loaded : new MenuFile();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new MenuFile();
        }
    }
}

/// <summary>One thing the user right-clicked.</summary>
/// <param name="Path">Full path on disk.</param>
/// <param name="IsDirectory">True for a folder, which only the archive entries take.</param>
internal readonly record struct ShellItem(string Path, bool IsDirectory);

internal sealed class MenuEntry
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<string> Extensions { get; set; } = [];

    /// <summary>True when this entry can be given a folder rather than a file.</summary>
    public bool Folders { get; set; }

    /// <summary>The kind of entry, so entries of one kind stand together in the menu.</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>True when every selected item is one this entry offers itself for.</summary>
    public bool AcceptsAll(IReadOnlyList<ShellItem> items)
    {
        foreach (ShellItem item in items)
        {
            if (item.IsDirectory)
            {
                if (!this.Folders)
                {
                    return false;
                }

                continue;
            }

            if (this.Extensions.Count == 0)
            {
                continue;
            }

            string extension = System.IO.Path.GetExtension(item.Path).TrimStart('.');
            bool matched = false;

            foreach (string candidate in this.Extensions)
            {
                if (candidate.Equals(extension, StringComparison.OrdinalIgnoreCase))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(MenuFile))]
internal sealed partial class MenuFileJsonContext : JsonSerializerContext;
