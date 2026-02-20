using System.Collections.Concurrent;

namespace SmtpGateSender.Services;

public sealed class RateLimiter
{
    private readonly IConfiguration _cfg;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTime>> _hits = new();

    public RateLimiter(IConfiguration cfg) => _cfg = cfg;

    public bool Allow(string ip)
    {
        if (!_cfg.GetValue<bool>("RateLimit:Enabled")) return true;

        var limit = _cfg.GetValue<int>("RateLimit:RequestsPerMinutePerIp");
        if (limit <= 0) return true;

        var now = DateTime.UtcNow;
        var q = _hits.GetOrAdd(ip, _ => new ConcurrentQueue<DateTime>());
        q.Enqueue(now);

        while (q.TryPeek(out var t) && (now - t).TotalSeconds > 60)
            q.TryDequeue(out _);

        return q.Count <= limit;
    }
}
