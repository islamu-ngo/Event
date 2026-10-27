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

    public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask<ExternalDataProtectionInput> ReadFreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Input);
    }

    private sealed class Repository : IXmlRepository
    {
        private readonly List<XElement> documents = [];
        public IReadOnlyCollection<XElement> GetAllElements() => documents.Select(element => new XElement(element)).ToArray();
        public void StoreElement(XElement element, string friendlyName) => documents.Add(new XElement(element));
    }
}
