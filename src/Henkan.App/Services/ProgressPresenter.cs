using Henkan.App.ViewModels;
using Henkan.Core.Conversion;
using Henkan.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Henkan.App.Services;

/// <summary>
/// Shows a running batch somewhere. Which somewhere is the user's choice.
/// </summary>
public interface IProgressPresenter : IDisposable
{
    /// <summary>Puts whatever this presenter uses on screen.</summary>
    void Show();

    /// <summary>Brings it forward, which only a failure is worth doing.</summary>
    void Raise();
}

/// <summary>
/// Stacked cards in a corner of the screen, one per file.
/// </summary>
/// <remarks>
/// The cards are positioned from the bottom or top of the work area depending on
/// the chosen corner, so the stack grows away from the screen edge and the newest
/// card never pushes an older one off. A finished card lingers briefly and then
/// removes itself; a failed one stays until it is dismissed, because it is the
/// only thing telling the user the conversion did not happen.
/// </remarks>
public sealed class ToastPresenter : IProgressPresenter
{
    private const int Gap = 8;
    private const int Margin = 16;

    private readonly BatchViewModel batch;
    private readonly AppSettings settings;
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly List<ToastWindow> toasts = [];
    private bool disposed;

    public ToastPresenter(BatchViewModel batch, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(settings);

        this.batch = batch;
        this.settings = settings;
    }

    /// <summary>Raised once the last card has gone and there is nothing left on screen.</summary>
    public event EventHandler? Emptied;

    public void Show()
    {
        foreach (ConversionJob job in this.batch.Jobs)
        {
            this.Add(job);
        }

        this.batch.JobAdded += this.OnJobAdded;
        this.batch.JobEnded += this.OnJobEnded;
    }

    public void Raise()
    {
        foreach (ToastWindow toast in this.toasts)
        {
            toast.ShowQuietly();
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.batch.JobAdded -= this.OnJobAdded;
        this.batch.JobEnded -= this.OnJobEnded;

        foreach (ToastWindow toast in this.toasts.ToList())
        {
            toast.Dismiss();
        }

        this.toasts.Clear();
    }

    private void OnJobAdded(object? sender, ConversionJob job) => this.dispatcher.TryEnqueue(() => this.Add(job));

    private void OnJobEnded(object? sender, ConversionJob job)
    {
        // A failure stays put. Anything else has said all it has to say, so it
        // goes of its own accord after long enough to have been noticed.
        if (job.State is ConversionState.Failed)
        {
            this.dispatcher.TryEnqueue(() => this.Find(job)?.ShowQuietly());
            return;
        }

        this.Expire(job);
    }

    private void Add(ConversionJob job)
    {
        if (this.disposed || this.Find(job) is not null)
        {
            return;
        }

        var toast = new ToastWindow(job, App.Current.ResolvedTheme);
        toast.Resized += (_, _) => this.Reflow();
        toast.Dismissed += (_, _) =>
        {
            this.toasts.Remove(toast);
            this.Reflow();

            if (this.toasts.Count == 0 && !this.disposed)
            {
                this.Emptied?.Invoke(this, EventArgs.Empty);
            }
        };

        this.toasts.Add(toast);
        this.Reflow();
        toast.ShowQuietly();

        if (job.IsFinished && job.State is not ConversionState.Failed)
        {
            this.Expire(job);
        }
    }

    private void Expire(ConversionJob job)
    {
        if (this.settings.StayOpenWhenFinished)
        {
            return;
        }

        _ = Task.Delay(Math.Max(0, this.settings.ToastLingerMilliseconds))
            .ContinueWith(
                _ => this.dispatcher.TryEnqueue(() => this.Find(job)?.Dismiss()),
                TaskScheduler.Default);
    }

    private ToastWindow? Find(ConversionJob job) => this.toasts.Find(t => t.Job == job);

    /// <summary>Lays the stack out again after something was added or removed.</summary>
    private void Reflow()
    {
        if (this.toasts.Count == 0)
        {
            return;
        }

        DisplayArea area = DisplayArea.GetFromWindowId(this.toasts[0].Id, DisplayAreaFallback.Primary);
        Windows.Graphics.RectInt32 work = area.WorkArea;

        bool right = this.settings.ToastCorner is ScreenCorner.TopRight or ScreenCorner.BottomRight;
        bool bottom = this.settings.ToastCorner is ScreenCorner.BottomLeft or ScreenCorner.BottomRight;

        int offset = 0;

        foreach (ToastWindow toast in this.toasts)
        {
            double scale = toast.Scale;
            int margin = (int)Math.Round(Margin * scale);
            int gap = (int)Math.Round(Gap * scale);

            int x = right
                ? work.X + work.Width - toast.Width - margin
                : work.X + margin;

            int y = bottom
                ? work.Y + work.Height - toast.Height - margin - offset
                : work.Y + margin + offset;

            toast.MoveTo(x, y);
            offset += toast.Height + gap;
        }
    }
}

/// <summary>The floating window listing the whole batch.</summary>
public sealed class WindowPresenter(ProgressWindow window) : IProgressPresenter
{
    public void Show() => window.Activate();

    public void Raise() => window.BringToFront();

    public void Dispose() => window.Close();
}

/// <summary>
/// Shows nothing while a batch runs.
/// </summary>
/// <remarks>
/// Silence is only ever right for a conversion that worked. A failure still has
/// to reach the user, so the caller falls back to a window for that; this simply
/// has nothing to put on screen.
/// </remarks>
public sealed class SilentPresenter : IProgressPresenter
{
    public void Show()
    {
    }

    public void Raise()
    {
    }

    public void Dispose()
    {
    }
}
