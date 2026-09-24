using System.Text.Json;
using System.Text.Json.Serialization;

namespace Henkan.Core.Backends;

/// <summary>
/// Reads and writes backend definition files. Built-in definitions are embedded
/// resources and user definitions are loose files, but both use this one shape,
/// so "duplicate a built-in and edit it" is a complete customisation story.
/// </summary>
public static class BackendSerializer
{
    /// <summary>
    /// Lenient on read, tidy on write. Comments and trailing commas are tolerated
    /// because these files are meant to be edited by hand as well as by the app.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static BackendDefinition Deserialize(string json, string? sourcePath = null)
    {
        ArgumentNullException.ThrowIfNull(json);

        BackendDefinition definition = JsonSerializer.Deserialize<BackendDefinition>(json, Options)
            ?? throw new JsonException("The backend definition was empty.");

        return sourcePath is null ? definition : definition with { SourcePath = sourcePath };
    }

    public static string Serialize(BackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        // Neither of these belongs in the file: one is a runtime flag, the other
        // is where the file happens to live.
        return JsonSerializer.Serialize(definition with { IsBuiltIn = false, SourcePath = null }, Options);
    }

    public static async Task<BackendDefinition> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Deserialize(json, path);
    }

    public static async Task SaveAsync(BackendDefinition definition, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Write beside the target and move into place so an interrupted save
        // cannot leave a half-written definition behind.
        string temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, Serialize(definition), cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }
}
