using System.Collections.Frozen;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.ConfigurationManifest.Requests.Queries;
using Explore.Domain;
using ISLAMU.Wire.Contracts.ConfigurationPortability;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Exports retained native meaning under one serializable authority acquisition. Requested support
/// is portable; source inventory, current readiness, identities and private runtime state are not.
/// </summary>
public sealed class ModerationConfigurationExportService(
    IModerationPolicyPublicationRepository publications, IUnitOfWork unitOfWork)
{
    public Task<ModerationConfigurationExport> ReadAsync(
        ConfigurationImportTarget target, ConfigurationManifestExportScope? instanceScope,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(
            token => ReadInCurrentTransactionAsync(target, instanceScope, token), cancellationToken);

    public async Task<ModerationConfigurationExport> ReadPublicManifestInCurrentTransactionAsync(
        ConfigurationManifestExportScope scope, CancellationToken cancellationToken)
    {
        var selected = await publications.AcquirePublicManifestExportAsync(scope, cancellationToken);
        return Project(selected, scope, null);
    }

    public async Task<ModerationConfigurationExport> ReadInCurrentTransactionAsync(
        ConfigurationImportTarget target, ConfigurationManifestExportScope? instanceScope,
        CancellationToken cancellationToken)
    {
        var selected = await publications.AcquireConfigurationExportAsync(target, instanceScope, cancellationToken);
        return Project(selected, instanceScope, target.TenantId);
    }

    /// <summary>
    /// Reads one tenant's adopted definition under its own serializable transaction on the caller's
    /// tenantless Global context. Package authority is the caller's responsibility.
    /// </summary>
    public Task<ModerationConfigurationExport> ReadTenantPackageAsync(Guid tenantId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(token => ReadTenantPackageInCurrentTransactionAsync(tenantId, token),
            cancellationToken);

    /// <summary>
    /// Reads one tenant's adopted definition inside the caller's read snapshot on a tenantless Global
    /// context; used where the provider snapshot must not take the database write lock.
    /// </summary>
    public async Task<ModerationConfigurationExport> ReadTenantPackageInCurrentTransactionAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        Project(await publications.AcquireTenantPackageExportAsync(tenantId, cancellationToken), null, tenantId);

    private static ModerationConfigurationExport Project(
        IReadOnlyList<ModerationPolicyPublication> selected, ConfigurationManifestExportScope? instanceScope,
        Guid? tenantTarget)
    {
        ConfigurationModerationDocumentV1? instance = null;
        var tenants = new Dictionary<Guid, ConfigurationModerationDocumentV1>();
        foreach (var publication in selected)
        {
            var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)
                ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            var document = ModerationConfigurationImportSections.ToWire(
                ModerationConfigurationDocumentProjection.FromCompiledDraft(draft));
            if (publication.JurisdictionTenantId is { } tenantId)
            {
                if ((instanceScope != ConfigurationManifestExportScope.InstanceAndTenants && tenantTarget != tenantId)
                    || !tenants.TryAdd(tenantId, document))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            }
            else
            {
                if (instanceScope is null || instance is not null)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                instance = document;
            }
        }
        return new ModerationConfigurationExport(instance, tenants,
            selected.Select(publication => publication.Id).Order().ToArray());
    }
}

public sealed record ModerationConfigurationExport
{
    public ModerationConfigurationExport(
        ConfigurationModerationDocumentV1? instance,
        IReadOnlyDictionary<Guid, ConfigurationModerationDocumentV1> tenants,
        IReadOnlyList<Guid> publicationIds)
    {
        Instance = instance;
        Tenants = tenants.ToFrozenDictionary();
        PublicationIds = publicationIds.ToArray();
    }

    public ConfigurationModerationDocumentV1? Instance { get; }
    public IReadOnlyDictionary<Guid, ConfigurationModerationDocumentV1> Tenants { get; }

    /// <summary>
    /// Ordered source publication ids. Every republish creates a new id, so equal ids across two reads
    /// prove the exported definitions did not change between them.
    /// </summary>
    public IReadOnlyList<Guid> PublicationIds { get; }
}
