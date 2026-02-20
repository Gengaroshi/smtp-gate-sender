using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpGateSender.Models;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;

namespace SmtpGateSender.Services;

public sealed class SmtpSender
{
    private readonly SmtpOptions _opt;
    private readonly ILogger<SmtpSender> _log;

    public SmtpSender(IOptions<SmtpOptions> opt, ILogger<SmtpSender> log)
    {
        _opt = opt.Value;
        _log = log;
    }

    public async Task SendAsync(EmailRequestDto dto, CancellationToken ct)
    {
        var host = (_opt.Host ?? "").Trim();
        var port = _opt.Port;
        var useSsl = _opt.UseSsl;
        var user = (_opt.User ?? "").Trim();
        var pass = _opt.Pass ?? "";

        if (string.IsNullOrWhiteSpace(host))
            throw new InvalidOperationException("Brak Smtp:Host w konfiguracji.");

        using var msg = new MailMessage();

        // Kompatybilno wsteczna: stare klienty nie wysyaj From
        var from =
            !string.IsNullOrWhiteSpace(dto.From) ? dto.From.Trim() :
            !string.IsNullOrWhiteSpace(_opt.From) ? _opt.From.Trim() :
            !string.IsNullOrWhiteSpace(user) ? user :
            "";

        if (string.IsNullOrWhiteSpace(from))
            throw new InvalidOperationException("Brak From (ani w daniu, ani w konfiguracji Smtp:From, ani w Smtp:User).");

        msg.From = new MailAddress(from);

        foreach (var t in dto.ToEmails ?? Enumerable.Empty<string>())
        {
            var tt = (t ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(tt))
                msg.To.Add(new MailAddress(tt));
        }

        foreach (var c in dto.CcEmails ?? Enumerable.Empty<string>())
        {
            var cc = (c ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(cc))
                msg.CC.Add(new MailAddress(cc));
        }

        if (msg.To.Count == 0)
            throw new InvalidOperationException("Brak adresatw ToEmails.");

        msg.Subject = (dto.Subject ?? "").Trim();
        msg.SubjectEncoding = Encoding.UTF8;
        msg.BodyEncoding = Encoding.UTF8;
        msg.HeadersEncoding = Encoding.UTF8;

        // Tre: preferuj HTML jeli jest
        var html = dto.BodyHtml;
        var text = dto.Body;

        if (!string.IsNullOrWhiteSpace(html))
        {
            var htmlTrim = html.Trim();

            // fallback plain
            var plain = !string.IsNullOrWhiteSpace(text) ? text!.Trim() : HtmlToPlainText(htmlTrim);

            msg.IsBodyHtml = false;
            msg.Body = plain;

            // multipart/alternative
            var plainView = AlternateView.CreateAlternateViewFromString(plain, Encoding.UTF8, "text/plain");
            var htmlView = AlternateView.CreateAlternateViewFromString(htmlTrim, Encoding.UTF8, "text/html");
            msg.AlternateViews.Add(plainView);
            msg.AlternateViews.Add(htmlView);
        }
        else
        {
            var body = text ?? "";

            // stara wersja: jeli IsHtml==true, traktuj Body jako HTML
            if (dto.IsHtml == true && !string.IsNullOrWhiteSpace(body))
            {
                var htmlBody = body.Trim();
                var plain = HtmlToPlainText(htmlBody);

                msg.IsBodyHtml = false;
                msg.Body = plain;

                var plainView = AlternateView.CreateAlternateViewFromString(plain, Encoding.UTF8, "text/plain");
                var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, "text/html");
                msg.AlternateViews.Add(plainView);
                msg.AlternateViews.Add(htmlView);
            }
            else
            {
                msg.IsBodyHtml = false;
                msg.Body = body;
            }
        }

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl,
            Timeout = Math.Max(5, _opt.TimeoutSeconds) * 1000
        };

        if (!string.IsNullOrWhiteSpace(user))
            client.Credentials = new NetworkCredential(user, pass);

        var hasHtml = !string.IsNullOrWhiteSpace(dto.BodyHtml) || dto.IsHtml == true;

        _log.LogInformation(
            "SMTP send start toCount={ToCount} ccCount={CcCount} subjectLen={SubLen} hasHtml={HasHtml} host={Host}:{Port} ssl={Ssl}",
            msg.To.Count, msg.CC.Count, msg.Subject.Length, hasHtml, host, port, useSsl);

        // SmtpClient nie ma natywnego CT  Task.Run + ThrowIfCancellationRequested
        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            client.Send(msg);
        }, ct);

        _log.LogInformation("SMTP send ok toCount={ToCount} ccCount={CcCount}", msg.To.Count, msg.CC.Count);
    }

    public async Task SendFromSpoolAsync(string filePath, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);

        // Spool może mieć różne formaty (starsze wersje zapisywały "to"/"cc", nowe "toEmails"/"ccEmails"
        // oraz/lub DTO bezpośrednio). Tu robimy parsowanie odporne na format.
        EmailRequestDto dto;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            // helper: lista stringów z pola które może być stringiem lub tablicą stringów
            static List<string> ReadStringList(System.Text.Json.JsonElement root, params string[] names)
            {
                foreach (var n in names)
                {
                    if (!root.TryGetProperty(n, out var el)) continue;

                    if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var s = el.GetString();
                        return string.IsNullOrWhiteSpace(s) ? new List<string>() : new List<string> { s! };
                    }

                    if (el.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var list = new List<string>();
                        foreach (var it in el.EnumerateArray())
                        {
                            if (it.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                            var s = it.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) list.Add(s!);
                        }
                        return list;
                    }

                    return new List<string>();
                }

                return new List<string>();
            }

            static string? ReadString(System.Text.Json.JsonElement root, params string[] names)
            {
                foreach (var n in names)
                {
                    if (!root.TryGetProperty(n, out var el)) continue;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.String) return el.GetString();
                }
                return null;
            }

            static bool? ReadBool(System.Text.Json.JsonElement root, params string[] names)
            {
                foreach (var n in names)
                {
                    if (!root.TryGetProperty(n, out var el)) continue;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.True) return true;
                    if (el.ValueKind == System.Text.Json.JsonValueKind.False) return false;
                }
                return null;
            }

            dto = new EmailRequestDto
            {
                RequestId = ReadString(root, "requestId", "RequestId"),
                Client = ReadString(root, "client", "Client"),
                From = ReadString(root, "from", "From"),
                Subject = ReadString(root, "subject", "Subject"),
                Body = ReadString(root, "body", "Body"),
                BodyHtml = ReadString(root, "bodyHtml", "BodyHtml"),
                IsHtml = ReadBool(root, "isHtml", "IsHtml"),
                ToEmails = ReadStringList(root, "toEmails", "ToEmails", "to", "To"),
                CcEmails = ReadStringList(root, "ccEmails", "CcEmails", "cc", "Cc")
            };

            dto = EmailRequestDto.Normalize(dto);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Nie można zdeserializować spool JSON: {Path.GetFileName(filePath)}", ex);
        }

        await SendAsync(dto, ct);
    }

private static string HtmlToPlainText(string html)
    {
        var s = html ?? "";

        s = s.Replace("\r", "").Replace("\n", "");
        s = s.Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
             .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
             .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
             .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
             .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
             .Replace("</tr>", "\n", StringComparison.OrdinalIgnoreCase)
             .Replace("</td>", " ", StringComparison.OrdinalIgnoreCase)
             .Replace("</th>", " ", StringComparison.OrdinalIgnoreCase);

        // usu tagi
        var sb = new StringBuilder(s.Length);
        var inside = false;
        foreach (var ch in s)
        {
            if (ch == '<') { inside = true; continue; }
            if (ch == '>') { inside = false; continue; }
            if (!inside) sb.Append(ch);
        }

        var plain = WebUtility.HtmlDecode(sb.ToString());

        // przytnij nadmiarowe spacje
        plain = string.Join("\n", plain.Split('\n').Select(l => l.TrimEnd()));
        return plain.Trim();
    }
}
