using Microsoft.Extensions.Logging;

namespace SmtpGateSender.Logging;

public sealed class SimpleFileLoggerProvider : ILoggerProvider
{
    private readonly string _dir;
    private readonly LogLevel _minLevel;

    public SimpleFileLoggerProvider(string directory, string minLevel)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
        _minLevel = Parse(minLevel);
    }

    public ILogger CreateLogger(string categoryName) => new SimpleFileLogger(_dir, _minLevel, categoryName);

    public void Dispose() { }

    private static LogLevel Parse(string s) =>
        Enum.TryParse<LogLevel>(s, true, out var lvl) ? lvl : LogLevel.Information;
}
