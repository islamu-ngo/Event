using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Explore.ServiceDefaults.DataProtection;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class ExternalDataProtectionRingTests
{
    [Test]
    public async Task ExpiredRetainedKeyDecryptsAcrossRestartButWrongPurposeAndPartitionReject()
    {
        var documents = new MemoryRepository();
        var now = DateTimeOffset.UtcNow;
        using var generator = Generate(documents);
        var keys = generator.GetRequiredService<IKeyManager>();
        var old = keys.CreateNewKey(now.AddDays(-10), now.AddDays(1));
        using var oldProvider = Framework(documents);
        var ciphertext = oldProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("durable-destination").Protect("synthetic-destination");
        documents.ExpireKey(old.KeyId, now.AddDays(-1));
        var active = keys.CreateNewKey(now.AddHours(-1), now.AddDays(30));
        var input = Input(documents, active.KeyId);
        using var admitted = ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System);
        await Assert.That(admitted.CreateProtector("durable-destination").Unprotect(ciphertext))
            .IsEqualTo("synthetic-destination");
        await Assert.That(() => admitted.CreateProtector("cookie").Unprotect(ciphertext))
            .Throws<CryptographicException>();
        await Assert.That(() => ExternalDataProtectionProvider.Create(input, "bff", TimeProvider.System))
            .Throws<InvalidOperationException>();
        await Assert.That(admitted.KeyCount).IsEqualTo(2);
        await Assert.That(old.KeyId == active.KeyId).IsFalse();
    }

    [Test]
    public async Task OmittedHistoricalKeyCannotBeCertifiedByCandidateRing()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var keys = generator.GetRequiredService<IKeyManager>();
        keys.CreateNewKey(DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1));
        var active = keys.CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        var candidate = JsonSerializer.Deserialize<ExternalDataProtectionRing>(input.Ring)!;
        candidate = candidate with { Documents = candidate.Documents.Skip(1).ToArray() };
        string candidateJson = JsonSerializer.Serialize(candidate);
        var independentInventory = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
        string admission = JsonSerializer.Serialize(independentInventory with
        {
            RingSha256 = ExternalDataProtectionInput.Hash(candidateJson)
        });
        await Assert.That(() => ExternalDataProtectionProvider.Create(
            input with { Ring = candidateJson, Admission = admission, AdmissionSha256 = ExternalDataProtectionInput.Hash(admission) },
            "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task RevocationSurvivesRestartAndCannotBeRemovedUnderPinnedAdmission()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var keys = generator.GetRequiredService<IKeyManager>();
        var old = keys.CreateNewKey(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(5));
        using var first = Framework(documents);
        var ciphertext = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("durable")
            .Protect("synthetic");
        var active = keys.CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        keys.RevokeKey(old.KeyId, "operator-approved-invalidation");
        var input = Input(documents, active.KeyId);
        using var restarted = ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System);
        await Assert.That(() => restarted.CreateProtector("durable").Unprotect(ciphertext))
            .Throws<CryptographicException>();
        var ring = JsonSerializer.Deserialize<ExternalDataProtectionRing>(input.Ring)!;
        var noRevocation = ring with
        {
            Documents = ring.Documents.Where(xml => XElement.Parse(xml).Name == "key").ToArray()
        };
        await Assert.That(() => ExternalDataProtectionProvider.Create(
            input with { Ring = JsonSerializer.Serialize(noRevocation) }, "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task IndependentFloorRejectsCoherentlyRestoredRingAndAdmission()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        await Assert.That(() => ExternalDataProtectionProvider.Create(
            input with { MinimumGeneration = 2 }, "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
        await Assert.That(() => ExternalDataProtectionProvider.Create(
            input with { AdmissionSha256 = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) },
            "api", TimeProvider.System)).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CapabilityExpiryIsIndependentOfRetainedKeysAndRepositoryRejectsWrites()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        using var admitted = ExternalDataProtectionProvider.Create(Input(documents, active.KeyId), "api", TimeProvider.System);
        var protector = admitted.CreateProtector("capability").ToTimeLimitedDataProtector();
        var expired = protector.Protect("synthetic", DateTimeOffset.UtcNow.AddMinutes(-1));
        await Assert.That(() => protector.Unprotect(expired)).Throws<CryptographicException>();
        var repository = new ExternalDataProtectionXmlRepository(documents.GetAllElements());
        await Assert.That(() => repository.StoreElement(new XElement("key"), "forbidden"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CapacityOverflowHaltsInsteadOfDroppingHistoricalMaterial()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var keys = generator.GetRequiredService<IKeyManager>();
        IKey? active = null;
        for (var index = 0; index < 17; index++)
            active = keys.CreateNewKey(DateTimeOffset.UtcNow.AddDays(-18 + index), DateTimeOffset.UtcNow.AddDays(30 + index));
        var input = Input(documents, active!.KeyId);
        await Assert.That(() => ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ActualDurableProtectorsSurviveRetainedKeyExpiryWithoutInventedCiphertextExpiry()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var manager = generator.GetRequiredService<IKeyManager>();
        var now = DateTimeOffset.UtcNow;
        var historical = manager.CreateNewKey(now.AddDays(-10), now.AddDays(1));
        using var old = Framework(documents);
        var oldProvider = old.GetRequiredService<IDataProtectionProvider>();
        Guid tenant = Guid.CreateVersion7();
        Guid resource = Guid.CreateVersion7();
        string destination = new EventResourceDestinationProtector(oldProvider).Protect(
            "https://files.example.test/synthetic", tenant, resource, 1);
        var answer = new RegistrationSensitiveValueProtector(oldProvider).Protect("synthetic-answer");
        string evidence = new EventReportEvidenceProtector(oldProvider).Protect("synthetic-evidence");
        documents.ExpireKey(historical.KeyId, now.AddDays(-1));
        var active = manager.CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        using var restarted = ExternalDataProtectionProvider.Create(Input(documents, active.KeyId), "api", TimeProvider.System);
        await Assert.That(new EventResourceDestinationProtector(restarted).Unprotect(destination, tenant, resource, 1))
            .IsEqualTo("https://files.example.test/synthetic");
        await Assert.That(new RegistrationSensitiveValueProtector(restarted).Unprotect(answer.Ciphertext, 1))
            .IsEqualTo("synthetic-answer");
        await Assert.That(new EventReportEvidenceProtector(restarted).Unprotect(evidence)).IsEqualTo("synthetic-evidence");
        await Assert.That(() => new EventResourceDestinationProtector(restarted)
            .Unprotect(destination, Guid.CreateVersion7(), resource, 1)).Throws<CryptographicException>();
    }

    [Test]
    public async Task LeaseExpiryFailsCachedProtectorsWithoutPollingOrSleeping()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        using var provider = ExternalDataProtectionProvider.Create(input, "api", clock);
        var protector = provider.CreateProtector("durable");
        var encrypted = protector.Protect("synthetic");
        clock.Now = admission.NotAfterUtc;
        await Assert.That(() => protector.Unprotect(encrypted)).Throws<CryptographicException>();
        await Assert.That(() => protector.Protect("new")).Throws<CryptographicException>();
    }

    [Test]
    public async Task WrongMaterialTamperingAndUnsafeXmlNeverReachTheFrameworkActivator()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        var ring = JsonSerializer.Deserialize<ExternalDataProtectionRing>(input.Ring)!;
        var key = XElement.Parse(ring.Documents.Single());
        key.Element("descriptor")!.SetAttributeValue("deserializerType", typeof(object).AssemblyQualifiedName);
        string xml = key.ToString(SaveOptions.DisableFormatting);
        string candidate = JsonSerializer.Serialize(ring with { Documents = [xml] });
        var original = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
        string admission = JsonSerializer.Serialize(original with
        {
            RingSha256 = ExternalDataProtectionInput.Hash(candidate),
            Keys = [new(active.KeyId, ExternalDataProtectionInput.Hash(xml), false)]
        });
        await Assert.That(() => ExternalDataProtectionProvider.Create(
            new(candidate, admission, ExternalDataProtectionInput.Hash(admission), 1), "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
        using var provider = ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System);
        var protector = provider.CreateProtector("durable");
        byte[] ciphertext = protector.Protect([1, 2, 3]);
        ciphertext[^1] ^= 1;
        await Assert.That(() => protector.Unprotect(ciphertext)).Throws<CryptographicException>();
        var bytes = new ExternalDataProtectionInput(new string('x', 32769), input.Admission, input.AdmissionSha256, 1);
        await Assert.That(() => ExternalDataProtectionProvider.Create(bytes, "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CopiedKeyCannotCrossSplitBffDiscriminatorButExplicitBackendMoveCan()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        using var api = ExternalDataProtectionProvider.Create(input, "api", TimeProvider.System);
        byte[] ciphertext = api.CreateProtector("durable").Protect([1, 2, 3]);
        ExternalDataProtectionInput ProvisionFor(string partition)
        {
            var ring = JsonSerializer.Deserialize<ExternalDataProtectionRing>(input.Ring)!;
            var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
            string ringJson = JsonSerializer.Serialize(ring with { Partition = partition });
            string admissionJson = JsonSerializer.Serialize(admission with
            {
                Partition = partition,
                RingSha256 = ExternalDataProtectionInput.Hash(ringJson)
            });
            return new(ringJson, admissionJson, ExternalDataProtectionInput.Hash(admissionJson), 1);
        }
        using var bff = ExternalDataProtectionProvider.Create(ProvisionFor("bff"), "bff", TimeProvider.System);
        await Assert.That(() => bff.CreateProtector("durable").Unprotect(ciphertext)).Throws<CryptographicException>();
        using var combined = ExternalDataProtectionProvider.Create(ProvisionFor("combined"), "combined", TimeProvider.System);
        await Assert.That(combined.CreateProtector("durable").Unprotect(ciphertext).Length).IsEqualTo(3);
    }

    [Test]
    public async Task ExpiredServingLeaseCanBeInspectedForRecoveryButNotAdmitted()
    {
        var documents = new MemoryRepository();
        using var generator = Generate(documents);
        var active = generator.GetRequiredService<IKeyManager>()
            .CreateNewKey(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(30));
        var input = Input(documents, active.KeyId);
        var original = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)!;
        string historical = JsonSerializer.Serialize(original with
        {
            NotBeforeUtc = DateTimeOffset.UtcNow.AddHours(-14),
            NotAfterUtc = DateTimeOffset.UtcNow.AddHours(-2)
        });
        var expired = input with { Admission = historical, AdmissionSha256 = ExternalDataProtectionInput.Hash(historical) };
        await Assert.That(() => ExternalDataProtectionProvider.Create(expired, "api", TimeProvider.System))
            .Throws<InvalidOperationException>();
        var history = ExternalDataProtectionRecovery.ReadPinnedHistory(expired, "api");
        await Assert.That(history.Admission.ActiveKeyId).IsEqualTo(active.KeyId);
        await Assert.That(() => ExternalDataProtectionRecovery.ReadPinnedHistory(
            expired with { MinimumGeneration = 2 }, "api")).Throws<InvalidOperationException>();
    }

    private static ServiceProvider Generate(MemoryRepository repository) => Framework(repository, true);

    private static ServiceProvider Framework(MemoryRepository repository, bool generate = false)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().SetApplicationName(ExternalDataProtectionRegistration.ApplicationName("api"))
            .AddKeyManagementOptions(options =>
            {
                options.XmlRepository = repository;
                options.AutoGenerateKeys = generate;
            });
        return services.BuildServiceProvider();
    }

    private static ExternalDataProtectionInput Input(MemoryRepository repository, Guid active)
    {
        var elements = repository.GetAllElements();
        var ring = JsonSerializer.Serialize(new ExternalDataProtectionRing(1, "api", 1,
            elements.Select(element => element.ToString(SaveOptions.DisableFormatting)).ToArray()));
        var revoked = elements.Where(element => element.Name == "revocation")
            .Select(element => Guid.Parse(element.Element("key")!.Attribute("id")!.Value)).ToHashSet();
        var admission = JsonSerializer.Serialize(new ExternalDataProtectionAdmission(
            1, "api", 1, ExternalDataProtectionInput.Hash(ring), active,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(12),
            elements.Where(element => element.Name == "key").Select(element =>
                new ExternalDataProtectionKeyInventory(Guid.Parse(element.Attribute("id")!.Value),
                    ExternalDataProtectionInput.Hash(element.ToString(SaveOptions.DisableFormatting)),
                    revoked.Contains(Guid.Parse(element.Attribute("id")!.Value)))).ToArray()));
        return new(ring, admission, ExternalDataProtectionInput.Hash(admission), 1);
    }

    private sealed class MemoryRepository : IXmlRepository
    {
        private readonly List<XElement> elements = [];
        public IReadOnlyCollection<XElement> GetAllElements() => elements.Select(element => new XElement(element)).ToArray();
        public void ExpireKey(Guid keyId, DateTimeOffset expiration)
        {
            // Ciphertext is issued while the key is active, then retained after expiry.
            // Only lifetime metadata changes; the framework-generated material stays intact.
            elements.Single(element => element.Name == "key"
                && Guid.Parse(element.Attribute("id")!.Value) == keyId)
                .SetElementValue("expirationDate", expiration);
        }
        public void StoreElement(XElement element, string friendlyName)
        {
            var copy = new XElement(element);
            // Model a genuinely historical issuance without sleeping or changing the machine clock.
            // Cryptographic material and descriptors still come from the real framework generator.
            if (copy.Name == "key")
                copy.SetElementValue("creationDate", copy.Element("activationDate")!.Value);
            elements.Add(copy);
        }
    }

    private sealed class ControlledClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
