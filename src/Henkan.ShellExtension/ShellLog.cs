namespace Henkan.ShellExtension;

/// <summary>
/// Opt-in trace for the handler. Nothing is written unless the marker file
/// <c>%LocalAppData%\Henkan\shell-debug</c> exists, because this code runs on
/// every right click in Explorer. With the marker present, each call lands in
/// <c>%LocalAppData%\Henkan\logs\shell.log</c>.
/// </summary>
internal static class ShellLog
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Henkan");

    private static readonly bool Enabled = File.Exists(Path.Combine(Root, "shell-debug"));

    private static readonly string LogPath = Path.Combine(Root, "logs", "shell.log");

    private static readonly Lock Gate = new();

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tracing must never take the menu down.
        }
    }
}
