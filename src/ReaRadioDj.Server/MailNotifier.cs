using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using ReaRadioDj.Contracts;
using ReaRadioDj.Core;
using ReaRadioDj.Core.Data;
using ReaRadioDj.Core.Remote;

namespace ReaRadioDj.Server;

/// <summary>
/// Sends the important messages by mail, so they are seen outside the studio too: silence on air, the backup studio
/// taking over, a failed backup, a card that stopped, a placeholder whose sound is late, a recording that failed.
/// Mails of one topic are held together for <see cref="MailSettings.MinutesBetween"/> minutes, so a flapping alarm
/// does not fill the mailbox.
/// </summary>
public sealed class MailNotifier(MessageCenter messages, StationDb db, ReaRadioDjOptions options, ILogger<MailNotifier> logger) : BackgroundService
{
    public const string Key = "notify.mail";

    /// <summary>The topics that can be mailed, with what they are about (Dutch; translated in the studio).</summary>
    public static readonly IReadOnlyList<(string Id, string Name)> AllTopics =
    [
        ("stilte", L("Stilte op de zender (en weer geluid)")),
        ("reservestudio", L("Reservestudio neemt over of geeft terug")),
        ("reservekopie", L("Reservekopie mislukt (en alsnog gelukt)")),
        ("geluidskaart", L("ASIO-kaart valt weg")),
        ("plaatshouder", L("Plaatshouder zonder geluid")),
        ("opname", L("Geplande opname mislukt")),
        ("alarm", L("Elk ander alarm (ook het commando ALERT)")),
    ];

    /// <summary>Marks a Dutch text for the translation table; the studio shows it in its own language.</summary>
    private static string L(string nl) => nl;


    private readonly object _gate = new();
    private MailSettings _settings = new();
    private readonly Dictionary<string, List<StudioMessage>> _pending = [];
    private readonly Dictionary<string, DateTime> _lastSent = [];

    public string? LastError { get; private set; }
    public DateTime? LastSent { get; private set; }

    public MailSettings Settings { get { lock (_gate) return _settings; } }

    public static MailSettings Load(StationDb db)
    {
        if (db.GetSetting(Key) is not { } json) return new();
        try { return JsonSerializer.Deserialize<MailSettings>(json, JsonSerializerOptions.Web) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Saves; a null or empty password keeps the stored one.</summary>
    public MailSettings Save(MailSettingsDto d)
    {
        var old = Settings;
        var s = new MailSettings
        {
            Enabled = d.Enabled, Host = d.Host.Trim(), Port = d.Port, Tls = d.Tls, User = d.User.Trim(),
            PasswordProtected = string.IsNullOrEmpty(d.Password) ? (d.ClearPassword ? "" : old.PasswordProtected) : WindowsSecret.Protect(d.Password),
            From = d.From.Trim(), To = d.To.Trim(),
            Topics = d.Topics.Where(t => AllTopics.Any(a => a.Id == t)).Distinct().ToList(),
            MinutesBetween = Math.Clamp(d.MinutesBetween, 1, 1440),
        };
        db.SetSetting(Key, JsonSerializer.Serialize(s, JsonSerializerOptions.Web));
        lock (_gate) _settings = s;
        return s;
    }

    public MailSettingsDto Dto()
    {
        var s = Settings;
        return new MailSettingsDto(s.Enabled, s.Host, s.Port, s.Tls, s.User, null, s.PasswordProtected.Length > 0, false, s.From, s.To, s.Topics,
            s.MinutesBetween, AllTopics.Select(t => new MailTopicDto(t.Id, t.Name)).ToList(), LastSent, LastError, s.Problem());
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        lock (_gate) _settings = Load(db);
        messages.Raised += OnMessage;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(ct)) await FlushAsync(DateTime.Now, ct);
        }
        catch (OperationCanceledException) { }
        finally { messages.Raised -= OnMessage; }
    }

