using Henkan.App.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Henkan.App;

/// <summary>
/// The small window a conversion started from the Explorer menu gets.
/// </summary>
/// <remarks>
/// Opening the whole application to convert one file is a poor trade: the point
/// of the context menu is that the work happens without a detour through a user
/// interface. So this window shows the progress, closes itself when everything
/// worked, and stays on screen with the reason when something did not.
/// </remarks>
public sealed partial class ProgressWindow : Window
{
    private bool closing;

    public ProgressWindow()
    {
        this.InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        this.AppWindow.SetIcon("Assets/Henkan.ico");
        this.AppWindow.Resize(new SizeInt32(580, 440));

        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.PreferredMinimumWidth = 420;
            presenter.PreferredMinimumHeight = 280;
        }

        this.ViewModel.Completed += this.OnCompleted;
        this.ViewModel.Failed += this.OnFailed;
        this.View.CloseRequested += (_, _) => this.CloseSafely();
        this.Closed += (_, _) => this.ViewModel.Detach();
    }

    /// <summary>Raised when the window has gone, so the app can decide whether to exit.</summary>
    public event EventHandler? Dismissed;

    public BatchViewModel ViewModel => this.View.ViewModel;

    public void BringToFront()
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        this.Activate();
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        // Nothing to report, so the window gets out of the way. What the user
        // asked for is already sitting in the folder they right-clicked. Unless
        // they asked it to stay, which is what the setting is for.
        if (Services.AppServices.Settings.Current.StayOpenWhenFinished)
        {
            this.DispatcherQueue.TryEnqueue(this.BringToFront);
            return;
        }

        this.CloseSafely();
    }

    private void OnFailed(object? sender, EventArgs e)
    {
        // A failure is the one case worth interrupting for. Without it the
        // conversion has simply not happened and nothing said so.
        this.DispatcherQueue.TryEnqueue(this.BringToFront);
    }

    private void CloseSafely()
    {
        if (this.closing)
        {
            return;
        }

        this.closing = true;

        this.DispatcherQueue.TryEnqueue(() =>
        {
            this.Close();
            this.Dismissed?.Invoke(this, EventArgs.Empty);
        });
    }
}
