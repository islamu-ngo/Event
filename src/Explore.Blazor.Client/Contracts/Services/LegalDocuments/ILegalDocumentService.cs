namespace Explore.Blazor.Client.Contracts.Services.LegalDocuments;

using Explore.Blazor.Client.Clients;

public interface ILegalDocumentService
{
    Task<PublicLegalDocumentDto?> GetAsync(
        string kindCode,
        CancellationToken cancellationToken = default);
}
