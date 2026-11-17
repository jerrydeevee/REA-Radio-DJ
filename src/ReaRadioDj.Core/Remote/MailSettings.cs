using System.Net.Mail;

namespace ReaRadioDj.Core.Remote;

/// <summary>What is mailed, to whom and over which server. The password is kept encrypted (<see cref="Data.WindowsSecret"/>).</summary>
public sealed record MailSettings
{
    public static readonly IReadOnlyList<string> DefaultTopics = ["stilte", "reservestudio", "reservekopie", "geluidskaart"];

    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;
    /// <summary>STARTTLS (port 587 or 25). Implicit TLS on port 465 is not offered by .NET's mail client.</summary>
    public bool Tls { get; init; } = true;
    public string User { get; init; } = "";
    public string PasswordProtected { get; init; } = "";
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public IReadOnlyList<string> Topics { get; init; } = DefaultTopics;
    /// <summary>At most one mail per topic in this many minutes; what comes in meanwhile goes along in the next one.</summary>
    public int MinutesBetween { get; init; } = 10;

    public IReadOnlyList<string> Recipients => To.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Why mail cannot be sent with these settings, or null.</summary>
    public string? Problem()
    {
        if (Host.Trim().Length == 0) return "geen mailserver ingesteld";
        if (Port is < 1 or > 65535) return "ongeldige poort";
        if (Port == 465) return "poort 465 (TLS vanaf het begin) wordt niet ondersteund; gebruik poort 587 met STARTTLS";
        if (!IsAddress(From)) return "het afzenderadres klopt niet";
        if (Recipients.Count == 0 || !Recipients.All(IsAddress)) return "geef een of meer geldige ontvangers";
        return null;
    }

    private static bool IsAddress(string a) => MailAddress.TryCreate(a.Trim(), out _);
}

