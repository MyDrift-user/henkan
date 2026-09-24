using Henkan.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.Services;

/// <summary>
/// One way to report a failure to the user, and one place that decides how.
/// </summary>
/// <remarks>
/// Swallowing an exception and carrying on leaves the user with a program that
/// silently does nothing, which is worse than a crash because there is nothing to
/// report. Everything caught in the app goes through here: it reaches the log,
/// and unless it is genuinely cosmetic it reaches the screen as well.
/// </remarks>
public static class ErrorReport
{
    /// <summary>Records a failure the user does not need to act on.</summary>
    public static void Log(Exception exception, string context)
    {
        ArgumentNullException.ThrowIfNull(exception);
        AppServices.Log.LogWarning(exception, "{Context}", context);
    }

    /// <summary>
    /// Shows a failure in a dialog over the given window. Safe to call from a
    /// background thread and safe to call when there is no window yet, in which
    /// case the failure is logged and nothing is shown.
    /// </summary>
    public static void Show(Window? owner, string title, string message, Exception? exception = null)
    {
        if (exception is not null)
        {
            AppServices.Log.LogError(exception, "{Title}", title);
        }
        else
        {
            AppServices.Log.LogError("{Title}: {Message}", title, message);
        }

        if (owner?.Content?.XamlRoot is not { } root)
        {
            return;
        }

        owner.DispatcherQueue.TryEnqueue(async () =>
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new ScrollViewer
                {
                    Content = new TextBlock { Text = Describe(message, exception), TextWrapping = TextWrapping.Wrap },
                    MaxHeight = 420,
                },
                CloseButtonText = "Close",
                XamlRoot = root,
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                // Another dialog is already up. The log still has the failure.
                AppServices.Log.LogWarning(ex, "Could not show the error dialog");
            }
        });
    }

    /// <summary>
    /// A failure with nowhere to show itself, during startup or on a background
    /// thread with no window. A message box is the only surface left, and it is
    /// better than exiting without a word.
    /// </summary>
    public static void ShowFatal(string title, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        AppServices.Log.LogCritical(exception, "{Title}", title);
        NativeMessageBox.Show(
            title,
            $"{exception.Message}{Environment.NewLine}{Environment.NewLine}The details are in the log, in {HenkanPaths.LogsDirectory}.");
    }

    private static string Describe(string message, Exception? exception)
    {
        if (exception is null)
        {
            return message;
        }

        // The reason in the exception's own words, without its type name; the
        // type and the stack trace are in the log for whoever needs them.
        return $"{message}{Environment.NewLine}{Environment.NewLine}{exception.Message}";
    }
}
