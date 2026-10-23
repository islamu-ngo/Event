using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace Explore.ServiceDefaults.DataProtection;

public sealed class ExternalDataProtectionXmlRepository(IEnumerable<XElement> documents) : IXmlRepository
{
    private readonly XElement[] snapshot = documents.Select(element => new XElement(element)).ToArray();

    public IReadOnlyCollection<XElement> GetAllElements() => snapshot.Select(element => new XElement(element)).ToArray();

    public void StoreElement(XElement element, string friendlyName) =>
        throw new InvalidOperationException("data_protection_runtime_write_forbidden");
}
