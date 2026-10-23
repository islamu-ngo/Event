using System.Security.Cryptography;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.ServiceDefaults.DataProtection;

/// <summary>
/// Owns an isolated framework provider with exactly one read-only repository.
/// The outer host cannot add a second persistence or key-encryption option to it.
/// </summary>
public sealed class ExternalDataProtectionProvider : IDataProtectionProvider, IDisposable
{
    private readonly ServiceProvider framework;
    private readonly IDataProtectionProvider provider;
    private readonly TimeProvider clock;
    private readonly DateTimeOffset activeExpiresUtc;
    private int rejected;

    private ExternalDataProtectionProvider(ExternalDataProtectionInput input, string partition, TimeProvider clock)
    {
        this.clock = clock;
        var (admission, documents) = ExternalDataProtectionRingAdmission.Validate(input, partition, clock.GetUtcNow());
        Admission = admission;
        AdmissionSha256 = input.AdmissionSha256;
        MinimumGeneration = input.MinimumGeneration;
        activeExpiresUtc = ExternalDataProtectionRingAdmission.ReadDate(
            documents.Single(element => element.Name == "key" && Guid.Parse(element.Attribute("id")!.Value) == admission.ActiveKeyId),
            "expirationDate");
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(ExternalDataProtectionRegistration.ApplicationName(partition))
            .AddKeyManagementOptions(options =>
            {
                options.AutoGenerateKeys = false;
                options.XmlRepository = new ExternalDataProtectionXmlRepository(documents);
                options.XmlEncryptor = null;
            });
        // Deliberately no application logging providers: framework descriptor errors must not export XML.
        framework = services.BuildServiceProvider();
        try
        {
            provider = framework.GetRequiredService<IDataProtectionProvider>();
            var keys = framework.GetRequiredService<IKeyManager>().GetAllKeys();
            foreach (var entry in admission.Keys)
            {
                var key = keys.Single(item => item.KeyId == entry.Id);
                if (key.IsRevoked != entry.Revoked) throw ExternalDataProtectionRingAdmission.Invalid();
                if (entry.Revoked) continue;
                var encryptor = key.CreateEncryptor() ?? throw ExternalDataProtectionRingAdmission.Invalid();
                byte[] plaintext = RandomNumberGenerator.GetBytes(32);
                byte[] additionalData = RandomNumberGenerator.GetBytes(32);
                byte[] encrypted = encryptor.Encrypt(new ArraySegment<byte>(plaintext), new ArraySegment<byte>(additionalData));
                byte[] recovered = encryptor.Decrypt(new ArraySegment<byte>(encrypted), new ArraySegment<byte>(additionalData));
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(plaintext, recovered))
                        throw ExternalDataProtectionRingAdmission.Invalid();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                    CryptographicOperations.ZeroMemory(recovered);
                }
            }
            ProveActiveKey();
        }
        catch
        {
            framework.Dispose();
            throw ExternalDataProtectionRingAdmission.Invalid();
        }
    }

    public ExternalDataProtectionAdmission Admission { get; }
    public string AdmissionSha256 { get; }
    public long MinimumGeneration { get; }
    public int KeyCount => Admission.Keys.Length;
    public DateTimeOffset ActiveExpiresUtc => activeExpiresUtc;
    internal IReadOnlyCollection<IKey> GetRetainedKeys() => framework.GetRequiredService<IKeyManager>().GetAllKeys();

    public static ExternalDataProtectionProvider Create(
        ExternalDataProtectionInput input, string partition, TimeProvider clock) => new(input, partition, clock);

    public IDataProtector CreateProtector(string purpose) => new AdmittedProtector(this, provider.CreateProtector(purpose));

    public void RejectUntilRestart() => Interlocked.Exchange(ref rejected, 1);

    public void EnsureAdmitted()
    {
        var now = clock.GetUtcNow();
        if (Volatile.Read(ref rejected) != 0 || now < Admission.NotBeforeUtc
            || now >= Admission.NotAfterUtc || now >= activeExpiresUtc)
            throw new CryptographicException("data_protection_not_admitted");
    }

    public void ProveActiveKey()
    {
        EnsureAdmitted();
        var protector = provider.CreateProtector("Event.ExternalDataProtection.Admission.v1");
        byte[] plain = RandomNumberGenerator.GetBytes(32);
        byte[] encrypted = protector.Protect(plain);
        byte[] recovered = protector.Unprotect(encrypted);
        try
        {
            // Framework payload: four-byte magic header followed by the key ID.
            if (encrypted.Length < 20 || new Guid(encrypted.AsSpan(4, 16)) != Admission.ActiveKeyId
                || !CryptographicOperations.FixedTimeEquals(plain, recovered))
                throw ExternalDataProtectionRingAdmission.Invalid();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(recovered);
        }
    }

    public void Dispose() => framework.Dispose();

    private sealed class AdmittedProtector(ExternalDataProtectionProvider owner, IDataProtector inner) : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose) => new AdmittedProtector(owner, inner.CreateProtector(purpose));
        public byte[] Protect(byte[] plaintext)
        {
            owner.EnsureAdmitted();
            byte[] protectedData = inner.Protect(plaintext);
            if (protectedData.Length < 20 || new Guid(protectedData.AsSpan(4, 16)) != owner.Admission.ActiveKeyId)
            {
                owner.RejectUntilRestart();
                throw new CryptographicException("data_protection_active_key_changed");
            }
            return protectedData;
        }
        public byte[] Unprotect(byte[] protectedData)
        {
            owner.EnsureAdmitted();
            return inner.Unprotect(protectedData);
        }
    }
}
