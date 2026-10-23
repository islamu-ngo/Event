using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;

namespace Explore.Application.Features.ConfigurationManifest.Moderation;

/// <summary>
/// Native-only portability entry point. Read-only preview owns a serializable read transaction;
/// apply is exclusively a participant in the original import transaction. Wire registry and
/// schema support must not be advertised until their artifact/session integration exists.
/// </summary>
public sealed class ModerationConfigurationImportAdapter(
    IModerationPolicyPublicationRepository publications, IUnitOfWork unitOfWork,
    ModerationCapabilityRegistry inventory)
{
    public Task<ModerationConfigurationImportSetPreview> PreviewSetAsync(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact, ConfigurationImportPreviewRequest request,
        ModerationConfigurationImportReservationSet? reservation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(token =>
            PreviewSetInCurrentTransactionAsync(target, artifact, request, reservation, token), cancellationToken);

    public async Task<ModerationConfigurationImportSetPreview> PreviewSetInCurrentTransactionAsync(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact, ConfigurationImportPreviewRequest request,
        ModerationConfigurationImportReservationSet? reservation, CancellationToken cancellationToken)
    {
        var sources = ModerationConfigurationImportSections.ReadSelected(target, artifact, request);
        ValidateSetBinding(target, artifact, request, reservation);
        if (sources.Count == 0 || reservation is not null && sources.Count != reservation.Entries.Count)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        var previews = new List<ModerationConfigurationTargetPreview>();
        foreach (var source in sources.OrderBy(source => source.SectionKey, StringComparer.Ordinal)
                     .ThenBy(source => source.TargetName, StringComparer.Ordinal))
        {
            var retained = reservation?.Entries.SingleOrDefault(entry =>
                entry.SectionKey == source.SectionKey && entry.SourceName == source.SourceName);
            Guid? tenantId = source.SectionKey == ModerationConfigurationImportSections.Instance ? null
                : target.Scope == ConfigurationImportScope.Tenant ? target.TenantId
                : (await publications.AcquireInstanceConfigurationImportTargetAsync(source.TargetName, cancellationToken)).Id;
            if (reservation is not null && (retained is null || retained.TargetTenantId != tenantId
                || retained.TargetName != source.TargetName))
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            var destination = target.Scope == ConfigurationImportScope.Instance && tenantId is { } id
                ? await publications.AcquireInstanceConfigurationImportDestinationAsync(
                    id, retained?.Definition.BaselinePublicationId, cancellationToken)
                : await publications.AcquireConfigurationImportDestinationAsync(
                    target, retained?.Definition.BaselinePublicationId, cancellationToken);
            var preview = ComposeReserved(destination, source.Document, inventory, retained?.Definition);
            if (retained is not null && preview.Reserve().Digest != retained.Definition.Digest)
                throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
            var entry = new ModerationConfigurationReservedTarget(source.SectionKey, source.SourceName,
                source.TargetName, tenantId, retained?.OperationId ?? Guid.CreateVersion7(), preview.Reserve());
            previews.Add(new(entry, preview));
        }
        var allocated = new ModerationConfigurationImportReservationSet(target.AuthorityKey,
            ConfigurationImportDigest.ComputeBytes(artifact.Span), ConfigurationImportDigest.Compute(request.SelectedSectionKeys),
            ConfigurationImportDigest.Compute(request.Mappings.Select(pair => $"{pair.Key}\u001f{pair.Value}")),
            previews.Select(preview => preview.Target).ToArray());
        if (reservation is not null && allocated.Digest != reservation.Digest)
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
        return new(allocated, previews);
    }

    public Task<IModerationConfigurationImportCompletion> ApplySetInCurrentTransactionAsync(
        ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact, ConfigurationImportPreviewRequest request,
        ModerationConfigurationImportReservationSet reservation, CancellationToken cancellationToken)
    {
        ValidateSetBinding(target, artifact, request, reservation);
        return publications.ApplyConfigurationImportSetInCurrentTransactionAsync(target,
            ModerationConfigurationImportSections.ReadSelected(target, artifact, request), reservation, cancellationToken);
    }

    private static void ValidateSetBinding(ConfigurationImportTarget target, ReadOnlyMemory<byte> artifact,
        ConfigurationImportPreviewRequest request, ModerationConfigurationImportReservationSet? reservation)
    {
        if (reservation is not null && (reservation.TargetAuthorityKey != target.AuthorityKey
            || reservation.ArtifactDigest != ConfigurationImportDigest.ComputeBytes(artifact.Span)
            || reservation.SelectionDigest != ConfigurationImportDigest.Compute(request.SelectedSectionKeys)
            || reservation.MappingDigest != ConfigurationImportDigest.Compute(request.Mappings.Select(pair => $"{pair.Key}\u001f{pair.Value}"))))
            throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Conflict);
    }

    /// <summary>
    /// Exports current adopted meaning, including unsupported definitions, never readiness.
    /// Null means the independently authorized destination has no selected policy.
    /// </summary>
    public Task<ModerationConfigurationDocument?> ExportAsync(
        ConfigurationImportTarget target, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(token =>
            ExportInCurrentTransactionAsync(target, token), cancellationToken);

    public async Task<ModerationConfigurationDocument?> ExportInCurrentTransactionAsync(
        ConfigurationImportTarget target, CancellationToken cancellationToken)
    {
        var destination = await publications.AcquireConfigurationImportDestinationAsync(
            target, null, cancellationToken);
        var current = Restore(destination.Current);
        return current is null ? null : ModerationConfigurationDocumentProjection.FromCompiledDraft(current);
    }

    public Task<ModerationConfigurationImportPreview> ValidateOnlyAsync(
        ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        Guid? baselinePublicationId, CancellationToken cancellationToken) =>
        PreviewAsync(target, incoming, baselinePublicationId, cancellationToken);

    public Task<ModerationConfigurationImportPreview> PreviewAsync(
        ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        Guid? baselinePublicationId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(token =>
            PreviewInCurrentTransactionAsync(target, incoming, baselinePublicationId, token), cancellationToken);

    public async Task<ModerationConfigurationImportPreview> PreviewInCurrentTransactionAsync(
        ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        Guid? baselinePublicationId, CancellationToken cancellationToken)
    {
        var destination = await publications.AcquireConfigurationImportDestinationAsync(
            target, baselinePublicationId, cancellationToken);
        return Compose(destination, incoming, inventory);
    }

    public Task<ModerationPolicyPublication> ApplyInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportPreview preview, CancellationToken cancellationToken) =>
        publications.ApplyConfigurationImportInCurrentTransactionAsync(
            operationId, target, incoming, preview, cancellationToken);

    /// <summary>
    /// Pure mapping of acquired entities. Even a fabricated destination can only produce a preview,
    /// never a publication: apply calls this again with independently reacquired native entities.
    /// </summary>
    public static ModerationConfigurationImportPreview Compose(
        ModerationConfigurationImportDestination destination, ModerationConfigurationDocument incoming,
        ModerationCapabilityRegistry inventory, ModerationConfigurationImportPreview? expected = null) =>
        ComposeReserved(destination, incoming, inventory, expected?.Reserve());

    public static ModerationConfigurationImportPreview ComposeReserved(
        ModerationConfigurationImportDestination destination, ModerationConfigurationDocument incoming,
        ModerationCapabilityRegistry inventory, ModerationConfigurationImportReservation? expected = null)
    {
        ModerationConfigurationDocumentValidator.ValidateShape(incoming);
        var current = Restore(destination.Current);
        var baseline = Restore(destination.Baseline);
        var merge = current is null
            ? new ModerationConfigurationMerge(incoming, ModerationConfigurationDocumentDiff.Initial(incoming))
            : ModerationConfigurationDocumentDiff.Merge(
                ModerationConfigurationDocumentProjection.FromCompiledDraft(baseline
                    ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable)),
                ModerationConfigurationDocumentProjection.FromCompiledDraft(current), incoming);
        // A conflicted document is withheld in full; no partial profile mapping is applied.
        var document = merge.Document;
        var profiles = new Dictionary<ModerationConfigurationReference, Guid>();
        if (document is not null)
        {
            foreach (var profile in document.Profiles)
            {
                var retained = current?.Profiles.SingleOrDefault(item =>
                    item.Definition.Rule.Code == profile.Rule.Code && item.Definition.Rule.Revision == profile.Rule.Revision);
                var id = retained?.Definition.Id
                    ?? expected?.Profiles.SingleOrDefault(item =>
                        item.Code == profile.Rule.Code && item.Revision == profile.Rule.Revision)?.ProfileId
                    ?? Guid.CreateVersion7();
                if (!profiles.TryAdd(profile.Rule, id))
                    throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Invalid);
            }
        }
        var mapping = new ModerationConfigurationDestinationMapping(
            destination.Selection?.Id ?? expected?.PolicyId ?? Guid.CreateVersion7(),
            destination.Selection?.Revision ?? 0,
            expected?.PublicationId ?? Guid.CreateVersion7(),
            new(destination.Host.Id, destination.Epoch.Id, destination.Epoch.Revision,
                ModerationPolicyPublicationProjection.WriterGeneration,
                ModerationPolicyPublicationProjection.AuthoringBounds(destination.Jurisdiction), current), profiles);
        var validation = document is null ? null
            : new ModerationConfigurationDocumentValidator().ValidateOnly(document, mapping, inventory);
        var binding = ModerationPolicyPublicationProjection.Digest(new
        {
            destination.PrincipalId, destination.TenantId, destination.Jurisdiction, destination.AdministrationBinding,
            HostId = destination.Host.Id, destination.Host.TrustDomainId, destination.Host.SolutionId,
            destination.Host.InstanceId, destination.Host.MaintenanceServicePrincipalId,
            EpochId = destination.Epoch.Id, EpochRevision = destination.Epoch.Revision,
            RequestEpochId = destination.RequestEpoch?.Id, RequestEpochRevision = destination.RequestEpoch?.Revision,
            PolicyId = mapping.PolicyId, mapping.ExpectedRevision, mapping.PublicationId,
            Latest = destination.Selection?.LatestPublicationId, Active = destination.Selection?.ActivePublicationId,
            Baseline = destination.Baseline?.Id, BaselineDigest = destination.Baseline?.DeclaredStateDigest,
            CurrentDigest = destination.Current?.DeclaredStateDigest,
            ModerationPolicyPublicationProjection.WriterGeneration,
            InventoryRevision = inventory.Revision, InventoryFingerprint = inventory.Fingerprint,
            IncomingDigest = Digest(incoming), DocumentDigest = document is null ? null : Digest(document),
            Profiles = profiles.OrderBy(item => item.Key.Code, StringComparer.Ordinal).ThenBy(item => item.Key.Revision)
                .Select(item => new { item.Key.Code, item.Key.Revision, Id = item.Value }).ToArray()
        });
        return new(destination.Baseline?.Id, document, merge.Diff, mapping, validation, binding);
    }

    public Task<ModerationConfigurationImportPreview> PreviewReservedAsync(
        ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportReservation? reservation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(token =>
            PreviewReservedInCurrentTransactionAsync(target, incoming, reservation, token), cancellationToken);

    public async Task<ModerationConfigurationImportPreview> PreviewReservedInCurrentTransactionAsync(
        ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportReservation? reservation, CancellationToken cancellationToken)
    {
        var destination = await publications.AcquireConfigurationImportDestinationAsync(
            target, reservation?.BaselinePublicationId, cancellationToken);
        return ComposeReserved(destination, incoming, inventory, reservation);
    }

    public Task<ModerationPolicyPublication> ApplyReservedInCurrentTransactionAsync(
        Guid operationId, ConfigurationImportTarget target, ModerationConfigurationDocument incoming,
        ModerationConfigurationImportReservation reservation, CancellationToken cancellationToken,
        IReadOnlyList<LegalDocument>? ownedLegalDocuments = null) =>
        publications.ApplyConfigurationImportReservationInCurrentTransactionAsync(
            operationId, target, incoming, reservation, ownedLegalDocuments ?? [], cancellationToken);

    private static ModerationPolicyCompiledDraft? Restore(ModerationPolicyPublication? publication) =>
        publication is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(publication)
            ?? throw new ModerationConfigurationImportException(ModerationPolicyPublicationOutcome.Unavailable);

    private static string Digest(ModerationConfigurationDocument document) =>
        Convert.ToHexString(SHA256.HashData(ModerationConfigurationDocumentProjection.Serialize(document)));
}
