using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Henkan.App.Services;
using Henkan.Core.Settings;

namespace Henkan.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    public partial double MaxParallelConversions { get; set; }

    [ObservableProperty]
    public partial string DefaultOutputPathTemplate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int DefaultConflictPolicyIndex { get; set; }

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    [ObservableProperty]
    public partial bool StayOpenWhenFinished { get; set; }

    [ObservableProperty]
    public partial bool WriteJobLogs { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsToasts))]
    public partial int ProgressDisplayIndex { get; set; }

    [ObservableProperty]
    public partial int ToastCornerIndex { get; set; }

    [ObservableProperty]
    public partial double ProgressDelaySeconds { get; set; }

    [ObservableProperty]
    public partial double ToastLingerSeconds { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    public SettingsViewModel()
    {
        AppSettings current = AppServices.Settings.Current;
        this.MaxParallelConversions = current.MaxParallelConversions;
        this.DefaultOutputPathTemplate = current.DefaultOutputPathTemplate;
        this.DefaultConflictPolicyIndex = (int)current.DefaultConflictPolicy;
        this.ThemeIndex = ThemeLabels.ToList().FindIndex(t => t.Equals(current.Theme, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : 0;
        this.StayOpenWhenFinished = current.StayOpenWhenFinished;
        this.WriteJobLogs = current.WriteJobLogs;
        this.ProgressDisplayIndex = (int)current.ProgressDisplay;
        this.ToastCornerIndex = (int)current.ToastCorner;
        this.ProgressDelaySeconds = current.ProgressDelayMilliseconds / 1000d;
        this.ToastLingerSeconds = current.ToastLingerMilliseconds / 1000d;
    }

    public static IReadOnlyList<string> ProgressDisplayLabels { get; } =
    [
        "Stacked cards in a screen corner",
        "A floating window",
        "A Windows notification",
        "Nothing, unless it fails",
    ];

    public static IReadOnlyList<string> ToastCornerLabels { get; } =
    [
        "Top left",
        "Top right",
        "Bottom left",
        "Bottom right",
    ];

    /// <summary>The corner and linger settings only mean anything for the cards.</summary>
    public bool ShowsToasts => this.ProgressDisplayIndex == (int)ProgressDisplay.Toast;

    public static IReadOnlyList<string> ThemeLabels { get; } = ["System", "Light", "Dark"];

    public static IReadOnlyList<string> ConflictPolicyLabels => ConversionsViewModel.ConflictPolicyLabels;

    public string Version
    {
        get
        {
            string? informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return informational?.Split('+')[0] ?? "0.0.0";
        }
    }

    public string DataFolder => HenkanPaths.Root;

    public string ToolsFolder => Path.Combine(AppContext.BaseDirectory, "tools");

    public string ToolsHint => Directory.Exists(this.ToolsFolder)
        ? "Bundled tools were found next to the application."
        : "No bundled tools next to the application. Run tools\\fetch-deps.ps1 in the repository before building, install the tools and put them on PATH, or choose an executable per tool on the Conversions page.";

    public int ProcessorCount => Environment.ProcessorCount;

    [RelayCommand]
    private void Save()
    {
        AppSettings updated = AppServices.Settings.Current with
        {
            MaxParallelConversions = Math.Clamp((int)Math.Round(this.MaxParallelConversions), 1, 64),
            DefaultOutputPathTemplate = string.IsNullOrWhiteSpace(this.DefaultOutputPathTemplate)
                ? "{inputDir}\\{inputName}.{outputExt}"
                : this.DefaultOutputPathTemplate.Trim(),
            DefaultConflictPolicy = (FileConflictPolicy)this.DefaultConflictPolicyIndex,
            Theme = ThemeLabels[Math.Clamp(this.ThemeIndex, 0, ThemeLabels.Count - 1)],
            StayOpenWhenFinished = this.StayOpenWhenFinished,
            WriteJobLogs = this.WriteJobLogs,
            ProgressDisplay = (ProgressDisplay)Math.Clamp(this.ProgressDisplayIndex, 0, ProgressDisplayLabels.Count - 1),
            ToastCorner = (ScreenCorner)Math.Clamp(this.ToastCornerIndex, 0, ToastCornerLabels.Count - 1),
            ProgressDelayMilliseconds = (int)Math.Round(Math.Clamp(this.ProgressDelaySeconds, 0d, 10d) * 1000),
            ToastLingerMilliseconds = (int)Math.Round(Math.Clamp(this.ToastLingerSeconds, 0.5d, 60d) * 1000),
        };

        try
        {
            AppServices.SaveSettings(updated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.StatusMessage = $"Settings could not be saved: {ex.Message}";
            ErrorReport.Show(App.Current.Window, "Settings not saved", "The settings file could not be written.", ex);
            return;
        }

        App.Current.ApplyTheme(updated.Theme);
        this.StatusMessage = "Saved.";
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{HenkanPaths.Root}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            this.StatusMessage = $"Could not open the folder: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenRepository() =>
        Process.Start(new ProcessStartInfo("https://github.com/MyDrift-user/henkan") { UseShellExecute = true });
}
