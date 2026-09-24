using Henkan.App.Services;
using Henkan.App.ViewModels;
using Henkan.Core.Conversion;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.Views;

/// <summary>
/// The contents of the progress window.
/// </summary>
/// <remarks>
/// A user control rather than markup on the window itself: the XAML compiler
/// cannot resolve a StaticResource converter for an x:Bind whose root is a
/// Window, and every row here needs one.
/// </remarks>
public sealed partial class BatchView : UserControl
{
    public BatchView()
    {
        this.InitializeComponent();
    }

    /// <summary>Raised when the user closes the window from the button.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// The batch is owned by the application, not by this control: a batch
    /// outlives whichever way it is being shown, and the user can change that way
    /// between one conversion and the next.
    /// </summary>
    public BatchViewModel ViewModel => App.Current.Batch;

    private void OnClose(object sender, RoutedEventArgs e) => this.CloseRequested?.Invoke(this, EventArgs.Empty);

    private async void OnShowLog(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConversionJob job })
        {
            return;
        }

        IReadOnlyList<string> lines = job.Log;
        string text = lines.Count == 0 ? "Nothing was logged." : string.Join(Environment.NewLine, lines);

        if (job.Message is { } message)
        {
            text = message + Environment.NewLine + Environment.NewLine + text;
        }

        var dialog = new ContentDialog
        {
            Title = job.InputFileName,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = text,
                    Style = (Style)Application.Current.Resources["MonospaceStyle"],
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 360,
            },
            CloseButtonText = "Close",
            XamlRoot = this.XamlRoot,
        };

        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            ErrorReport.Log(ex, "Could not show the job log");
        }
    }
}
