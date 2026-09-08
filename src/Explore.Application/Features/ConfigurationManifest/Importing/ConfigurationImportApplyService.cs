namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Domain;
using Microsoft.Extensions.Logging;

public enum ConfigurationImportEffectStatus
{
    Pending,
    Processing,
    Completed,
    DeadLettered,
    Unknown
}

public sealed record ConfigurationImportOperationResult(
    Guid OperationId,
    Guid SessionId,
    ConfigurationImportOperationKind Kind,
    ConfigurationImportOperationStatus Status,
    ConfigurationImportScope TargetScope,
    Guid? TargetTenantId,
    Guid? SourceOperationId,
    ImmutableArray<string> SelectedSectionKeys,
    bool SnapshotAvailable,
    ConfigurationImportEffectStatus EffectStatus,
    int EffectRetryCount,
    bool FidelityVerified,
    string FidelityDigest,
    ImmutableArray<string> OmittedSectionKeys,
    DateTime CompletedAt)
{
    public override string ToString() => nameof(ConfigurationImportOperationResult);
}

public sealed record ConfigurationImportRollbackSessionCreatedResult(
    Guid SourceOperationId,
    ConfigurationImportSessionCreatedResult Session)
{
    public override string ToString() =>
        nameof(ConfigurationImportRollbackSessionCreatedResult);
}

public sealed record ConfigurationImportHistoryResult(
    ImmutableArray<ConfigurationImportOperationResult> Operations)
{
    public override string ToString() => nameof(ConfigurationImportHistoryResult);
}

