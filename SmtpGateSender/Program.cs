using SmtpGateSender.Logging;
using SmtpGateSender.Models;
using SmtpGateSender.Services;
using SmtpGateSender.Worker;
using SmtpGateSender.Swagger;

var builder = WebApplication.CreateBuilder(args);

// Legacy config bridge: SMTP_Config -> Smtp
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.Configure<SmtpOptions>(opt =>
{
    // najpierw nowy format
    builder.Configuration.GetSection("Smtp").Bind(opt);

    // nadpisanie z ENV (wygodne w Dockerze)
    var envHost = Environment.GetEnvironmentVariable("SMTP_HOST");
    var envPort = Environment.GetEnvironmentVariable("SMTP_PORT");
    var envUser = Environment.GetEnvironmentVariable("SMTP_USER");
    var envPass = Environment.GetEnvironmentVariable("SMTP_PASS");
    var envFrom = Environment.GetEnvironmentVariable("SMTP_FROM");
    var envSsl = Environment.GetEnvironmentVariable("SMTP_SSL");

    if (!string.IsNullOrWhiteSpace(envHost)) opt.Host = envHost;
    if (int.TryParse(envPort, out var ep)) opt.Port = ep;
    if (!string.IsNullOrWhiteSpace(envUser)) opt.User = envUser;
    if (!string.IsNullOrWhiteSpace(envPass)) opt.Pass = envPass;
    if (!string.IsNullOrWhiteSpace(envFrom)) opt.From = envFrom;
    if (bool.TryParse(envSsl, out var es)) opt.UseSsl = es;

    // jeśli Host nadal pusty, spróbuj starego formatu
    if (string.IsNullOrWhiteSpace(opt.Host))
    {
        var legacy = builder.Configuration.GetSection("SMTP_Config");
        if (legacy.Exists())
        {
            opt.Host = legacy["SMTP_Server"] ?? opt.Host;
            opt.Port = int.TryParse(legacy["SMTP_Port"], out var p) ? p : opt.Port;
            opt.User = legacy["SMTP_Username"] ?? opt.User;
            opt.Pass = legacy["SMTP_Password"] ?? opt.Pass;
            opt.UseSsl = bool.TryParse(legacy["EnableSsl"], out var ssl) ? ssl : opt.UseSsl;

            // From: jeśli nie ustawione w nowym, ustaw jak User (tak jak stare bramki zwykle robią)
            if (string.IsNullOrWhiteSpace(opt.From))
                opt.From = opt.User;
        }
    }
});

// API docs (homelab/demo)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // MULTI-samples dla /email i /api/email
    c.OperationFilter<EmailExamplesOperationFilter>();
});

// URL nasłuchu (żeby usługa miała stały port)
var listenUrls = builder.Configuration["ListenUrls"] ?? "http://0.0.0.0:8885";
builder.WebHost.UseUrls(listenUrls);

// logging: console + file rolling
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var fileLogEnabled = builder.Configuration.GetValue<bool>("Logging:File:Enabled");
if (fileLogEnabled)
{
    var logDir = builder.Configuration["Logging:File:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "data", "logs");
    var minLevel = builder.Configuration["Logging:File:MinLevel"] ?? "Information";
    builder.Logging.AddProvider(new SimpleFileLoggerProvider(logDir, minLevel));
}

// Windows Service hosting (bez wpływu na normalne uruchamianie z konsoli)
builder.Host.UseWindowsService();

builder.Services.AddSingleton<SpoolStore>();
builder.Services.AddSingleton<SmtpSender>();
builder.Services.AddSingleton<RateLimiter>();
builder.Services.AddHostedService<SpoolWorker>();

builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection("Retention"));
builder.Services.AddHostedService<RetentionWorker>();

var app = builder.Build();

// Swagger UI (enabled by default for homelab/demo)
app.UseSwagger();
app.UseSwaggerUI();

// global exception -> JSON
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// Health
app.MapGet("/health", (SpoolStore store) =>
{
    var s = store.GetStats();
    return Results.Ok(new
    {
        ok = true,
        timeUtc = DateTime.UtcNow.ToString("o"),
        queued = s.Queued,
        sent = s.Sent,
        failed = s.Failed
    });
});

// Stats (bardziej szczegółowe)
app.MapGet("/stats", (SpoolStore store) => Results.Ok(store.GetStats()));

// Kompatybilność: stary endpoint /api/email
app.MapPost("/api/email", async (HttpContext ctx, EmailRequestDto dto, SpoolStore store, RateLimiter rl, ILoggerFactory lf) =>
{
    var log = lf.CreateLogger("ApiEmail");
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!rl.Allow(ip))
        return Results.StatusCode(429);

    var normalized = EmailRequestDto.Normalize(dto);

    var vr = EmailRequestDto.Validate(normalized, store.Config);
    if (!vr.Ok)
        return Results.BadRequest(new { error = vr.Error });

    // Idempotencja: jeśli RequestId już przetworzony w oknie -> nie duplikuj
    var enqueue = await store.EnqueueAsync(normalized, ip);
    log.LogInformation("Enqueue result={Result} requestId={RequestId} ip={Ip} toCount={ToCount}",
        enqueue.Result, enqueue.RequestId, ip, normalized.ToEmails.Count);

    // 202 (queued) lub 200 (duplicate)
    if (enqueue.Result == "duplicate")
        return Results.Ok(new { requestId = enqueue.RequestId, duplicate = true });

    return Results.Accepted(value: new { requestId = enqueue.RequestId });
});

// Nowy endpoint (opcjonalny) - alias bez /api
app.MapPost("/email", async (HttpContext ctx, EmailRequestDto dto, SpoolStore store, RateLimiter rl, ILoggerFactory lf) =>
{
    var log = lf.CreateLogger("Email");
    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    if (!rl.Allow(ip))
        return Results.StatusCode(429);

    var normalized = EmailRequestDto.Normalize(dto);

    var vr = EmailRequestDto.Validate(normalized, store.Config);
    if (!vr.Ok)
        return Results.BadRequest(new { error = vr.Error });

    var enqueue = await store.EnqueueAsync(normalized, ip);
    log.LogInformation("Enqueue result={Result} requestId={RequestId} ip={Ip}", enqueue.Result, enqueue.RequestId, ip);

    if (enqueue.Result == "duplicate")
        return Results.Ok(new { requestId = enqueue.RequestId, duplicate = true });

    return Results.Accepted(value: new { requestId = enqueue.RequestId });
});

app.Run();
