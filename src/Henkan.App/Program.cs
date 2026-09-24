using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Henkan.App;

/// <summary>
/// Custom entry point so the app is single-instance. A second launch, which is
/// what the context menu produces while a window is already open, hands its
/// arguments to the running instance and exits, so files land in one queue
/// instead of one window each.
/// </summary>
public static class Program
{
    private const string InstanceKey = "henkan-main";

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        AppInstance main = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (!main.IsCurrent)
        {
            AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            main.RedirectActivationToAsync(activation).AsTask().GetAwaiter().GetResult();
            return 0;
        }

        main.Activated += (_, e) => App.Current.OnRedirectedActivation(e);

        Application.Start(parameters =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });

        return 0;
    }
}
