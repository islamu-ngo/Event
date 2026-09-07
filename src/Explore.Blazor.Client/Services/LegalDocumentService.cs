namespace Explore.Blazor.Client.Services;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.LegalDocuments;

public sealed class LegalDocumentService(
    ILegalDocumentsClient apiClient,
    ILogger<LegalDocumentService> logger) : ILegalDocumentService
{
    public async Task<PublicLegalDocumentDto?> GetAsync(
        string kindCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kindCode);
        try
        {
            return await apiClient.GetPublicLegalDocumentAsync(
                kindCode,
                cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException exception) when (exception.StatusCode is 404 or 503)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation(
                    "Public legal document {KindCode} is unavailable with status {StatusCode}.",
                    kindCode,
                    exception.StatusCode);
            }
            return null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Public legal document {KindCode} could not be loaded.",
                kindCode);
            return null;
        }
    }
}
