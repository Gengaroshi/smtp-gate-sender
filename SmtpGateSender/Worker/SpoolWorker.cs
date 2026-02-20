using System.Text.Json;
using SmtpGateSender.Services;

namespace SmtpGateSender.Worker;

public sealed class SpoolWorker : BackgroundService
{
    private readonly ILogger<SpoolWorker> _log;
    private readonly SpoolStore _store;
    private readonly SmtpSender _sender;
    private readonly IConfiguration _cfg;
    private readonly SemaphoreSlim _sem;

    public SpoolWorker(ILogger<SpoolWorker> log, SpoolStore store, SmtpSender sender, IConfiguration cfg)
    {
        _log = log;
        _store = store;
        _sender = sender;
        _cfg = cfg;

        var parallel = Math.Max(1, _cfg.GetValue<int>("Worker:MaxParallelSends"));
        _sem = new SemaphoreSlim(parallel, parallel);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollMs = Math.Max(300, _cfg.GetValue<int>("Worker:PollIntervalMs"));
        var maxAttempts = Math.Max(1, _cfg.GetValue<int>("Worker:MaxAttempts"));

        _log.LogInformation("SpoolWorker started pollMs={PollMs} maxAttempts={MaxAttempts}", pollMs, maxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var files = _store.GetQueuedFiles().ToList();
                if (files.Count == 0)
                {
                    await Task.Delay(pollMs, stoppingToken);
                    continue;
                }

                var tasks = new List<Task>();
                foreach (var f in files)
                {
                    await _sem.WaitAsync(stoppingToken);
                    tasks.Add(Task.Run(async () =>
                    {
                        try { await ProcessOneAsync(f, maxAttempts, stoppingToken); }
                        finally { _sem.Release(); }
                    }, stoppingToken));
                }

                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Worker loop error");
                await Task.Delay(pollMs, stoppingToken);
            }
        }

        _log.LogInformation("SpoolWorker stopping");
    }

    private async Task ProcessOneAsync(string filePath, int maxAttempts, CancellationToken ct)
    {
        string requestId = "unknown";
        try
        {
            var json = await File.ReadAllTextAsync(filePath, ct);
            using var doc = JsonDocument.Parse(json);
            requestId = (doc.RootElement.TryGetProperty("requestId", out var rid) ? rid.GetString() : (doc.RootElement.TryGetProperty("RequestId", out var rid2) ? rid2.GetString() : null)) ?? "unknown";
        }
        catch
        {
            _store.MoveToFailed(filePath);
            return;
        }

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _sender.SendFromSpoolAsync(filePath, ct);
                _store.MarkIdempotentDone(requestId);
                _store.MoveToSent(filePath);
                return;
            }
            catch (Exception ex)
            {
                // Błędy walidacyjne nie są "przejściowe" -> nie ma sensu retry. Od razu do failed.
                if (ex is InvalidOperationException || ex is ArgumentException)
                {
                    _log.LogWarning(ex, "Permanent failure requestId={RequestId} -> move to failed", requestId);
                    _store.MoveToFailed(filePath);
                    return;
                }

                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt) * 2));
                _log.LogWarning(ex, "Send failed attempt={Attempt}/{Max} requestId={RequestId}", attempt, maxAttempts, requestId);

                if (attempt == maxAttempts)
                {
                    _store.MoveToFailed(filePath);
                    return;
                }

                await Task.Delay(delay, ct);
            }
        }
    }
}
