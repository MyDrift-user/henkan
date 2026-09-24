using System.Text;
using Microsoft.Extensions.Logging;

namespace Henkan.Core.Settings;

/// <summary>
/// Writes the application log to a file.
/// </summary>
/// <remarks>
/// A conversion started from the Explorer menu runs in a process with no console
/// and, when it works, no window either. Without a file there is nowhere for a
/// diagnosis to land: the process starts, something goes wrong, the process ends,
/// and all anyone can say is that nothing happened.
/// </remarks>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const long MaximumBytes = 2 * 1024 * 1024;

    private readonly string path;
    private readonly Lock gate = new();

    public FileLoggerProvider(string? path = null)
    {
        this.path = path ?? Path.Combine(HenkanPaths.LogsDirectory, "app.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(string line)
    {
        lock (this.gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);

                // One roll, not a rotation scheme. Keeping the previous file is
                // enough to still have yesterday's failure after today's run.
                if (File.Exists(this.path) && new FileInfo(this.path).Length > MaximumBytes)
                {
                    File.Move(this.path, this.path + ".1", overwrite: true);
                }

                File.AppendAllText(this.path, line, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never be the thing that breaks a conversion.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            if (!this.IsEnabled(logLevel))
            {
                return;
            }

            var text = new StringBuilder();
            text.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            text.Append(" [").Append(Environment.ProcessId).Append("] ");
            text.Append(Short(logLevel)).Append(' ');
            text.Append(category).Append(": ");
            text.AppendLine(formatter(state, exception));

            if (exception is not null)
            {
                text.AppendLine(exception.ToString());
            }

            owner.Write(text.ToString());
        }

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "trc",
            LogLevel.Debug => "dbg",
            LogLevel.Information => "inf",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };
    }
}
