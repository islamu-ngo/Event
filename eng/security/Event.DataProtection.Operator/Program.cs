using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Event.DataProtection.Operator;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption;
using Microsoft.AspNetCore.DataProtection.AuthenticatedEncryption.ConfigurationModel;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

try
{
    if (args.Length < 2) throw new InvalidOperationException();
    string operation = args[0];
    string partition = args[1];
    string prefix = ExternalDataProtectionRegistration.EnvironmentPrefix(partition);
    var repository = new OperatorRepository();
    ExternalDataProtectionAdmission? previous = null;
    Guid activeId = Guid.Empty;
    long generation = 1;
    if (operation == "create")
    {
        if (args.Length != 3 || args[2] != "--fresh-empty-deployment"
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(prefix + "_ADMISSION_SHA256"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(prefix + "_MINIMUM_GENERATION")))
            throw new InvalidOperationException();
    }
    else
    {
        if (!Console.IsInputRedirected) throw new InvalidOperationException();
        using var input = Console.OpenStandardInput();
        using var bounded = new MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (bounded.Length + read > 128 * 1024) throw new InvalidOperationException();
            bounded.Write(buffer, 0, read);
        }
        var bundle = JsonSerializer.Deserialize<OperatorBundle>(bounded.ToArray())
            ?? throw new InvalidOperationException();
        string pin = Environment.GetEnvironmentVariable(prefix + "_ADMISSION_SHA256") ?? string.Empty;
        if (!long.TryParse(Environment.GetEnvironmentVariable(prefix + "_MINIMUM_GENERATION"),
                NumberStyles.None, CultureInfo.InvariantCulture, out long floor))
            throw new InvalidOperationException();
        var history = ExternalDataProtectionRecovery.ReadPinnedHistory(
            new(bundle.Ring, bundle.Admission, pin, floor), partition);
        previous = history.Admission;
        foreach (var document in history.Documents) repository.StoreElement(document, string.Empty);
        activeId = previous.ActiveKeyId;
        generation = checked(previous.Generation + 1);
        if (operation == "status")
        {
            if (args.Length != 2) throw new InvalidOperationException();
            Console.Error.WriteLine($"partition={partition} generation={previous.Generation} keys={previous.Keys.Length} admittedUntilUtc={previous.NotAfterUtc:O}");
            Console.Error.WriteLine("Pinned inventory only; runtime readiness, complete historical custody and replica convergence are not certified.");
            return 0;
        }
    }

    if (!Console.IsOutputRedirected) throw new InvalidOperationException();
    var services = new ServiceCollection();
    services.AddDataProtection()
        .UseCryptographicAlgorithms(new AuthenticatedEncryptorConfiguration
        {
            EncryptionAlgorithm = EncryptionAlgorithm.AES_256_CBC,
            ValidationAlgorithm = ValidationAlgorithm.HMACSHA256
        })
        .AddKeyManagementOptions(options =>
        {
            options.XmlRepository = repository;
            options.AutoGenerateKeys = false;
            options.XmlEncryptor = null;
        });
    using var provider = services.BuildServiceProvider();
    var manager = provider.GetRequiredService<IKeyManager>();
    var now = DateTimeOffset.UtcNow;
    switch (operation)
    {
        case "create":
        case "rotate":
            if (operation == "rotate" && args.Length != 2) throw new InvalidOperationException();
            activeId = manager.CreateNewKey(now, now.AddDays(90)).KeyId;
            break;
        case "prepare":
            if (args.Length != 3 || !DateTimeOffset.TryParseExact(args[2], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var activation)
                || activation < now.AddHours(2) || activation > now.AddDays(1))
                throw new InvalidOperationException();
            manager.CreateNewKey(activation, activation.AddDays(90));
            break;
        case "activate":
            if (args.Length != 3 || !Guid.TryParse(args[2], out activeId)) throw new InvalidOperationException();
            break;
        case "revoke":
            if (args.Length != 4 || args[3] != "--invalidate-dependent-values"
                || !Guid.TryParse(args[2], out Guid revoked) || revoked == activeId
                || !manager.GetAllKeys().Any(key => key.KeyId == revoked && !key.IsRevoked))
                throw new InvalidOperationException();
            manager.RevokeKey(revoked, "operator-approved-invalidation");
            break;
        case "renew":
            if (args.Length != 2) throw new InvalidOperationException();
            break;
        default:
            throw new InvalidOperationException();
    }

    var keys = manager.GetAllKeys();
    var issuedAt = DateTimeOffset.UtcNow;
    var expires = issuedAt.AddHours(12);
    var futureActivation = keys.Where(key => !key.IsRevoked && key.ActivationDate > issuedAt)
        .Select(key => key.ActivationDate).DefaultIfEmpty(expires).Min();
    if (futureActivation < expires) expires = futureActivation;
    var active = keys.Single(key => key.KeyId == activeId && !key.IsRevoked);
    if (active.ExpirationDate < expires) expires = active.ExpirationDate;
    var documents = repository.GetAllElements();
    if (documents.Count > 16) throw new InvalidOperationException();
    string ring = JsonSerializer.Serialize(new ExternalDataProtectionRing(1, partition, generation,
        documents.Select(element => element.ToString(SaveOptions.DisableFormatting)).ToArray()));
    var inventory = documents.Where(element => element.Name == "key")
        .Select(element =>
        {
            var id = Guid.Parse(element.Attribute("id")!.Value);
            return new ExternalDataProtectionKeyInventory(id,
                ExternalDataProtectionInput.Hash(element.ToString(SaveOptions.DisableFormatting)),
                keys.Single(key => key.KeyId == id).IsRevoked);
        }).ToArray();
    if (previous is not null && previous.Keys.Any(old => !inventory.Any(current =>
            current.Id == old.Id && current.Sha256 == old.Sha256 && (!old.Revoked || current.Revoked))))
        throw new InvalidOperationException();
    string admission = JsonSerializer.Serialize(new ExternalDataProtectionAdmission(1, partition, generation,
        ExternalDataProtectionInput.Hash(ring), activeId, issuedAt, expires, inventory));
    string admissionPin = ExternalDataProtectionInput.Hash(admission);
    var candidate = new ExternalDataProtectionInput(ring, admission, admissionPin, generation);
    using var proof = ExternalDataProtectionProvider.Create(candidate, partition, TimeProvider.System);
    // This is candidate mechanics, not proof that the initial operator inventory was complete.
    Console.Error.WriteLine($"candidate partition={partition} generation={generation} keys={inventory.Length} admittedUntilUtc={expires:O}");
    Console.Error.WriteLine($"{prefix}_ADMISSION_SHA256={admissionPin}");
    Console.Error.WriteLine($"{prefix}_MINIMUM_GENERATION={generation}");
    Console.Error.WriteLine("Protect the new pin/floor independently; provision both authority values, restart/drain every replica, then admit traffic.");
    Console.Write(JsonSerializer.Serialize(new OperatorBundle(ring, admission)));
    return 0;
}
catch
{
    Console.Error.WriteLine("data_protection_operator_rejected; discard any partial output. Preserve existing authority and backups.");
    return 1;
}

namespace Event.DataProtection.Operator
{
    internal sealed record OperatorBundle(string Ring, string Admission);

    internal sealed class OperatorRepository : IXmlRepository
    {
        private readonly List<XElement> documents = [];
        public IReadOnlyCollection<XElement> GetAllElements() => documents.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => documents.Add(new XElement(element));
    }
}
