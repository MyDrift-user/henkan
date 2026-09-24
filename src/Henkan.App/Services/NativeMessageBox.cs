using System.Runtime.InteropServices;

namespace Henkan.App.Services;

/// <summary>
/// The last resort for showing a failure: a plain Win32 message box, which works
/// before XAML is up and after it has fallen over.
/// </summary>
internal static class NativeMessageBox
{
    private const uint IconError = 0x00000010;
    private const uint TopMost = 0x00040000;

    public static void Show(string title, string text)
    {
        try
        {
            // The box is modal to its own thread, so a very long stack trace is
            // trimmed rather than producing a dialog taller than the screen.
            MessageBoxW(0, Trim(text), title, IconError | TopMost);
        }
        catch (DllNotFoundException)
        {
            // Nothing left to try.
        }
    }

    private static string Trim(string text) =>
        text.Length <= 2000 ? text : text[..2000] + Environment.NewLine + "...";

    // DllImport rather than LibraryImport: the generated marshalling code needs
    // the project compiled unsafe, and one message box is not worth that.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(nint owner, string text, string caption, uint type);
}
