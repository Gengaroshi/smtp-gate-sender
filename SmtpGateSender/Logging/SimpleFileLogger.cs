using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Threading;

namespace SmtpGateSender.Logging;

public sealed class SimpleFileLogger : ILogger
{
    private static readonly object FileLock = new();

    private readonly string _dir;
    private readonly LogLevel _min;
    private readonly string _cat;

    public SimpleFileLogger(string dir, LogLevel minLevel, string category)
    {
        _dir = dir;
        _min = minLevel;
        _cat = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _min;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var now = DateTime.UtcNow;
        var file = Path.Combine(_dir, $"smtp-gate-{now:yyyyMMdd}.log");

        var msg = formatter(state, exception);
        var line = new StringBuilder()
            .Append(now.ToString("o")).Append(' ')
            .Append('[').Append(logLevel).Append(']').Append(' ')
            .Append(_cat).Append(": ")
            .Append(msg);

        if (exception != null)
            line.Append(" | ex=").Append(exception.GetType().Name).Append(" ").Append(exception.Message);

        line.AppendLine();
        // Zabezpieczenie przed równoległym zapisem i przed tym, że ktoś ma plik otwarty (np. podgląd loga).
        Directory.CreateDirectory(_dir);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                lock (FileLock)
                {
                    using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    sw.Write(line.ToString());
                    sw.Flush();
                }
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(25 * attempt);
            }
        }
    }
}
