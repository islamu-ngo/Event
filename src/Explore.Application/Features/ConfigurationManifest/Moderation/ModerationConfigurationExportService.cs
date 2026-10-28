using System.Collections.Frozen;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
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
        ConfigurationImportTarget target, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(
            token => ReadInCurrentTransactionAsync(target, token), cancellationToken);

    public async Task<ModerationConfigurationExport> ReadInCurrentTransactionAsync(
        ConfigurationImportTarget target, CancellationToken cancellationToken)
    {
        var selected = await publications.AcquireConfigurationExportAsync(target, cancellationToken);
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
                if (target.Scope == ConfigurationImportScope.Tenant && target.TenantId != tenantId
                    || !tenants.TryAdd(tenantId, document))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
            }
            else
            {
                if (target.Scope != ConfigurationImportScope.Instance || instance is not null)
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);
                instance = document;
            }
        }
        return new ModerationConfigurationExport(instance, tenants);
    }
}

public sealed record ModerationConfigurationExport
{
    public ModerationConfigurationExport(
        ConfigurationModerationDocumentV1? instance,
        IReadOnlyDictionary<Guid, ConfigurationModerationDocumentV1> tenants)
    {
        Instance = instance;
        Tenants = tenants.ToFrozenDictionary();
    }

    public ConfigurationModerationDocumentV1? Instance { get; }
    public IReadOnlyDictionary<Guid, ConfigurationModerationDocumentV1> Tenants { get; }
}
