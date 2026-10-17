namespace Explore.Application.Contracts.Persistence;

using Explore.Domain;

public interface IConfigurationImportLegalSourceReader
{
    Task<IReadOnlyList<LegalDocumentPortableSource>> ReadAsync(
        LegalDocumentScope scope, Guid? tenantId, CancellationToken ct);
}
