using System.Text.Json;

namespace Henkan.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>.
/// </summary>
/// <remarks>
/// A corrupt or unreadable file falls back to defaults rather than stopping the
/// application, and the bad file is kept with a .broken suffix so nothing the
/// user configured is thrown away silently.
/// </remarks>
public sealed class SettingsStore
{
    private readonly string path;

    public SettingsStore(string? path = null)
    {
        this.path = path ?? HenkanPaths.SettingsFile;
    }

    public AppSettings Current { get; private set; } = new();

    public string? LastError { get; private set; }

    public AppSettings Load()
    {
        this.LastError = null;

        if (!File.Exists(this.path))
        {
            return this.Current = new AppSettings();
        }

        try
        {
            string json = File.ReadAllText(this.path);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonDefaults.Options);
            return this.Current = loaded ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            this.LastError = $"Settings could not be read ({ex.Message}). Defaults are in use.";
            this.PreserveBrokenFile();
            return this.Current = new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);

        string temporary = this.path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonDefaults.Options));
        File.Move(temporary, this.path, overwrite: true);

        this.Current = settings;
    }

    private void PreserveBrokenFile()
    {
        try
        {
            File.Move(this.path, this.path + ".broken", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; defaults are already in place.
        }
    }
}
