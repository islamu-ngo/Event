using System.Text.Json;
using System.Xml.Linq;
using Explore.ServiceDefaults.DataProtection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Testing.DataProtection;

internal sealed class ExternalDataProtectionTestAuthority : IExternalDataProtectionSource
{
    public ExternalDataProtectionInput Input { get; set; }

    public ExternalDataProtectionTestAuthority(string partition, TimeProvider? clock = null)
    {
        var repository = new Repository();
        var services = new ServiceCollection();
        services.AddDataProtection().AddKeyManagementOptions(options =>
        {
            options.XmlRepository = repository;
            options.AutoGenerateKeys = false;
        });
        using var provider = services.BuildServiceProvider();
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var key = provider.GetRequiredService<IKeyManager>().CreateNewKey(now, now.AddDays(90));
        string xml = repository.GetAllElements().Single().ToString(SaveOptions.DisableFormatting);
        string ring = JsonSerializer.Serialize(new ExternalDataProtectionRing(1, partition, 1, [xml]));
        string admission = JsonSerializer.Serialize(new ExternalDataProtectionAdmission(1, partition, 1,
            ExternalDataProtectionInput.Hash(ring), key.KeyId, now, now.AddHours(12),
            [new(key.KeyId, ExternalDataProtectionInput.Hash(xml), false)]));
        Input = new(ring, admission, ExternalDataProtectionInput.Hash(admission), 1);
    }

    public ExternalDataProtectionInput Read() => Input;

    public static ExternalDataProtectionInput RevokeActiveKey(ExternalDataProtectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ring = JsonSerializer.Deserialize<ExternalDataProtectionRing>(input.Ring)
            ?? throw new InvalidOperationException("assurance_protection_input_invalid");
        var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)
            ?? throw new InvalidOperationException("assurance_protection_input_invalid");
        var repository = new Repository();
        foreach (string document in ring.Documents)
            repository.StoreElement(XElement.Parse(document), "owned-revocation");
        var services = new ServiceCollection();
        services.AddDataProtection().AddKeyManagementOptions(options =>
        {
            options.XmlRepository = repository;
            options.AutoGenerateKeys = false;
        });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IKeyManager>().RevokeKey(admission.ActiveKeyId, "owned-assurance-revocation");
        string revokedRing = JsonSerializer.Serialize(ring with
        {
            Documents = repository.GetAllElements().Select(element => element.ToString(SaveOptions.DisableFormatting)).ToArray()
        });
        string revokedAdmission = JsonSerializer.Serialize(admission with
        {
            RingSha256 = ExternalDataProtectionInput.Hash(revokedRing),
            Keys = admission.Keys.Select(key => key.Id == admission.ActiveKeyId ? key with { Revoked = true } : key).ToArray()
        });
        return input with
        {
            Ring = revokedRing,
            Admission = revokedAdmission,
            AdmissionSha256 = ExternalDataProtectionInput.Hash(revokedAdmission)
        };
    }

    private sealed class Repository : IXmlRepository
    {
        private readonly List<XElement> documents = [];
        public IReadOnlyCollection<XElement> GetAllElements() => documents.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => documents.Add(new XElement(element));
    }
}
