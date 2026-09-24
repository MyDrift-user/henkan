using Henkan.App.Services;
using Henkan.App.ViewModels;
using Henkan.Core.Conversion;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Henkan.App;

public partial class App : Application
{
    private MainWindow? window;
    private ProgressWindow? progress;
    private IProgressPresenter? presenter;
    private BatchViewModel? batch;
    private DispatcherQueue? ui;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += OnUnhandledException;

        // Windows come and go here: a toast, a progress window, the main window,
        // or none at all while a conversion runs. Letting the framework decide to
        // exit on the last one would end a batch that is still working, so the
        // decision is made in one place instead.
        this.DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    public static new App Current => (App)Application.Current;

    public MainWindow? Window => this.window;

    /// <summary>
    /// The conversions started from the Explorer menu in this session. Created on
    /// demand and owned here rather than by a window, because how a batch is shown
    /// is a setting and the batch has to outlive any one answer to it.
    /// </summary>
    public BatchViewModel Batch => this.batch ??= new BatchViewModel();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Captured once, here, because a redirected activation has to reach this
        // thread whether or not a window happens to exist at the time. Toast mode
        // has no window at all, and a second right click was being dropped.
        this.ui = DispatcherQueue.GetForCurrentThread();

        try
        {
            await AppServices.InitializeAsync();
        }
        catch (Exception ex)
        {
            // Nothing works without the settings, presets and backends, and a
            // window that cannot convert anything is not worth showing.
            ErrorReport.ShowFatal("Henkan could not start", ex);
            this.Exit();
            return;
        }

