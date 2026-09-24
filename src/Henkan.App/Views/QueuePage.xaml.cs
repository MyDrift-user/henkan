using Henkan.App.Services;
using Henkan.App.ViewModels;
using Henkan.Core.Conversion;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.Views;

public sealed partial class QueuePage : Page
{
    public QueuePage()
    {
        this.InitializeComponent();
    }

    public QueueViewModel ViewModel { get; } = new();

    private async void OnShowLog(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConversionJob job })
        {
            return;
        }

        IReadOnlyList<string> lines = job.Log;

        var dialog = new ContentDialog
        {
            Title = job.InputFileName,
            Content = new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = lines.Count == 0 ? "Nothing was logged." : string.Join(Environment.NewLine, lines),
                    Style = (Style)Application.Current.Resources["MonospaceStyle"],
                    IsTextSelectionEnabled = true,
                },
                MaxHeight = 480,
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
