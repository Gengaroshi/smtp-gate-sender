namespace SmtpGateSender.Models;

public sealed class SmtpOptions
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; } = false;

    public string User { get; set; } = "";
    public string Pass { get; set; } = "";

    public string From { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 20;
}