public sealed class ConfigurationImportApplyService(
    ConfigurationImportSessionManager sessionManager,
    IConfigurationImportSessionRepository sessions,
    IConfigurationImportArtifactStore artifacts,
    IConfigurationImportOperationRepository operations,
    ConfigurationImportSessionApplicationService previews,
    ConfigurationImportPreviewComposer previewComposer,
    ConfigurationImportArtifactParser parser,
    ConfigurationImportSectionApplier sectionApplier,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IConfigurationImportEffectOutboxRepository outbox,
    IConfigurationImportEffectDelivery effectDelivery,
    ConfigurationManagedApplyScheduleService managedSchedules,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ConfigurationImportApplyService> logger)
{
    public Task<ConfigurationImportOperationResult> ApplyInstanceAsync(
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            sessionId,
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            request,
            sourceOperationId,
            managedScheduleId,
            cancellationToken);

    public Task<ConfigurationImportOperationResult> ApplyTenantAsync(
        Guid tenantId,
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            sessionId,
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            request,
            sourceOperationId,
            managedScheduleId,
            cancellationToken);

    public async Task<ConfigurationImportRollbackSessionCreatedResult>
        CreateRollbackSessionAsync(
        Guid operationId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken)
    {
        _ = Actor();
        ConfigurationImportOperation operation =
            await operations.GetByIdAsync(
                operationId,
                target.AuthorityKey,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.RollbackUnavailable);
        DateTime now = UtcNow();
        if (operation.SnapshotArtifactHandleId is not { } handleId
            || operation.SnapshotExpiresAt is not { } expiresAt
            || expiresAt <= now)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.RollbackUnavailable);
        }

        ReadOnlyMemory<byte> snapshot;
        try
        {
            snapshot = await artifacts.ReadAsync(
                new ConfigurationImportArtifactHandle(handleId),
                cancellationToken);
        }
        catch (ConfigurationImportSessionException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.RollbackUnavailable);
        }

        ConfigurationImportSessionCreated created =
            await sessionManager.CreateAsync(
                target,
                snapshot,
                now,
                ConfigurationImportSessionLimits.DefaultSessionLifetime,
                cancellationToken);
        ConfigurationImportSessionCreatedResult mapped =
            Map(created, operation.SelectedSectionKeys);
        return new ConfigurationImportRollbackSessionCreatedResult(
            operationId,
            mapped with
            {
                AccessToken = $"{operationId:D}.{mapped.AccessToken}"
            });
    }

    public async Task<ConfigurationImportOperationResult> GetReceiptAsync(
        Guid operationId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken)
    {
        _ = Actor();
        ConfigurationImportOperation operation =
            await operations.GetByIdAsync(
                operationId,
                target.AuthorityKey,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        return await MapAsync(operation, cancellationToken);
    }

    public async Task<ImmutableArray<ConfigurationImportOperationResult>> ListAsync(
        ConfigurationImportTarget target,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        _ = Actor();
        IReadOnlyList<ConfigurationImportOperation> history =
            await operations.ListAsync(
                target.AuthorityKey,
                maximumCount,
                cancellationToken);
        var result = ImmutableArray.CreateBuilder<ConfigurationImportOperationResult>(
            history.Count);
        foreach (ConfigurationImportOperation operation in history)
            result.Add(await MapAsync(operation, cancellationToken));
        return result.MoveToImmutable();
    }

    private async Task<ConfigurationImportOperationResult> ApplyAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        CancellationToken cancellationToken)
    {
        Validate(request);
        Guid actorUserId = Actor();
        DateTime startedAt = UtcNow();
        Guid operationId = Guid.CreateVersion7();
        ConfigurationImportAuthorizedArtifact initial =
            await sessionManager.ReadArtifactForPreviewAsync(
                sessionId,
                target,
                accessToken,
                startedAt,
                cancellationToken);
        IReadOnlyList<IReadOnlyList<string>> lockGroups =
            await sectionApplier.CompileLockGroupsAsync(
                target,
                initial.Bytes,
                request,
                parser,
                cancellationToken);
        try
        {
            ConfigurationImportOperation operation =
                await mutationLock.ExecuteOrderedGroupsAsync(
                    lockGroups,
                    token => unitOfWork.ExecuteSerializableAsync(
                        innerToken => ApplyInsideTransactionAsync(
                            operationId,
                            sessionId,
                            target,
                            accessToken,
                            request,
                            sourceOperationId,
                            managedScheduleId,
                            actorUserId,
                            startedAt,
                            innerToken),
                        token),
                    cancellationToken);
            try
            {
                if (operation.EffectOutboxId is { } messageId)
                    await effectDelivery.DeliverAsync(messageId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Configuration import operation {OperationId} committed with pending effect type {ExceptionType}.",
                    operation.Id,
                    exception.GetType().Name);
            }
            return await MapAsync(operation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ConfigurationImportSessionException exception)
        {
            await RecordFailureAsync(
                operationId,
                sessionId,
                target,
                actorUserId,
                sourceOperationId,
                initial.Digest,
                request,
                exception.FailureCode,
                startedAt,
                cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Configuration import operation {OperationId} failed with exception type {ExceptionType}.",
                operationId,
                exception.GetType().Name);
            await RecordFailureAsync(
                operationId,
                sessionId,
                target,
                actorUserId,
                sourceOperationId,
                initial.Digest,
                request,
                ConfigurationImportFailureCodes.ApplyFailed,
                startedAt,
                cancellationToken);
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyFailed);
        }
    }

    private async Task<ConfigurationImportOperation> ApplyInsideTransactionAsync(
        Guid operationId,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        Guid actorUserId,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        DateTime now = UtcNow();
        string sessionAccessToken = ResolveSessionAccessToken(
            accessToken,
            sourceOperationId);
        string tokenDigest = ConfigurationImportSessionManager.DigestToken(
            sessionAccessToken);
        ConfigurationImportSession session = await sessions.GetForUpdateAsync(
                sessionId,
                target,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactMissing);
        session.AuthorizePreview(target, tokenDigest, now);
        ReadOnlyMemory<byte> sourceBytes = await artifacts.ReadAsync(
            session.Artifact.Handle,
            cancellationToken);
        if (!string.Equals(
                ConfigurationImportDigest.ComputeBytes(sourceBytes.Span),
                session.ArtifactDigest,
                StringComparison.Ordinal))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);
        }

        if (sourceOperationId is { } rollbackSource)
        {
            ConfigurationImportOperation sourceOperation =
                await operations.GetByIdAsync(
                    rollbackSource,
                    target.AuthorityKey,
                    cancellationToken)
                ?? throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.RollbackUnavailable);
            if (!string.Equals(
                    sourceOperation.SnapshotDigest,
                    session.ArtifactDigest,
                    StringComparison.Ordinal))
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.RollbackUnavailable);
            }
        }

        var authorized = new ConfigurationImportAuthorizedArtifact(
            sourceBytes,
            session.ArtifactDigest,
            session.ExpiresAt);
        ConfigurationImportPreviewPreparation preparation =
            await previews.PreparePreviewAsync(
                target,
                authorized,
                request,
                cancellationToken);
        ConfigurationImportPreview freshPreview =
            previewComposer.Compose(preparation.Input);
        if (!freshPreview.IsApplyReady)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        }
        if (session.PreviewBinding is not { } persisted
            || !persisted.Matches(freshPreview.Binding))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.StalePreview);
        }

        if (managedScheduleId is { } scheduleId)
        {
            await managedSchedules.AuthorizeApplyAsync(
                scheduleId,
                target,
                session.ArtifactDigest,
                freshPreview.Binding,
                actorUserId,
                now,
                cancellationToken);
        }

        DateTime snapshotExpiresAt = now.Add(
            ConfigurationImportSessionLimits.SnapshotRetention);
        ConfigurationImportArtifactReference snapshot = await artifacts.StoreAsync(
            new ConfigurationImportArtifactHandle(Guid.CreateVersion7()),
            preparation.CurrentTargetArtifact,
            now,
            snapshotExpiresAt,
            cancellationToken);
        session.Consume(freshPreview.Binding, target, tokenDigest, now);
        await sessions.UpdateAsync(session, cancellationToken);
        await sectionApplier.ApplyAsync(
            target,
            sourceBytes,
            request,
            actorUserId,
            now,
            session.ArtifactDigest,
            parser,
            cancellationToken);

        ConfigurationImportPreviewPreparation verificationPreparation =
            await previews.PreparePreviewAsync(
                target,
                authorized,
                request,
                cancellationToken);
        ConfigurationImportPreview verification =
            previewComposer.Compose(verificationPreparation.Input);
        var selected = request.SelectedSectionKeys.ToHashSet(StringComparer.Ordinal);
        bool fidelityVerified = verification.Items
            .Where(item => selected.Contains(item.SectionKey))
            .All(item => item.Category is
                ConfigurationImportPreviewCategory.Unchanged
                or ConfigurationImportPreviewCategory.Mapped);
        if (!fidelityVerified)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyFailed);
        }
        string fidelityDigest = ConfigurationImportDigest.Compute(
            verification.Items.Select(item =>
                $"{item.SectionKey}\u001f{(int)item.Category}\u001f{item.ReasonCode}"));
        string[] omittedSections = verification.Items
            .Where(item => item.Category == ConfigurationImportPreviewCategory.Omitted)
            .Select(item => item.SectionKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Guid outboxId = Guid.CreateVersion7();
        ConfigurationImportOperation operation =
            ConfigurationImportOperation.CreateApplied(
                operationId,
                sessionId,
                target.AuthorityKey,
                target.TenantId,
                actorUserId,
                sourceOperationId,
                session.ArtifactDigest,
                freshPreview.Binding.TargetRevisionDigest,
                freshPreview.Binding.SelectedSectionsDigest,
                freshPreview.Binding.MappingDigest,
                freshPreview.Binding.RequiredApprovalDigest,
                (int)freshPreview.Binding.ApplyMode,
                request.SelectedSectionKeys,
                snapshot.Handle.Id,
                snapshot.Sha256Digest,
                snapshot.ExpiresAt,
                outboxId,
                fidelityVerified,
                fidelityDigest,
                omittedSections,
                startedAt,
                UtcNow());
        await operations.AddAsync(operation, cancellationToken);
        await outbox.Create(ConfigurationImportEffectOutbox.Create(
            outboxId,
            operationId,
            operation.CompletedAt!.Value));
        return operation;
    }

    private async Task RecordFailureAsync(
        Guid operationId,
        Guid sessionId,
        ConfigurationImportTarget target,
        Guid actorUserId,
        Guid? sourceOperationId,
        string artifactDigest,
        ConfigurationImportPreviewRequest request,
        string failureCode,
        DateTime startedAt,
        CancellationToken cancellationToken)
    {
        if (failureCode is ConfigurationImportFailureCodes.TokenInvalid
            or ConfigurationImportFailureCodes.TargetMismatch
            or ConfigurationImportFailureCodes.ArtifactMissing)
        {
            return;
        }
        try
        {
            await operations.AddAsync(
                ConfigurationImportOperation.CreateFailed(
                    operationId,
                    sessionId,
                    target.AuthorityKey,
                    target.TenantId,
                    actorUserId,
                    sourceOperationId,
                    artifactDigest,
                    (int)request.ApplyMode,
                    request.SelectedSectionKeys,
                    failureCode,
                    "Configuration import failed and no selected section was committed.",
                    startedAt,
                    UtcNow()),
                cancellationToken.IsCancellationRequested
                    ? CancellationToken.None
                    : cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Configuration import failure evidence for operation {OperationId} could not be persisted: {ExceptionType}.",
                operationId,
                exception.GetType().Name);
        }
    }

    private Guid Actor()
    {
        Guid? actor = currentUser.UserId;
        return currentUser.IsAuthenticated && actor is { } id && id != Guid.Empty
            ? id
            : throw new UnauthorizedAccessException(
                "Authenticated operator context is required.");
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static void Validate(ConfigurationImportPreviewRequest request)
    {
        if (request is null
            || request.SelectedSectionKeys is null
            || request.SelectedSectionKeys.Count == 0
            || request.Mappings is null
            || request.GrantedApprovalCodes is null
            || !Enum.IsDefined(request.ApplyMode)
            || request.ApplyMode is ConfigurationImportApplyMode.PreviewOnly
                or ConfigurationImportApplyMode.ReconcileManaged)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ContractInvalid);
        }
        foreach (string section in request.SelectedSectionKeys)
        {
            if (!ConfigurationPortabilityRegistry.Sections.TryGetValue(
                    section,
                    out ConfigurationPortabilitySectionDescriptor? descriptor)
                || !descriptor.SupportsApply)
            {
                throw new ConfigurationImportSessionException(
                    ConfigurationImportFailureCodes.ApplyBlocked);
            }
        }
    }

    private static ConfigurationImportSessionCreatedResult Map(
        ConfigurationImportSessionCreated created,
        IEnumerable<string> availableSectionKeys) =>
        new(
            created.Session.SessionId,
            created.AccessToken,
            created.Session.TargetScope,
            created.Session.TargetTenantId,
            created.Session.State,
            created.Session.ExpiresAt,
            created.Session.ArtifactByteLength,
            [.. availableSectionKeys
                .Order(StringComparer.Ordinal)]);

    private static string ResolveSessionAccessToken(
        string accessToken,
        Guid? sourceOperationId)
    {
        if (sourceOperationId is null)
            return accessToken;
        string prefix = $"{sourceOperationId.Value:D}.";
        if (!accessToken.StartsWith(prefix, StringComparison.Ordinal)
            || accessToken.Length == prefix.Length)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.RollbackUnavailable);
        }
        return accessToken[prefix.Length..];
    }

    private async Task<ConfigurationImportOperationResult> MapAsync(
        ConfigurationImportOperation operation,
        CancellationToken cancellationToken)
    {
        ConfigurationImportEffectStatus effectStatus =
            ConfigurationImportEffectStatus.Unknown;
        var effectRetryCount = 0;
        if (operation.EffectOutboxId is { } messageId)
        {
            OutboxMessage? message = await outbox.GetByIdAsync(
                messageId,
                cancellationToken);
            effectStatus = message?.Status switch
            {
                OutboxMessageStatus.Pending => ConfigurationImportEffectStatus.Pending,
                OutboxMessageStatus.Processing => ConfigurationImportEffectStatus.Processing,
                OutboxMessageStatus.Completed => ConfigurationImportEffectStatus.Completed,
                OutboxMessageStatus.Failed or OutboxMessageStatus.DeadLettered =>
                    ConfigurationImportEffectStatus.DeadLettered,
                _ => ConfigurationImportEffectStatus.Unknown
            };
            effectRetryCount = message?.RetryCount ?? 0;
        }
        return new ConfigurationImportOperationResult(
            operation.Id,
            operation.SessionId,
            operation.Kind,
            operation.Status,
            operation.TargetTenantId.HasValue
                ? ConfigurationImportScope.Tenant
                : ConfigurationImportScope.Instance,
            operation.TargetTenantId,
            operation.SourceOperationId,
            [.. operation.SelectedSectionKeys],
            operation.SnapshotArtifactHandleId.HasValue
                && operation.SnapshotExpiresAt > DateTime.UtcNow,
            effectStatus,
            effectRetryCount,
            operation.FidelityVerified,
            operation.FidelityDigest,
            [.. operation.OmittedSectionKeys],
            operation.CompletedAt ?? operation.StartedAt);
    }
}
