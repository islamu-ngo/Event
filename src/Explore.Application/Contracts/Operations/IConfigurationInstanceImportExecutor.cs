using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using System.Collections.Immutable;

namespace Explore.Application.Contracts.Operations;

/// <summary>Runs authorized instance import work on its dedicated database owner.</summary>
public interface IConfigurationInstanceImportExecutor
{
    Task<ConfigurationImportSessionCreatedResult> CreateAsync(
        ReadOnlyMemory<byte> artifact, CancellationToken cancellationToken);

    Task CancelAsync(Guid sessionId, string accessToken, CancellationToken cancellationToken);

    Task<ConfigurationManifestExportResult> ReadSnapshotAsync(
        ConfigurationManifestExportView view, CancellationToken cancellationToken);

    Task<ConfigurationImportPreviewResult> PreviewAsync(
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken);

    Task<ConfigurationImportOperationResult> ApplyAsync(
        Guid sessionId, string accessToken, ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId, Guid? managedScheduleId, CancellationToken cancellationToken);

    Task<ConfigurationImportOperationResult> GetReceiptAsync(
        Guid operationId, CancellationToken cancellationToken);

    Task<ImmutableArray<ConfigurationImportOperationResult>> ListAsync(
        int maximumCount, CancellationToken cancellationToken);

    Task<ConfigurationImportRollbackSessionCreatedResult> CreateRollbackSessionAsync(
        Guid operationId, CancellationToken cancellationToken);
}