        LaunchArguments launch = LaunchArguments.Parse(Environment.GetCommandLineArgs().Skip(1));
        this.HandleLaunch(launch);
    }

    /// <summary>Called for activations redirected from other instances.</summary>
    public void OnRedirectedActivation(AppActivationArguments arguments)
    {
        LaunchArguments launch = LaunchArguments.FromActivation(arguments);

        DispatcherQueue? dispatcher = this.ui ?? this.window?.DispatcherQueue ?? this.progress?.DispatcherQueue;

        if (dispatcher is null || !dispatcher.TryEnqueue(() => this.HandleLaunch(launch)))
        {
            AppServices.Log.LogWarning("An activation arrived with nowhere to run it: {Files} files.", launch.Files.Count);
        }
    }

    /// <summary>The chosen theme, for windows created outside the main tree.</summary>
    public ElementTheme ResolvedTheme => AppServices.Settings.Current.Theme.ToLowerInvariant() switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Applies "System", "Light" or "Dark" to the window's content tree.</summary>
    public void ApplyTheme(string theme)
    {
        ElementTheme resolved = theme.ToLowerInvariant() switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        if (this.window?.Content is FrameworkElement main)
        {
            main.RequestedTheme = resolved;
        }

        if (this.progress?.Content is FrameworkElement batchWindow)
        {
            batchWindow.RequestedTheme = resolved;
        }
    }

    /// <summary>
    /// Decides what a launch means.
    /// </summary>
    /// <remarks>
    /// A conversion asked for from the Explorer menu is reported however the user
    /// chose, and the whole application is not opened for it. Opening it would be
    /// a detour through a user interface they deliberately avoided by using the
    /// context menu in the first place.
    /// </remarks>
    private void HandleLaunch(LaunchArguments launch)
    {
        if (launch is { Run: true, PresetId: not null, HasWork: true })
        {
            this.RunBatch(launch);
            return;
        }

        if (launch.HasWork)
        {
            // Files with no preset to run them through. Nothing in the
            // application takes a dropped file, so they are noted and left.
            AppServices.Log.LogInformation(
                "Opened with {Count} file(s) but no preset; showing the window instead.",
                launch.Files.Count);
        }

        this.ShowMainWindow();
    }

    private void RunBatch(LaunchArguments launch)
    {
        BatchViewModel batchModel = this.Batch;

        try
        {
            Preset? preset = AppServices.Presets.Find(launch.PresetId!);

            if (preset is null)
            {
                batchModel.AddProblem(
                    "That menu entry no longer exists. Open Henkan and check the Conversions page, then try again.");
            }
            else
            {
                IReadOnlyList<ConversionJob> jobs = AppServices.Queue.Enqueue(launch.Files, preset);
                batchModel.Add(jobs);
            }
        }
        catch (Exception ex)
        {
            ErrorReport.Log(ex, "Could not start the conversion");
            batchModel.AddProblem(ex.Message);
        }

        this.ShowProgressWhenItIsWorthIt();
    }

    /// <summary>
    /// Waits a moment before showing anything.
    /// </summary>
    /// <remarks>
    /// Converting one small image takes a fraction of a second. Anything that
    /// appears and disappears again in that time is a flash on the screen that
    /// cannot be read and was never worth drawing, so a batch that finishes
    /// inside the delay is never shown at all. A failure skips the wait, because
    /// a failure is the one outcome the user has to be told about.
    /// </remarks>
    private void ShowProgressWhenItIsWorthIt()
    {
        if (this.presenter is not null)
        {
            this.presenter.Raise();
            return;
        }

        if (this.Batch.HasFailures)
        {
            this.ShowProgress();
            return;
        }

        int delay = Math.Max(0, AppServices.Settings.Current.ProgressDelayMilliseconds);
        DispatcherQueue dispatcher = this.ui ?? DispatcherQueue.GetForCurrentThread();

        _ = Task.Delay(delay).ContinueWith(
            _ => dispatcher.TryEnqueue(() =>
            {
                if (this.presenter is not null)
                {
                    return;
                }

                // A finished batch still gets its cards: one that appears once
                // and stays for its full time is a confirmation, not a flash, and
                // a conversion that leaves no trace reads as one that did not
                // happen. A window or a notification really would appear only to
                // close again, so those stay hidden.
                bool worthShowing = !this.Batch.IsFinished
                    || this.Batch.HasFailures
                    || AppServices.Settings.Current.ProgressDisplay is ProgressDisplay.Toast;

                if (worthShowing)
                {
                    this.ShowProgress();
                }
                else
                {
                    this.ExitIfNothingLeft();
                }
            }),
            TaskScheduler.Default);
    }

    private void ShowProgress()
    {
        ProgressDisplay display = AppServices.Settings.Current.ProgressDisplay;

        // Silence is only ever right for a conversion that worked. Once one has
        // failed there is something to say, and a window is the way to say it.
        if (display is ProgressDisplay.Silent && this.Batch.HasFailures)
        {
            display = ProgressDisplay.Window;
        }

        if (display is ProgressDisplay.Notification && !NotificationPresenter.IsAvailable)
        {
            display = ProgressDisplay.Window;
        }

        if (display is ProgressDisplay.Toast)
        {
            var toasts = new ToastPresenter(this.Batch, AppServices.Settings.Current);

            // The cards fade out one at a time; the process is only finished once
            // the last of them has gone.
            toasts.Emptied += (_, _) => this.DismissProgress();
            this.presenter = toasts;
        }
        else
        {
            this.presenter = display switch
            {
                ProgressDisplay.Notification => new NotificationPresenter(this.Batch),
                ProgressDisplay.Silent => new SilentPresenter(),
                _ => new WindowPresenter(this.CreateProgressWindow()),
            };
        }

        this.Batch.Completed += this.OnBatchCompleted;
        this.Batch.Failed += this.OnBatchFailed;

        AppServices.Log.LogInformation("Showing progress as {Display}.", display);
        this.presenter.Show();
    }

    private void OnBatchCompleted(object? sender, EventArgs e)
    {
        if (AppServices.Settings.Current.StayOpenWhenFinished)
        {
            this.presenter?.Raise();
            return;
        }

        // The toasts fade out one at a time on their own, so the presenter is
        // left to finish that rather than being torn down underneath them.
        if (this.presenter is ToastPresenter)
        {
            return;
        }

        this.DismissProgress();
    }

    private void OnBatchFailed(object? sender, EventArgs e) => this.presenter?.Raise();

    private void DismissProgress()
    {
        IProgressPresenter? current = this.presenter;
        this.presenter = null;

        if (current is not null)
        {
            this.Batch.Completed -= this.OnBatchCompleted;
            this.Batch.Failed -= this.OnBatchFailed;
            current.Dispose();
        }

        this.ExitIfNothingLeft();
    }

    private ProgressWindow CreateProgressWindow()
    {
        var batchWindow = new ProgressWindow();
        this.progress = batchWindow;

        batchWindow.Closed += (_, _) =>
        {
            this.progress = null;
            this.ExitIfNothingLeft();
        };

        batchWindow.Dismissed += (_, _) => this.DismissProgress();

        this.ApplyTheme(AppServices.Settings.Current.Theme);
        return batchWindow;
    }

    private void ShowMainWindow()
    {
        if (this.window is not null)
        {
            this.window.BringToFront();
            return;
        }

        this.window = new MainWindow();
        this.window.Closed += (_, _) =>
        {
            this.window = null;
            this.ExitIfNothingLeft();
        };

        this.ApplyTheme(AppServices.Settings.Current.Theme);
        this.window.Activate();
    }

    /// <summary>
    /// A conversion run from the context menu leaves nothing behind when it
    /// succeeds, and a process with no window and no work is just a background
    /// process nobody asked for.
    /// </summary>
    private void ExitIfNothingLeft()
    {
        if (this.window is not null || this.progress is not null || AppServices.Queue.HasActiveJobs)
        {
            return;
        }

        this.Exit();

        // Application.Exit ends the message loop, and a process that never opened
        // a window does not have one to end. Silent and notification runs are
        // exactly that case, so they would linger invisibly for ever. Everything
        // is finished and written by this point, so leaving is safe.
        DispatcherQueue dispatcher = this.ui ?? DispatcherQueue.GetForCurrentThread();

        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(
            _ => dispatcher.TryEnqueue(() =>
            {
                if (this.window is null && this.progress is null && !AppServices.Queue.HasActiveJobs)
                {
                    AppServices.Log.LogInformation("Nothing left to do and no window to close; exiting.");
                    Environment.Exit(0);
                }
            }),
            TaskScheduler.Default);
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Letting this through kills the process and takes any running
        // conversion with it, so the exception is reported and the app carries
        // on. A bug that is visible gets fixed; a silent one does not.
        e.Handled = true;

        ErrorReport.Show(
            (Window?)Current.window ?? Current.progress,
            "Something went wrong",
            "Henkan hit an error it did not expect. The conversion queue is unaffected.",
            e.Exception);
    }
}
