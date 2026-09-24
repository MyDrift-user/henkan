using Henkan.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        this.InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; } = new();
}
