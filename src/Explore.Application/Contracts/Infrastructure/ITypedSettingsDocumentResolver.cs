namespace Explore.Application.Contracts.Infrastructure;

using Explore.Application.Settings;

public interface ITypedSettingsDocumentResolver
{
    Task<ResolvedSettingsDocument<TPayload>?> ResolveTenantDocumentAsync<TPayload>(
        SettingsResolutionContext context,
        string documentKey,
        CancellationToken cancellationToken = default)
        where TPayload : notnull;

    Task<IReadOnlyList<ResolvedSettingsDocument<TPayload>>> ResolveTenantDocumentsAsync<TPayload>(
        SettingsResolutionContext context,
        IEnumerable<string> documentKeys,
        CancellationToken cancellationToken = default)
        where TPayload : notnull;

    void InvalidateTenantDocumentCache(Guid tenantId, string? documentKey = null);
}
