namespace SmtpGateSender.Models;

public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>How often to run cleanup.</summary>
    public int RunEveryMinutes { get; set; } = 60;

    public LogRetentionOptions Logs { get; set; } = new();
    public SpoolRetentionOptions Spool { get; set; } = new();
}

public sealed class LogRetentionOptions
{
    /// <summary>Delete log files older than this many days.</summary>
    public int Days { get; set; } = 14;

    /// <summary>Optional override. If empty, uses Logging:File:Directory.</summary>
    public string? Directory { get; set; }
}

public sealed class SpoolRetentionOptions
{
    /// <summary>Optional override. If empty, uses Spool:Root.</summary>
    public string? Root { get; set; }

    /// <summary>Delete sent spool files older than this many days.</summary>
    public int SentDays { get; set; } = 14;

    /// <summary>Delete failed spool files older than this many days.</summary>
    public int FailedDays { get; set; } = 30;

    /// <summary>Delete idempotency markers older than this many days.</summary>
    public int IdemDays { get; set; } = 7;

    /// <summary>
    /// Optional: move queued items older than this many days to "failed" (0 = disabled).
    /// </summary>
    public int QueuedMaxAgeDays { get; set; } = 0;
}
