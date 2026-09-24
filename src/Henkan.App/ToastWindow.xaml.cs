using System.ComponentModel;
using System.Runtime.InteropServices;
using Henkan.Core.Conversion;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Henkan.App;

/// <summary>
/// One small always-on-top card showing a single conversion.
/// </summary>
/// <remarks>
/// A real window rather than a Windows notification, because a notification
/// cannot show several conversions at once in a way anyone can follow, and cannot
/// be placed where the user wants it. The window itself is the card: acrylic
/// backdrop, rounded corners and a shadow all come from the compositor, so
/// nothing is drawn twice and there is no square edge around a round one.
/// </remarks>
public sealed partial class ToastWindow : Window
{
    /// <summary>Logical width of a card. The physical size follows the display scaling.</summary>
    public const int LogicalWidth = 372;

    private const int RestingHeight = 76;
    private const int FailedHeight = 116;

    private const uint ShowNoActivate = 0x0008;
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int CornerPreference = 33;
    private const int RoundedCorners = 2;
    private const int GwlStyle = -16;
    private const int WsPopup = unchecked((int)0x80000000);
    private const int WsVisible = 0x10000000;
    private const int WsOverlappedWindow = 0x00CF0000;

    private readonly nint handle;
    private int logicalHeight = RestingHeight;
    private bool closed;

    public ToastWindow(ConversionJob job, ElementTheme theme)
    {
        ArgumentNullException.ThrowIfNull(job);

        this.InitializeComponent();

        this.Job = job;
        this.View.Job = job;
        this.View.RequestedTheme = theme;
        this.View.Dismissed += (_, _) => this.Dismiss();

        this.handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        this.Id = Win32Interop.GetWindowIdFromWindow(this.handle);

        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        this.AppWindow.IsShownInSwitchers = false;

        // A toast that steals focus mid-typing is worse than no toast, and one in
        // the task bar is clutter. Both are window styles rather than presenter
        // settings, so they are set directly.
        SetWindowLong(this.handle, GwlExStyle, GetWindowLong(this.handle, GwlExStyle) | WsExNoActivate | WsExToolWindow);

        this.ApplyFrame();

        this.Scale = GetDpiForWindow(this.handle) / 96d;
        this.AppWindow.Resize(new SizeInt32(this.Width, this.Height));

        this.Job.PropertyChanged += this.OnJobChanged;
        this.Closed += (_, _) => this.Job.PropertyChanged -= this.OnJobChanged;
    }

    /// <summary>Raised when the toast has gone, so the stack can close the gap.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Raised when the card needs more room, so the stack can lay out again.</summary>
    public event EventHandler? Resized;

    public ConversionJob Job { get; }

    public WindowId Id { get; }

    public double Scale { get; }

    public int Width => (int)Math.Round(LogicalWidth * this.Scale);

    public int Height => (int)Math.Round(this.logicalHeight * this.Scale);

    /// <summary>Shows the toast without taking focus from whatever the user is doing.</summary>
    public void ShowQuietly()
    {
        ShowWindow(this.handle, ShowNoActivate);

        // Applied again now the window exists on screen. DWM ignores some frame
        // attributes on a window it has not composed yet, and the system border
        // reappearing around a rounded card is very visible.
        this.ApplyFrame();

        this.View.PlayEntrance();
    }

    /// <summary>
    /// Rounds the corners and removes the system border.
    /// </summary>
    /// <remarks>
    /// A borderless window is square and shadowless unless it asks not to be, and
    /// it still gets the frame border drawn around it, which on a small card reads
    /// as a hard outline rather than an edge.
    /// </remarks>
    private void ApplyFrame()
    {
        // Colouring the frame border was not enough to stop it reading as an
        // outline drawn around the card, so the window simply stops having one:
        // a plain popup has no frame for the compositor to draw. The corners are
        // still rounded, and a rounded window still gets its shadow.
        nint style = GetWindowLong(this.handle, GwlStyle);
        SetWindowLong(this.handle, GwlStyle, (style & ~WsOverlappedWindow) | WsPopup | WsVisible);

        int rounded = RoundedCorners;
        DwmSetWindowAttribute(this.handle, CornerPreference, ref rounded, sizeof(int));
    }

    public void MoveTo(int x, int y) => this.AppWindow.Move(new PointInt32(x, y));

    public void Dismiss()
    {
        if (this.closed)
        {
            return;
        }

        this.closed = true;

        this.DispatcherQueue.TryEnqueue(async () =>
        {
            // Fading out first is the difference between a card leaving and a
            // card blinking out of existence.
            await this.View.PlayExitAsync();

            this.Close();
            this.Dismissed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnJobChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ConversionJob.State))
        {
            return;
        }

        this.DispatcherQueue.TryEnqueue(() =>
        {
            // A failure has something to say and needs the room to say it.
            int wanted = this.Job.State is ConversionState.Failed ? FailedHeight : RestingHeight;

            if (wanted == this.logicalHeight || this.closed)
            {
                return;
            }

            this.logicalHeight = wanted;
            this.AppWindow.Resize(new SizeInt32(this.Width, this.Height));
            this.Resized?.Invoke(this, EventArgs.Empty);
        });
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint window, uint command);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLong(nint window, int index, nint value);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
