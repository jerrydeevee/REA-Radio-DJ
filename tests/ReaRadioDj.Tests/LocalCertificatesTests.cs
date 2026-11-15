using System.Net;
using System.Security.Cryptography.X509Certificates;
using ReaRadioDj.Core;

namespace ReaRadioDj.Tests;

public sealed class LocalCertificatesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rea-certs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void The_engine_makes_its_own_ca_and_a_server_certificate_a_phone_can_trust()
    {
        Assert.Null(LocalCertificates.CaCertificate(_dir));
        using var server = LocalCertificates.Server(_dir);
        Assert.True(server.HasPrivateKey);

        var der = LocalCertificates.CaCertificate(_dir);
        Assert.NotNull(der);
        using var ca = X509CertificateLoader.LoadCertificate(der!);
        Assert.False(ca.HasPrivateKey);   // what can be downloaded is only the public part

        // A phone that installed the CA: the server certificate checks out against it, for the computer's addresses.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(ca);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        Assert.True(chain.Build(server), string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation)));
        var san = server.Extensions.OfType<X509SubjectAlternativeNameExtension>().Single();
        Assert.Contains("localhost", san.EnumerateDnsNames());
        Assert.Contains(IPAddress.Loopback, san.EnumerateIPAddresses());
        Assert.True(server.NotAfter <= DateTime.Now.AddDays(398));

        // Started again: the same certificates, so a phone does not need a new one.
        using var again = LocalCertificates.Server(_dir);
        Assert.Equal(server.Thumbprint, again.Thumbprint);
        Assert.Equal(der, LocalCertificates.CaCertificate(_dir));
    }
}
