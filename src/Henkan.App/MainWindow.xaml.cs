using Henkan.App.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;

namespace Henkan.App;

public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["queue"] = typeof(QueuePage),
        ["conversions"] = typeof(ConversionsPage),
        ["settings"] = typeof(SettingsPage),
    };

    public MainWindow()
    {
        this.InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(this.AppTitleBar);
        this.AppWindow.SetIcon("Assets/Henkan.ico");
        this.AppWindow.Resize(new SizeInt32(1100, 760));

        if (this.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 760;
            presenter.PreferredMinimumHeight = 520;
        }

        this.Navigation.SelectedItem = this.Navigation.MenuItems[0];
    }

    public void BringToFront()
    {
        if (this.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        this.Activate();
    }

    public void NavigateTo(string tag)
    {
        foreach (object item in this.Navigation.MenuItems.Concat(this.Navigation.FooterMenuItems))
        {
            if (item is NavigationViewItem entry && entry.Tag is string entryTag && entryTag.Equals(tag, StringComparison.OrdinalIgnoreCase))
            {
                this.Navigation.SelectedItem = entry;
                return;
            }
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag || !Pages.TryGetValue(tag, out Type? page))
        {
            return;
        }

        if (this.ContentFrame.Content?.GetType() == page)
        {
            return;
        }

        this.ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
    }
}
