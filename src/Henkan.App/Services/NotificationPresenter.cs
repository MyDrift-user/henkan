using Henkan.App.ViewModels;
using Henkan.Core.Conversion;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Henkan.App.Services;

/// <summary>
/// A Windows notification with a progress bar, updated as the batch runs.
/// </summary>
/// <remarks>
/// One notification for the whole batch rather than one per file: the action
/// centre is shared with every other application on the machine, and filling it
/// with a row per converted file is not a courtesy. The toast presenter is the
/// one that shows each file separately, where the cards are Henkan's own space.
/// </remarks>
public sealed class NotificationPresenter : IProgressPresenter
{
    private const string Tag = "batch";
    private const string Group = "henkan";

    private static bool registered;

    private readonly BatchViewModel batch;
    private uint sequence = 1;
    private bool shown;
    private bool disposed;

    public NotificationPresenter(BatchViewModel batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        this.batch = batch;
    }

    /// <summary>True when Windows accepted the registration and notifications can be shown.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (registered)
            {
                return true;
            }

            try
            {
                AppNotificationManager.Default.Register();
                registered = true;
            }
            catch (Exception ex)
            {
                // Unregistered, unpackaged, or notifications turned off for the
                // app. Either way the caller falls back to a window.
                ErrorReport.Log(ex, "Windows notifications are not available");
            }

            return registered;
        }
    }

    public void Show()
    {
        if (!IsAvailable)
        {
            return;
        }

        this.batch.PropertyChanged += this.OnBatchChanged;
        this.Publish();
    }

    public void Raise() => this.Publish();

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.batch.PropertyChanged -= this.OnBatchChanged;

        // A finished notification is left in the action centre on purpose: it is
        // the record that the conversion happened, and the only one a user who
        // was looking elsewhere will ever see.
    }

    private void OnBatchChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BatchViewModel.Progress)
            or nameof(BatchViewModel.Summary)
            or nameof(BatchViewModel.IsRunning))
        {
            this.Publish();
        }
    }

    private void Publish()
    {
        if (!IsAvailable || this.disposed)
        {
            return;
        }

        try
        {
            if (!this.shown)
            {
                this.ShowFirst();
                return;
            }

            _ = AppNotificationManager.Default.UpdateAsync(this.BuildProgress(), Tag, Group);
        }
        catch (Exception ex)
        {
            ErrorReport.Log(ex, "Could not update the notification");
        }
    }

    private void ShowFirst()
    {
        AppNotificationBuilder builder = new AppNotificationBuilder()
            .AddText("Henkan")
            .AddProgressBar(new AppNotificationProgressBar()
                .BindTitle()
                .BindValue()
                .BindValueStringOverride()
                .BindStatus());

        AppNotification notification = builder.BuildNotification();
        notification.Tag = Tag;
        notification.Group = Group;
        notification.Progress = this.BuildProgress();

        AppNotificationManager.Default.Show(notification);
        this.shown = true;
    }

    private AppNotificationProgressData BuildProgress()
    {
        int finished = this.batch.Jobs.Count(j => j.IsFinished);
        int total = Math.Max(1, this.batch.Jobs.Count);

        return new AppNotificationProgressData(this.sequence++)
        {
            Title = this.batch.Title,
            Value = Math.Clamp(this.batch.Progress, 0d, 1d),
            ValueStringOverride = this.batch.IsRunning ? $"{finished} of {total}" : string.Empty,
            Status = this.batch.Summary.Length > 0 ? this.batch.Summary : "Working",
        };
    }
}
