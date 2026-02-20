using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpGateSender.Models;
using SmtpGateSender.Services;

namespace SmtpGateSender.Worker;

public sealed class RetentionWorker : BackgroundService
{
    private readonly RetentionOptions _opt;
    private readonly ILogger<RetentionWorker> _log;
    private readonly IConfiguration _cfg;
    private readonly SpoolStore _store;

    public RetentionWorker(IOptions<RetentionOptions> opt, IConfiguration cfg, SpoolStore store, ILogger<RetentionWorker> log)
    {
        _opt = opt.Value;
        _cfg = cfg;
        _store = store;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_opt.Enabled)
        {
            _log.LogInformation("Retention disabled.");
            return;
        }

        var every = TimeSpan.FromMinutes(Math.Max(5, _opt.RunEveryMinutes));
        _log.LogInformation("RetentionWorker started intervalMin={Min}", every.TotalMinutes);

        // run once quickly on startup, then periodically
        try { RunCleanup(); }
        catch (Exception ex) { _log.LogWarning(ex, "Retention cleanup failed (startup)."); }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(every, stoppingToken);
            }
            catch (TaskCanceledException) { break; }

            try { RunCleanup(); }
            catch (Exception ex) { _log.LogWarning(ex, "Retention cleanup failed."); }
        }
    }

    private void RunCleanup()
    {
        var nowUtc = DateTime.UtcNow;

        // --- Logs ---
        var logDir = _opt.Logs.Directory;
        if (string.IsNullOrWhiteSpace(logDir))
            logDir = _cfg["Logging:File:Directory"];

        if (!string.IsNullOrWhiteSpace(logDir) && Directory.Exists(logDir))
        {
            var cutoff = nowUtc.AddDays(-Math.Max(1, _opt.Logs.Days));
            var deleted = 0;

            foreach (var f in Directory.EnumerateFiles(logDir, "*.log", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTimeUtc < cutoff)
                    {
                        fi.Delete();
                        deleted++;
                    }
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Cannot delete log file {File}", f);
                }
            }

            if (deleted > 0)
                _log.LogInformation("Retention logs deleted={Deleted} cutoffUtc={Cutoff}", deleted, cutoff.ToString("o"));
        }

        // --- Spool ---
        var spoolRoot = _opt.Spool.Root;
        if (string.IsNullOrWhiteSpace(spoolRoot))
            spoolRoot = _store.Config.Root;

        if (!string.IsNullOrWhiteSpace(spoolRoot) && Directory.Exists(spoolRoot))
        {
            CleanupSpoolSubdir(Path.Combine(spoolRoot, "sent"), nowUtc.AddDays(-Math.Max(1, _opt.Spool.SentDays)), "sent");
            CleanupSpoolSubdir(Path.Combine(spoolRoot, "failed"), nowUtc.AddDays(-Math.Max(1, _opt.Spool.FailedDays)), "failed");
            CleanupIdem(Path.Combine(spoolRoot, "idem"), nowUtc.AddDays(-Math.Max(1, _opt.Spool.IdemDays)));

            if (_opt.Spool.QueuedMaxAgeDays > 0)
            {
                MoveOldQueuedToFailed(
                    queuedDir: Path.Combine(spoolRoot, "queued"),
                    failedDir: Path.Combine(spoolRoot, "failed"),
                    cutoffUtc: nowUtc.AddDays(-_opt.Spool.QueuedMaxAgeDays));
            }
        }
    }

    private void CleanupSpoolSubdir(string dir, DateTime cutoffUtc, string tag)
    {
        if (!Directory.Exists(dir)) return;

        var deleted = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fi = new FileInfo(f);
                if (fi.LastWriteTimeUtc < cutoffUtc)
                {
                    fi.Delete();
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cannot delete spool file {File}", f);
            }
        }

        if (deleted > 0)
            _log.LogInformation("Retention spool {Tag} deleted={Deleted} cutoffUtc={Cutoff}", tag, deleted, cutoffUtc.ToString("o"));
    }

    private void CleanupIdem(string idemRoot, DateTime cutoffUtc)
    {
        if (!Directory.Exists(idemRoot)) return;

        var deletedDirs = 0;
        var cutoffDate = cutoffUtc.Date;

        foreach (var d in Directory.EnumerateDirectories(idemRoot))
        {
            try
            {
                var name = Path.GetFileName(d);
                // format yyyyMMdd (as used by SpoolStore)
                if (name.Length == 8 && int.TryParse(name, out _))
                {
                    if (DateTime.TryParseExact(name, "yyyyMMdd", null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
                    {
                        if (dt.Date < cutoffDate)
                        {
                            Directory.Delete(d, recursive: true);
                            deletedDirs++;
                        }
                        continue;
                    }
                }

                // fallback to last write time
                var di = new DirectoryInfo(d);
                if (di.LastWriteTimeUtc < cutoffUtc)
                {
                    Directory.Delete(d, recursive: true);
                    deletedDirs++;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cannot delete idem dir {Dir}", d);
            }
        }

        if (deletedDirs > 0)
            _log.LogInformation("Retention idem deletedDirs={Deleted} cutoffUtc={Cutoff}", deletedDirs, cutoffUtc.ToString("o"));
    }

    private void MoveOldQueuedToFailed(string queuedDir, string failedDir, DateTime cutoffUtc)
    {
        if (!Directory.Exists(queuedDir)) return;
        Directory.CreateDirectory(failedDir);

        var moved = 0;
        foreach (var f in Directory.EnumerateFiles(queuedDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fi = new FileInfo(f);
                if (fi.LastWriteTimeUtc < cutoffUtc)
                {
                    var dest = Path.Combine(failedDir, Path.GetFileName(f));
                    if (File.Exists(dest))
                        dest = Path.Combine(failedDir, $"{Path.GetFileNameWithoutExtension(f)}.expired{Path.GetExtension(f)}");

                    File.Move(f, dest);
                    moved++;
                }
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cannot move queued file {File}", f);
            }
        }

        if (moved > 0)
            _log.LogWarning("Retention moved old queued->failed moved={Moved} cutoffUtc={Cutoff}", moved, cutoffUtc.ToString("o"));
    }
}
