using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SmtpGateSender.Models;

namespace SmtpGateSender.Services;

public sealed class SpoolConfig
{
    public string Root { get; init; } = Path.Combine(AppContext.BaseDirectory, "data", "spool");
    public int IdempotencyHours { get; init; } = 24;
    public int MaxBodyChars { get; init; } = 200000;
    public int MaxSubjectChars { get; init; } = 300;
}

public sealed class SpoolStats
{
    public int Queued { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SpoolStore
{
    private readonly ILogger<SpoolStore> _log;
    private readonly SpoolConfig _cfg;

    private readonly string _queued;
    private readonly string _sent;
    private readonly string _failed;
    private readonly string _idem;

    public SpoolConfig Config => _cfg;

    public SpoolStore(IConfiguration cfg, ILogger<SpoolStore> log)
    {
        _log = log;
        _cfg = cfg.GetSection("Spool").Get<SpoolConfig>() ?? new SpoolConfig();

        _queued = Path.Combine(_cfg.Root, "queued");
        _sent = Path.Combine(_cfg.Root, "sent");
        _failed = Path.Combine(_cfg.Root, "failed");
        _idem = Path.Combine(_cfg.Root, "idem");

        Directory.CreateDirectory(_queued);
        Directory.CreateDirectory(_sent);
        Directory.CreateDirectory(_failed);
        Directory.CreateDirectory(_idem);
    }

    public SpoolStats GetStats()
    {
        return new SpoolStats
        {
            Queued = SafeCount(_queued, "*.json"),
            Sent = SafeCount(_sent, "*.json"),
            Failed = SafeCount(_failed, "*.json"),
            TimeUtc = DateTime.UtcNow
        };
    }

    private static int SafeCount(string dir, string pattern)
    {
        try { return Directory.EnumerateFiles(dir, pattern).Count(); }
        catch { return -1; }
    }

    public async Task<EnqueueResult> EnqueueAsync(EmailRequestDto dto, string ip)
    {
        var requestId = dto.RequestId ?? ComputeStableId(dto);

        var idemKey = ComputeIdemKey(requestId);
        var idemDir = Path.Combine(_idem, DateTime.UtcNow.ToString("yyyyMMdd"));
        Directory.CreateDirectory(idemDir);

        var marker = Path.Combine(idemDir, $"{idemKey}.done");
        if (File.Exists(marker))
            return new EnqueueResult("duplicate", requestId);

        var now = DateTime.UtcNow;
        var payload = new
        {
            requestId = dto.RequestId,
            receivedUtc = now.ToString("o"),
            client = dto.Client,
            from = dto.From, // może być null -> Sender użyje konfiguracji
            subject = dto.Subject,
            body = dto.Body,
            bodyHtml = dto.BodyHtml,
            isHtml = dto.IsHtml,
            toEmails = dto.ToEmails ?? new List<string>(),
            ccEmails = dto.CcEmails ?? new List<string>(),
            meta = new { ip }
        };


        var fileName = $"{now:yyyyMMdd-HHmmssfff}_{idemKey}.json";
        var targetPath = Path.Combine(_queued, fileName);

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(targetPath, json, Encoding.UTF8);

        return new EnqueueResult("queued", requestId);
    }

    public IEnumerable<string> GetQueuedFiles() =>
        Directory.EnumerateFiles(_queued, "*.json").OrderBy(x => x);

    public void MarkIdempotentDone(string requestId)
    {
        var idemKey = ComputeIdemKey(requestId);
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        var dir = Path.Combine(_idem, today);
        Directory.CreateDirectory(dir);

        var marker = Path.Combine(dir, $"{idemKey}.done");
        File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
        CleanupIdemOldDays();
    }

    private void CleanupIdemOldDays()
    {
        var keepDays = Math.Max(1, (int)Math.Ceiling(_cfg.IdempotencyHours / 24.0));
        var cutoff = DateTime.UtcNow.Date.AddDays(-keepDays);

        foreach (var d in Directory.EnumerateDirectories(_idem))
        {
            var name = Path.GetFileName(d);
            if (name.Length == 8 && DateTime.TryParseExact(name, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var dt))
            {
                if (dt < cutoff)
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
        }
    }

    public void MoveToSent(string filePath)
    {
        var name = Path.GetFileName(filePath);
        File.Move(filePath, Path.Combine(_sent, name), overwrite: true);
    }

    public void MoveToFailed(string filePath)
    {
        var name = Path.GetFileName(filePath);
        File.Move(filePath, Path.Combine(_failed, name), overwrite: true);
    }

    public static string ComputeStableId(EmailRequestDto dto)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join("|", dto.ToEmails ?? new List<string>()).ToLowerInvariant()).Append('\n');
        sb.Append(string.Join("|", dto.CcEmails ?? new List<string>()).ToLowerInvariant()).Append('\n');
        sb.Append(dto.Subject ?? "").Append('\n');
        sb.Append(dto.Body ?? "");

        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ComputeIdemKey(string requestId)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(requestId));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
