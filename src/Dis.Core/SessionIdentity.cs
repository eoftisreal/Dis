using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Dis.Core;

/// <summary>A new certificate identity per server lifetime, compatible with Windows Schannel.</summary>
public sealed class SessionIdentity : IDisposable
{
    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    public SessionIdentity()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Dis Desktop", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(7));
        // CreateSelfSigned uses ephemeral keys that Schannel cannot consume on some Windows builds.
        // Import into the user's temporary key set. No PersistKeySet: disposal cleans up the imported key.
        byte[] pfx = created.Export(X509ContentType.Pfx);
        try { Certificate = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.UserKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
        Fingerprint = Convert.ToHexString(SHA256.HashData(Certificate.RawData)).ToLowerInvariant();
    }

    public void Dispose() => Certificate.Dispose();
}
