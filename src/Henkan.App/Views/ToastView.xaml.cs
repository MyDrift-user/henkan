using System.Diagnostics;
using System.Numerics;
using Henkan.App.Services;
using Henkan.Core.Conversion;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Henkan.App.Views;

/// <summary>The contents of one toast: a single file and how it is getting on.</summary>
public sealed partial class ToastView : UserControl
{
    private static readonly TimeSpan Entrance = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan Exit = TimeSpan.FromMilliseconds(160);

    private ConversionJob? job;

    public ToastView()
    {
        this.InitializeComponent();
    }

    /// <summary>Raised when the user closes this toast.</summary>
    public event EventHandler? Dismissed;

    /// <summary>
    /// The job shown here. Set once, before the toast is displayed; the bindings
    /// read it when the control loads.
    /// </summary>
    public ConversionJob Job
    {
        get => this.job ?? throw new InvalidOperationException("The toast has no job yet.");
        set => this.job = value;
    }

    /// <summary>
    /// Slides the card in from the edge it is docked against.
    /// </summary>
    /// <remarks>
    /// The animation waits for the control to load. Starting it against a visual
    /// that is not in the tree yet does nothing at all, and since the animation
    /// is what makes the card visible, the card stayed blank. The content is
    /// opaque by default now, so a failure to animate costs the entrance and
    /// nothing else.
    /// </remarks>
    public void PlayEntrance()
    {
        if (!this.IsLoaded)
        {
            this.Loaded += this.OnLoadedOnce;
            return;
        }

        this.Animate();
    }

    private void OnLoadedOnce(object sender, RoutedEventArgs e)
    {
        this.Loaded -= this.OnLoadedOnce;
        this.Animate();
    }

    private void Animate()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(this.Surface);
        Compositor compositor = visual.Compositor;

        visual.Opacity = 0f;

        ScalarKeyFrameAnimation fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = Entrance;

        Vector3KeyFrameAnimation slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(24f, 0f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.1f, 0.9f),
            new Vector2(0.2f, 1f)));
        slide.Duration = Entrance;

        visual.StartAnimation(nameof(Visual.Opacity), fade);
        visual.StartAnimation(nameof(Visual.Offset), slide);
    }

    /// <summary>Fades the card out, and waits long enough for it to be seen doing so.</summary>
    public async Task PlayExitAsync()
    {
        Visual visual = ElementCompositionPreview.GetElementVisual(this.Surface);

        ScalarKeyFrameAnimation fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 0f);
        fade.Duration = Exit;

        visual.StartAnimation(nameof(Visual.Opacity), fade);

        await Task.Delay(Exit);
    }

    private void OnDismiss(object sender, RoutedEventArgs e) => this.Dismissed?.Invoke(this, EventArgs.Empty);

    private void OnShowInFolder(object sender, RoutedEventArgs e)
    {
        if (this.job is null)
        {
            return;
        }

        string? result = this.job.OutputPath is not null && File.Exists(this.job.OutputPath)
            ? this.job.OutputPath
            : null;

        string arguments = result is null
            ? $"\"{Path.GetDirectoryName(this.job.InputPath)}\""
            : $"/select,\"{result}\"";

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ErrorReport.Log(ex, "Could not open Explorer from a toast");
        }
    }
}
