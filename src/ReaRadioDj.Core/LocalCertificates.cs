using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.Extensions.Logging;

namespace ReaRadioDj.Core;

/// <summary>
/// HTTPS on the station's own network, without a domain name: the engine makes its own small certificate authority
/// (installed once on a phone or tablet, then the browser trusts the engine) and with it a certificate for the names and
/// addresses of this computer. The server certificate is made again when an address changes or it nearly runs out.
/// Both live in the data folder; the CA's private key never leaves it, only its public certificate can be downloaded.
/// </summary>
public static class LocalCertificates
{
    public const string CaName = "REA-Radio-DJ lokale CA";
    private const string CaFile = "rea-ca.pfx", ServerFile = "rea-server.pfx";

    /// <summary>The certificate the engine serves HTTPS with (made or renewed when needed).</summary>
    public static X509Certificate2 Server(string dataFolder, ILogger? log = null)
    {
        var folder = Path.Combine(dataFolder, "certs");
        Directory.CreateDirectory(folder);
        var ca = LoadOrCreateCa(folder, log);
        var names = Names();
        var path = Path.Combine(folder, ServerFile);
        if (File.Exists(path))
        {
            try
            {
                var existing = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.Now.AddDays(30) && Covers(existing, names) && existing.Issuer == ca.Subject) return existing;
            }
            catch (CryptographicException) { }
        }
        var made = MakeServer(ca, names);
        File.WriteAllBytes(path, made.Export(X509ContentType.Pkcs12));
        log?.LogInformation("HTTPS-certificaat gemaakt voor {Names}", string.Join(", ", names.Dns.Concat(names.Ips.Select(i => i.ToString()))));
        // Windows wants the key from a PFX, not the in-memory one, for TLS.
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
    }

    /// <summary>The public certificate of the station's CA (DER), to install on a phone or tablet; null when there is none yet.</summary>
    public static byte[]? CaCertificate(string dataFolder)
    {
        var path = Path.Combine(dataFolder, "certs", CaFile);
        if (!File.Exists(path)) return null;
        using var ca = X509CertificateLoader.LoadPkcs12FromFile(path, null);
        return ca.Export(X509ContentType.Cert);
    }

    private static X509Certificate2 LoadOrCreateCa(string folder, ILogger? log)
    {
        var path = Path.Combine(folder, CaFile);
        if (File.Exists(path))
        {
            try
            {
                var ca = X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
                if (ca.NotAfter > DateTime.Now.AddDays(60)) return ca;
            }
            catch (CryptographicException) { }
        }
        using var key = RSA.Create(3072);
        var request = new CertificateRequest($"CN={CaName} ({Environment.MachineName}), O=REA-Radio-DJ", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var made = request.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(10));
        File.WriteAllBytes(path, made.Export(X509ContentType.Pkcs12));
        log?.LogInformation("Lokale CA voor HTTPS gemaakt");
        return X509CertificateLoader.LoadPkcs12FromFile(path, null, X509KeyStorageFlags.Exportable);
    }

    private static X509Certificate2 MakeServer(X509Certificate2 ca, (IReadOnlyList<string> Dns, IReadOnlyList<IPAddress> Ips) names)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={names.Dns[0]}, O=REA-Radio-DJ", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var d in names.Dns) san.AddDnsName(d);
        foreach (var ip in names.Ips) san.AddIpAddress(ip);
        request.CertificateExtensions.Add(san.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));   // server authentication
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        // 397 days: the longest a browser accepts for a server certificate.
        var notAfter = DateTimeOffset.Now.AddDays(397);
        if (notAfter > ca.NotAfter) notAfter = ca.NotAfter.AddDays(-1);
        using var signed = request.Create(ca, DateTimeOffset.Now.AddDays(-1), notAfter, serial);
        return signed.CopyWithPrivateKey(key);
    }

    /// <summary>The names and addresses of this computer: what a phone types in the address bar.</summary>
    public static (IReadOnlyList<string> Dns, IReadOnlyList<IPAddress> Ips) Names()
    {
        string host = Environment.MachineName.ToLowerInvariant();
        var dns = new List<string> { host, host + ".local", "localhost" };
        var ips = new List<IPAddress> { IPAddress.Loopback };
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !ips.Contains(a.Address))
                        ips.Add(a.Address);
        }
        catch (NetworkInformationException) { }
        return (dns, ips);
    }

    private static bool Covers(X509Certificate2 cert, (IReadOnlyList<string> Dns, IReadOnlyList<IPAddress> Ips) names)
    {
        var ext = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
        if (ext is null) return false;
        var dns = ext.EnumerateDnsNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ips = ext.EnumerateIPAddresses().ToHashSet();
        return names.Dns.All(dns.Contains) && names.Ips.All(ips.Contains);
    }
}
