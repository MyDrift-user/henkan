using Henkan.Core.Backends;
using Henkan.Core.Conversion;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Henkan.App.Services;

/// <summary>
/// The application's long-lived objects. Deliberately a static composition root
/// rather than a container: there are five services and one lifetime.
/// </summary>
public static class AppServices
{
    // A conversion started from the Explorer menu has no console and, when it
    // works, no window either. The file is the only place a diagnosis can land.
    private static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
        builder.AddDebug().AddProvider(new FileLoggerProvider()).SetMinimumLevel(LogLevel.Debug));

    public static ILogger Log { get; } = LoggerFactory.CreateLogger("Henkan");

    public static SettingsStore Settings { get; } = new();

    public static PresetStore Presets { get; } = new();

    public static ToolLocator Locator { get; } = new(backendId =>
        Settings.Current.ToolPaths.TryGetValue(backendId, out string? path) ? path : null);

    public static BackendRegistry Registry { get; } = new(Locator, HenkanPaths.BackendsDirectory);

    public static ConversionQueue Queue { get; private set; } = null!;

    /// <summary>Raised on the UI thread after backends were (re)loaded and probed.</summary>
    public static event EventHandler? BackendsChanged;

    public static bool IsReady { get; private set; }

    public static async Task InitializeAsync()
    {
        HenkanPaths.EnsureCreated();
        HenkanPaths.CleanTemporaryDirectory();

        Settings.Load();
        if (Settings.LastError is not null)
        {
            Log.LogWarning("{Error}", Settings.LastError);
        }

        Presets.Load();
        if (Presets.LastError is not null)
        {
            Log.LogWarning("{Error}", Presets.LastError);
        }
        else if (Settings.Current.StarterPresetsVersion < PresetStore.StarterVersion)
        {
            // New conversions are offered in the menu once, when the version that
            // brings them first starts.
            int added = Presets.AddNewStarters(Settings.Current.StarterPresetsVersion);
            Settings.Save(Settings.Current with { StarterPresetsVersion = PresetStore.StarterVersion });
            Log.LogInformation("Added {Count} new menu entries from starter set {Version}", added, PresetStore.StarterVersion);
        }

        Queue = new ConversionQueue(Registry, Settings.Current.MaxParallelConversions);
        Queue.JobFinished += (_, job) => RecordJob(job);

        await ReloadBackendsAsync();
        IsReady = true;

        Log.LogInformation(
            "Ready. {Presets} presets, {Backends} backends, arguments: {Arguments}",
            Presets.Presets.Count,
            Registry.Backends.Count,
            string.Join(" ", Environment.GetCommandLineArgs().Skip(1)));
    }

    public static async Task ReloadBackendsAsync()
    {
        await Registry.ReloadAsync();

        foreach (BackendLoadProblem problem in Registry.Problems)
        {
            Log.LogWarning("{Source}: {Message}", problem.Source, problem.Message);
        }

        foreach (IConversionBackend backend in Registry.Backends)
        {
            Log.LogInformation(
                "{Backend}: {State} {Detail}",
                backend.Definition.Name,
                backend.Availability.IsAvailable ? "available" : "unavailable",
                backend.Availability.IsAvailable ? backend.Availability.Version : backend.Availability.Reason);
        }

        // The context menu reads its entries from a file the app maintains, and
        // what belongs in it depends on which backends just probed successfully.
        // Republishing after every reload keeps it right after an upgrade, a hand
        // edit of presets.json, or a tool appearing or disappearing.
        ShellIntegration.PublishPresets(Presets.Presets, Registry);

        BackendsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Records how a job ended, and its tool output when the setting asks for it.
    /// </summary>
    /// <remarks>
    /// The outcome line is always written: a conversion that quietly produced
    /// nothing is the failure that is hardest to report and hardest to diagnose,
    /// and one line per job is a small price for being able to answer it.
    /// </remarks>
    private static void RecordJob(ConversionJob job)
    {
        Log.LogInformation(
            "{State}: {Input} -> {Target} {Output} {Message}",
            job.State,
            job.InputPath,
            job.Target.Key,
            job.OutputPath ?? "(no output path)",
            job.Message ?? string.Empty);

        if (!Settings.Current.WriteJobLogs)
        {
            return;
        }

        try
        {
            string directory = Path.Combine(HenkanPaths.LogsDirectory, "jobs");
            Directory.CreateDirectory(directory);

            string name = $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileNameWithoutExtension(job.InputPath)}.log";
            File.WriteAllLines(
                Path.Combine(directory, SanitiseFileName(name)),
                [
                    $"{job.InputPath} -> {job.Target.Key}",
                    $"output: {job.OutputPath ?? "(none)"}",
                    $"state:  {job.State} {job.Message}",
                    string.Empty,
                    .. job.Log,
                ]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.LogWarning(ex, "Could not write the job log");
        }
    }

    private static string SanitiseFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public static void SaveSettings(AppSettings settings)
    {
        Settings.Save(settings);
        Queue.SetParallelism(settings.MaxParallelConversions);
    }
}