    private void OnMessage(StudioMessage m, string? topic)
    {
        topic ??= m.Level == "alarm" ? "alarm" : null;
        if (topic is null) return;
        lock (_gate)
        {
            if (!_settings.Enabled || !_settings.Topics.Contains(topic)) return;
            if (!_pending.TryGetValue(topic, out var list)) _pending[topic] = list = [];
            if (list.Count < 50) list.Add(m);
        }
    }

    internal async Task FlushAsync(DateTime now, CancellationToken ct)
    {
        List<(string Topic, List<StudioMessage> Items)> due = [];
        MailSettings s;
        lock (_gate)
        {
            s = _settings;
            foreach (var (topic, list) in _pending)
                if (list.Count > 0 && (!_lastSent.TryGetValue(topic, out var last) || now - last >= TimeSpan.FromMinutes(s.MinutesBetween)))
                    due.Add((topic, list.ToList()));
            foreach (var (topic, _) in due) { _pending[topic].Clear(); _lastSent[topic] = now; }
        }
        foreach (var (topic, items) in due)
        {
            var name = AllTopics.FirstOrDefault(t => t.Id == topic).Name ?? topic;
            var worst = items.Any(i => i.Level == "alarm") ? "ALARM" : items.Any(i => i.Level is "waarschuwing" or "fout") ? "Waarschuwing" : "Melding";
            var subject = $"[{options.StationName}] {worst}: {items[^1].Text}";
            var body = new StringBuilder()
                .AppendLine($"{options.StationName} · {Environment.MachineName} · {name}")
                .AppendLine();
            foreach (var i in items) body.AppendLine($"{i.Time:ddd d MMM HH:mm:ss}  {i.Text}");
            body.AppendLine().AppendLine("Verstuurd door REA-Radio-DJ. Zet de meldingen per mail aan of uit in de studio: Instellingen › Meldingen per mail.");
            await SendAsync(s, subject, body.ToString(), ct);
        }
    }

    /// <summary>Sends one mail; null when it went, otherwise the reason.</summary>
    public async Task<string?> SendAsync(MailSettings s, string subject, string body, CancellationToken ct = default)
    {
        if (s.Problem() is { } problem) return LastError = problem;
        try
        {
            using var client = new SmtpClient(s.Host.Trim(), s.Port) { EnableSsl = s.Tls, DeliveryMethod = SmtpDeliveryMethod.Network, Timeout = 20000 };
            if (s.User.Length > 0) client.Credentials = new NetworkCredential(s.User, WindowsSecret.Unprotect(s.PasswordProtected));
            using var mail = new MailMessage { From = new MailAddress(s.From.Trim(), options.StationName), Subject = subject.Length > 160 ? subject[..160] + "…" : subject,
                Body = body, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
            foreach (var to in s.Recipients) mail.To.Add(to);
            await client.SendMailAsync(mail, ct);
            LastSent = DateTime.Now;
            LastError = null;
            logger.LogInformation("Mail sent to {Count} recipient(s): {Subject}", s.Recipients.Count, subject);
            return null;
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException or IOException)
        {
            logger.LogWarning(ex, "Mail not sent: {Subject}", subject);
            return LastError = ex.InnerException is { } inner ? $"{ex.Message} ({inner.Message})" : ex.Message;
        }
    }

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/notify/mail", (MailNotifier n) => n.Dto());
        api.MapPut("/notify/mail", (MailSettingsDto d, MailNotifier n) => { n.Save(d); return n.Dto(); });
        api.MapPost("/notify/mail/test", async (MailNotifier n, ReaRadioDjOptions o) =>
        {
            var error = await n.SendAsync(n.Settings, $"[{o.StationName}] Testmail",
                $"Dit is een testmail van REA-Radio-DJ op {Environment.MachineName}, {DateTime.Now:dddd d MMMM HH:mm}.\r\nKomt hij aan, dan komen de meldingen ook aan.");
            return error is null ? Results.Ok(n.Dto()) : Results.BadRequest(error);
        });
    }
}
