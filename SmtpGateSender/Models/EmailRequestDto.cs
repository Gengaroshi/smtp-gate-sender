using System.Net.Mail;

namespace SmtpGateSender.Models;

public sealed class EmailRequestDto
{
    // Wsteczna kompatybilność (stare klienty)
    public string? Subject { get; set; }
    public string? Body { get; set; }                 // domyślnie: plain text (chyba że IsHtml=true)

    // Nowe (formatowany tekst)
    public string? BodyHtml { get; set; }             // jeśli ustawione -> HTML
    public bool? IsHtml { get; set; }                 // jeśli true i BodyHtml puste -> Body traktujemy jako HTML

    public List<string>? ToEmails { get; set; }
    public List<string>? CcEmails { get; set; }

    public string? From { get; set; }
    public string? RequestId { get; set; }
    public string? Client { get; set; }

    public static EmailRequestDto Normalize(EmailRequestDto dto)
    {
        dto.ToEmails ??= new List<string>();
        dto.CcEmails ??= new List<string>();

        dto.Subject = (dto.Subject ?? "").Trim();
        dto.Body = (dto.Body ?? "").Trim();

        dto.BodyHtml = string.IsNullOrWhiteSpace(dto.BodyHtml) ? null : dto.BodyHtml.Trim();
        dto.From = string.IsNullOrWhiteSpace(dto.From) ? null : dto.From.Trim();
        dto.RequestId = string.IsNullOrWhiteSpace(dto.RequestId) ? null : dto.RequestId.Trim();
        dto.Client = string.IsNullOrWhiteSpace(dto.Client) ? null : dto.Client.Trim();

        dto.ToEmails = dto.ToEmails.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        dto.CcEmails = dto.CcEmails.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();

        // Jeśli klient wysłał HTML w Body (stary styl), a BodyHtml nie ustawione
        if (dto.IsHtml == true && dto.BodyHtml is null && !string.IsNullOrWhiteSpace(dto.Body))
        {
            dto.BodyHtml = dto.Body;
            // Body zostawiamy (może być użyte jako fallback, ale finalnie generujemy plain w senderze)
        }

        return dto;
    }

    public static ValidationResult Validate(EmailRequestDto dto, Services.SpoolConfig cfg)
    {
        if (dto.ToEmails is null || dto.ToEmails.Count == 0) return new(false, "Brak ToEmails.");
        if (string.IsNullOrWhiteSpace(dto.Subject)) return new(false, "Brak Subject.");

        var hasText = !string.IsNullOrWhiteSpace(dto.Body);
        var hasHtml = !string.IsNullOrWhiteSpace(dto.BodyHtml);

        if (!hasText && !hasHtml) return new(false, "Brak treści (Body/BodyHtml).");

        if (dto.Subject.Length > cfg.MaxSubjectChars) return new(false, $"Subject za długi (max {cfg.MaxSubjectChars}).");

        // Limit treści: liczymy większą z wersji, bo realnie tyle może polecieć
        var bodyLen = Math.Max(dto.Body?.Length ?? 0, dto.BodyHtml?.Length ?? 0);
        if (bodyLen > cfg.MaxBodyChars) return new(false, $"Body za długi (max {cfg.MaxBodyChars}).");

        foreach (var e in dto.ToEmails.Concat(dto.CcEmails ?? new List<string>()))
        {
            try { _ = new MailAddress(e); }
            catch { return new(false, $"Nieprawidłowy email: {e}"); }
        }

        if (dto.From != null)
        {
            try { _ = new MailAddress(dto.From); }
            catch { return new(false, $"Nieprawidłowy From: {dto.From}"); }
        }

        if (dto.RequestId != null && dto.RequestId.Length > 120) return new(false, "RequestId za długi.");

        return new(true, null);
    }
}

public sealed record ValidationResult(bool Ok, string? Error);
