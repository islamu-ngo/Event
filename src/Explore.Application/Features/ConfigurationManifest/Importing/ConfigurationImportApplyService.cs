namespace Explore.Application.Features.ConfigurationManifest.Importing;

using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using ISLAMU.Wire.Contracts.ConfigurationPortability;
using Explore.Application.Features.ConfigurationManifest.Managed;
using Explore.Application.Notifications;
using Explore.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

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
    ISerializableAttemptOwner setupAttemptOwner,
    IConfigurationImportEffectOutboxRepository outbox,
    IConfigurationImportEffectDelivery effectDelivery,
    ConfigurationManagedApplyScheduleService managedSchedules,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ConfigurationImportApplyService> logger,
    IHumanCommitScopePreparation humanScope,
    IPolicyMutationExecutionScopeFactory selectedScopeFactory,
    IHumanConfigurationImportAdmission importAdmission,
    Func<IInstanceAdministratorCommitAuthority> instanceCommitAuthorityFactory,
    Func<ITenantAdministratorCommitAuthority> tenantCommitAuthorityFactory,
    Func<IHumanActionCommitFinalizer> commitFinalizerFactory,
    IConfigurationManagedApplyScheduleRepository scheduleRepository,
    Func<IHumanSecuritySourceGate> humanGateFactory,
    Func<IEventDiscoveryEnrollmentGate> discoveryGateFactory)
{
    public Task<ConfigurationImportOperationResult> ApplyInstanceAsync(
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        HumanCommitSubject subject,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            sessionId,
            ConfigurationImportTarget.ForInstance(),
            accessToken,
            request,
            subject,
            sourceOperationId,
            managedScheduleId,
            setupLive: false,
            cancellationToken);

    public Task<ConfigurationImportOperationResult> ApplyTenantAsync(
        Guid tenantId,
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        HumanCommitSubject subject,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            sessionId,
            ConfigurationImportTarget.ForTenant(tenantId),
            accessToken,
            request,
            subject,
            sourceOperationId,
            managedScheduleId,
            setupLive: false,
            cancellationToken);

    internal Task<ConfigurationImportOperationResult> ApplySetupLiveTenantAsync(
        ConfigurationImportTarget target,
        Guid sessionId,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        CancellationToken cancellationToken) =>
        ApplyAsync(sessionId, target, accessToken, request, subject: null,
            sourceOperationId: null, managedScheduleId: null, setupLive: true, cancellationToken);

    internal async Task<ConfigurationImportOperationResult> DeliverSetupLiveEffectsAsync(
        ConfigurationImportOperationResult committed, CancellationToken cancellationToken)
    {
        if (committed.Status != ConfigurationImportOperationStatus.Applied
            || committed.TargetScope != ConfigurationImportScope.Tenant || committed.TargetTenantId is not { } tenantId)
            throw new InvalidOperationException("Setup effect delivery requires its committed tenant import.");
        var operation = await operations.GetByIdAsync(committed.OperationId,
            ConfigurationImportTarget.ForTenant(tenantId).AuthorityKey, cancellationToken)
            ?? throw new InvalidOperationException("The acknowledged import operation is unavailable.");
        await DeliverEffectsAsync(operation, cancellationToken);
        return await MapAsync(operation, cancellationToken);
    }

    public Task<ConfigurationImportRollbackSessionCreatedResult>
        CreateRollbackSessionAsync(
        Guid operationId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken) =>
        ExecuteObservationAsync(target, HumanConfigurationImportObservationAccess.Update,
            (owner, token) => CreateRollbackSessionCoreAsync(owner, operationId, target, token), cancellationToken);

    private async Task<ConfigurationImportRollbackSessionCreatedResult> CreateRollbackSessionCoreAsync(
        IPreparedHumanConfigurationImport owner, Guid operationId, ConfigurationImportTarget target,
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
                owner,
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

    public Task<ConfigurationImportOperationResult> GetReceiptAsync(
        Guid operationId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken) =>
        ExecuteObservationAsync(target, HumanConfigurationImportObservationAccess.Read,
            (_, token) => GetReceiptCoreAsync(operationId, target, token), cancellationToken);

    private async Task<ConfigurationImportOperationResult> GetReceiptCoreAsync(
        Guid operationId, ConfigurationImportTarget target, CancellationToken cancellationToken)
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

    public Task<ImmutableArray<ConfigurationImportOperationResult>> ListAsync(
        ConfigurationImportTarget target,
        int maximumCount,
        CancellationToken cancellationToken) =>
        ExecuteObservationAsync(target, HumanConfigurationImportObservationAccess.Read,
            (_, token) => ListCoreAsync(target, maximumCount, token), cancellationToken);

    private Task<T> ExecuteObservationAsync<T>(ConfigurationImportTarget target,
        HumanConfigurationImportObservationAccess access,
        Func<IPreparedHumanConfigurationImport, CancellationToken, Task<T>> observe, CancellationToken cancellationToken)
    {
        _ = Actor();
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("Import history requires its original selected human.");
        var capsule = new HumanConfigurationImportObservation<T>(subject, target, access, observe);
        return importAdmission
            .ExecuteObservationAsync(capsule, cancellationToken);
    }

    private async Task<ImmutableArray<ConfigurationImportOperationResult>> ListCoreAsync(
        ConfigurationImportTarget target, int maximumCount, CancellationToken cancellationToken)
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
        HumanCommitSubject? subject,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        bool setupLive,
        CancellationToken cancellationToken)
    {
        Validate(request);
        request = new ConfigurationImportPreviewRequest
        {
            SelectedSectionKeys = request.SelectedSectionKeys.ToImmutableArray(),
            Mappings = request.Mappings.ToImmutableDictionary(StringComparer.Ordinal),
            ApplyMode = request.ApplyMode,
            GrantedApprovalCodes = request.GrantedApprovalCodes.ToImmutableArray()
        };
        if (setupLive)
        {
            if (target.Scope != ConfigurationImportScope.Tenant
                || target.GetSessionStorageKey() == target.AuthorityKey)
                throw new InvalidOperationException("Setup import requires the enrollment-bound target.");
        }
        else
        {
            ArgumentNullException.ThrowIfNull(subject);
            if (!ReferenceEquals(humanScope.Subject, subject))
                throw new UnauthorizedAccessException("Import must retain its original prepared human subject.");
        }
        IInstanceAdministratorCommitAuthority? instanceAuthority = !setupLive
            && target.Scope == ConfigurationImportScope.Instance
                ? instanceCommitAuthorityFactory() : null;
        ITenantAdministratorCommitAuthority? tenantAuthority = !setupLive
            && target.Scope == ConfigurationImportScope.Tenant
                ? tenantCommitAuthorityFactory() : null;
        IHumanActionCommitFinalizer? finalizer = !setupLive
            ? commitFinalizerFactory() : null;
        Guid actorUserId = setupLive ? Actor() : subject!.PrincipalId;
        DateTime startedAt = UtcNow();
        Guid operationId = Guid.CreateVersion7();
        string sessionAccessToken = ResolveSessionAccessToken(accessToken, sourceOperationId);
        ConfigurationImportAuthorizedArtifact initial = setupLive
            ? await sessionManager.ReadSetupLiveArtifactAsync(
                sessionId, target, sessionAccessToken, startedAt, cancellationToken)
            : await ExecuteObservationAsync(target, HumanConfigurationImportObservationAccess.Read,
                (owner, token) => sessionManager.ReadArtifactForPreviewAsync(
                    owner, sessionId, target, sessionAccessToken, startedAt, token), cancellationToken);
        IPreparedHumanConfigurationImport? prepared = null;
        bool setupCommitAcknowledged = false;
        try
        {
            var outcome = await ConfigurationImportCoordination.ExecuteAsync(
                humanGateFactory, discoveryGateFactory, async (requireCurrent, coordinatedToken) =>
            {
                var originalLocks =
                    await sectionApplier.CaptureLockGroupsAsync(
                        target,
                        initial.Bytes,
                        request,
                        parser,
                        cancellationToken);
                IReadOnlyList<IReadOnlyList<string>> lockGroups = originalLocks.Groups;
                prepared = setupLive ? null
                    : await importAdmission
                        .PrepareAsync(subject!, target, null, cancellationToken);
                var invocation = setupLive ? null : new HumanApplyInvocation(
                    operationId, sessionId, target, subject!, initial.Digest,
                    ConfigurationImportSessionManager.DigestToken(sessionAccessToken), request,
                    sourceOperationId, managedScheduleId, startedAt);
                Task<HumanApplyAttempt> ApplyAttemptAsync(CancellationToken token) =>
                    ApplyInsideTransactionAsync(
                        operationId, sessionId, target, accessToken, request, subject,
                        sourceOperationId, managedScheduleId, setupLive, actorUserId, startedAt,
                        instanceAuthority, tenantAuthority, finalizer, invocation, prepared, originalLocks, token);
                ConfigurationImportOperation committedOperation;
                if (setupLive)
                {
                    var applied = await mutationLock.ExecuteOrderedGroupsOnceAsync(
                        lockGroups,
                        async token =>
                        {
                            var applied = await setupAttemptOwner.ExecuteAsync(async attemptToken =>
                            {
                                requireCurrent();
                                var applied = await ApplyAttemptAsync(attemptToken);
                                requireCurrent();
                                return applied;
                            }, token);
                            setupCommitAcknowledged = true;
                            return applied;
                        },
                        cancellationToken);
                    committedOperation = applied.Operation!;
                }
                else
                {
                    var owner = ImportOwnerFactory().GetImportSettlementBoundary(prepared!.Authority.Lifetime);
                    var settlement = await mutationLock.ExecuteOrderedGroupsAsync(
                        lockGroups, token => owner.ExecuteAsync(invocation!, async ownedToken =>
                        {
                            requireCurrent();
                            await prepared.AcquireAsync(ownedToken);
                            var applied = await ApplyAttemptAsync(ownedToken);
                            requireCurrent();
                            if (applied.Rejection is { } refusal)
                                return OriginalOwnerDecision<HumanApplySettlement>.RollBack(new(null, refusal));
                            if (applied.Operation!.Id != invocation!.OperationId)
                                throw new InvalidOperationException("Import must retain its original operation identity.");
                            return OriginalOwnerDecision<HumanApplySettlement>.Commit(new(applied.Operation.Id, null));
                        }, token), cancellationToken);
                    // The public disposition chooses a validation branch; it never grants eligibility.
                    var settled = owner.Consume(invocation!, settlement, settlement.Disposition);
                    if (settled.Rejection is { } rejection)
                    {
                        if (settlement.Disposition != OriginalOwnerDisposition.WholeTransactionRolledBack
                            || !ReferenceEquals(rejection.Invocation, invocation) || settled.OperationId is not null)
                            throw new InvalidOperationException("Import rejection requires its original whole rollback.");
                        return new HumanApplyAttempt(null, rejection);
                    }
                    if (settlement.Disposition != OriginalOwnerDisposition.Committed || settled.OperationId is not { } committedId)
                        throw new InvalidOperationException("Import success requires its original commit.");
                    committedOperation = await operations.GetByIdAsync(committedId, target.AuthorityKey, cancellationToken)
                        ?? throw new InvalidOperationException("The acknowledged import operation is unavailable.");
                }
                return new HumanApplyAttempt(committedOperation, null);
            }, cancellationToken);
            if (outcome.Rejection is { } rejection)
            {
                await RecordHumanRejectionAsync(rejection, prepared!.Authority, cancellationToken);
                throw new ConfigurationImportSessionException(rejection.FailureCode);
            }
            ConfigurationImportOperation operation = outcome.Operation!;
            // Setup's outer generation owner releases its gates before attempting delivery.
            if (!setupLive) await DeliverEffectsAsync(operation, cancellationToken);
            return await MapAsync(operation, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Denial must unwind the real owner, not become a persisted apply-failure receipt.
            throw;
        }
        catch (HumanMutationAttemptRolledBackException)
        {
            // Only the original owner can acknowledge this rollback. A caller must restart
            // the complete import admission; never replay the old callback or preview decision.
            throw;
        }
        catch (HumanMutationOutcomeUnknownException)
        {
            throw;
        }
        catch (TransactionCommittedCleanupException)
        {
            throw;
        }
        catch (ConfigurationImportCoordinationCleanupException)
        {
            throw;
        }
        catch (Exception exception) when (setupCommitAcknowledged)
        {
            throw new ConfigurationImportCoordinationCleanupException(exception);
        }
        catch (Exception) when (!setupLive)
        {
            // A thrown physical commit can have an unknown outcome. Do not append a contradictory
            // failure receipt or claim success using a callback result from an unacknowledged owner.
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

    private async Task DeliverEffectsAsync(ConfigurationImportOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            if (operation.EffectOutboxId is { } messageId)
                await effectDelivery.DeliverAsync(messageId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Configuration import operation {OperationId} committed with pending effect type {ExceptionType}.",
                operation.Id, exception.GetType().Name);
        }
    }

    private async Task<HumanApplyAttempt> ApplyInsideTransactionAsync(
        Guid operationId,
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        ConfigurationImportPreviewRequest request,
        HumanCommitSubject? subject,
        Guid? sourceOperationId,
        Guid? managedScheduleId,
        bool setupLive,
        Guid actorUserId,
        DateTime startedAt,
        IInstanceAdministratorCommitAuthority? instanceAuthority,
        ITenantAdministratorCommitAuthority? tenantAuthority,
        IHumanActionCommitFinalizer? finalizer,
        HumanApplyInvocation? invocation,
        IPreparedHumanConfigurationImport? prepared,
        ConfigurationImportSectionApplier.OriginalLockGroups originalLocks,
        CancellationToken cancellationToken)
    {
        async Task RequireCurrentAuthorityAsync(CancellationToken token)
        {
            if (setupLive)
                return;
            bool allowed = target.Scope == ConfigurationImportScope.Instance
                ? await instanceAuthority!.IsInstanceAdministratorForCommitAsync(subject!.PrincipalId, token)
                : await tenantAuthority!.IsTenantAdministratorForCommitAsync(subject!.PrincipalId, target.TenantId!.Value, token);
            if (!allowed)
                throw new UnauthorizedAccessException("Current configuration import administration is required.");
        }
        await RequireCurrentAuthorityAsync(cancellationToken);
        if (!setupLive)
            finalizer!.Register(RequireCurrentAuthorityAsync, _ => { });
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
        if (invocation is not null && session.ArtifactDigest != invocation.ArtifactDigest)
            throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ArtifactIntegrityInvalid);

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

        HumanImportSources? retained = invocation is null ? null
            : await CaptureHumanSourcesAsync(invocation, session, cancellationToken);
        // Capture the actual parsed artifact/targets and FIRST publication data image
        // before preview/business validation. Admission below freezes this same observation.
        var originalSections = setupLive ? null : await sectionApplier.CaptureOriginalApplyAsync(
            operationId, target, sourceBytes, request, prepared!.Authority, now, session.ArtifactDigest,
            parser, ImportOwnerFactory() as IHumanConfigurationImportPublicationFactory
                ?? throw new InvalidOperationException("The selected import owner has no original publication source."),
            originalLocks, cancellationToken);
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
            if (invocation is not null)
                return new(null, new(invocation, retained!, ConfigurationImportFailureCodes.ApplyBlocked, UtcNow()));
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        }
        if (session.PreviewBinding is not { } persisted
            || !persisted.Matches(freshPreview.Binding))
        {
            if (invocation is not null)
                return new(null, new(invocation, retained!, ConfigurationImportFailureCodes.StalePreview, UtcNow()));
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

        // Only this branch has validated the original artifact, fresh preview, persisted binding,
        // rollback lineage and optional schedule inside the acquired original physical owner.
        // The section compiler freezes exact effects and the matching persistence callback.
        HumanConfigurationImportApply? admittedApply = setupLive ? null
            : sectionApplier.PrepareAdmittedApply(originalSections!, async token =>
                {
                    await ValidateHumanSourcesAsync(invocation!, retained!, consumed: true, token);
                    RequireSourceTimes(retained!, UtcNow(), requirePreview: true);
                });
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
        ImmutableArray<SettingChangedNotification> deferredNotifications = setupLive
            ? await sectionApplier.ApplySetupLiveAsync(
                target, sourceBytes, request, actorUserId, now, session.ArtifactDigest, parser, cancellationToken)
            : await prepared!.ExecuteApplyAsync(admittedApply!, cancellationToken);

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
            if (invocation is not null)
                return new(null, new(invocation, retained!, ConfigurationImportFailureCodes.ApplyFailed, UtcNow()));
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
        OutboxMessage effect = ConfigurationImportEffectOutbox.Create(
            outboxId,
            operationId,
            operation.CompletedAt!.Value,
            deferredNotifications);
        operation.BindEffectPayload(effect.Payload);
        await operations.AddAsync(operation, cancellationToken);
        await outbox.Create(effect);
        if (invocation is not null)
            finalizer!.Register(
                token => ValidateHumanSourcesAsync(invocation, retained!, consumed: true, token),
                instant => RequireSourceTimes(retained!, instant.UtcDateTime, requirePreview: true));
        return new(operation, null);
    }

    private IHumanConfigurationImportOwnerFactory ImportOwnerFactory() =>
        selectedScopeFactory
            as IHumanConfigurationImportOwnerFactory
        ?? throw new InvalidOperationException("Human import requires its actual selected owner factory.");

    private async Task RecordHumanRejectionAsync(
        HumanImportRejection rejection, HumanConfigurationImportAuthority authority, CancellationToken cancellationToken)
    {
        // The first physical owner and ordered source locks have already unwound, and its opaque
        // whole-rollback witness has been consumed. This scope never replays ApplyInsideTransaction.
        await using var fresh = await ImportOwnerFactory().CreateFreshImportOwnerAsync(authority, cancellationToken);
        await fresh.Services.GetRequiredService<ConfigurationImportApplyService>()
            .CommitHumanRejectionAsync(rejection, authority, cancellationToken);
    }

    private async Task CommitHumanRejectionAsync(
        HumanImportRejection rejection, HumanConfigurationImportAuthority authority, CancellationToken cancellationToken)
    {
        var invocation = rejection.Invocation;
        var prepared = await importAdmission
            .PrepareAsync(invocation.Subject, invocation.Target, authority, cancellationToken);
        var owner = ImportOwnerFactory().GetImportSettlementBoundary(authority.Lifetime);
        var finalizer = commitFinalizerFactory();
        var instanceAuthority = invocation.Target.Scope == ConfigurationImportScope.Instance
            ? instanceCommitAuthorityFactory() : null;
        var tenantAuthority = invocation.Target.Scope == ConfigurationImportScope.Tenant
            ? tenantCommitAuthorityFactory() : null;
        async Task RequireAuthorityAsync(CancellationToken token)
        {
            bool allowed = invocation.Target.Scope == ConfigurationImportScope.Instance
                ? await instanceAuthority!
                    .IsInstanceAdministratorForCommitAsync(invocation.Subject.PrincipalId, token)
                : await tenantAuthority!
                    .IsTenantAdministratorForCommitAsync(invocation.Subject.PrincipalId,
                        invocation.Target.TenantId!.Value, token);
            if (!allowed)
                throw new UnauthorizedAccessException("Current import authority is required for failed history.");
        }
        var settlement = await owner.ExecuteAsync(rejection, async token =>
        {
            await prepared.AcquireAsync(token);
            await RequireAuthorityAsync(token);
            await ValidateHumanSourcesAsync(invocation, rejection.Sources, consumed: false, token);
            RequireSourceTimes(rejection.Sources, UtcNow(), requirePreview: false);
            await operations.AddAsync(ConfigurationImportOperation.CreateFailed(
                invocation.OperationId, invocation.SessionId, invocation.Target.AuthorityKey,
                invocation.Target.TenantId, invocation.Subject.PrincipalId, invocation.SourceOperationId,
                invocation.ArtifactDigest, (int)invocation.Request.ApplyMode,
                invocation.Request.SelectedSectionKeys, rejection.FailureCode,
                "Configuration import failed and no selected section was committed.",
                invocation.StartedAt, rejection.ObservedAt), token);
            finalizer.Register(async finalToken =>
            {
                await RequireAuthorityAsync(finalToken);
                await ValidateHumanSourcesAsync(invocation, rejection.Sources, consumed: false, finalToken);
            }, now => RequireSourceTimes(rejection.Sources, now.UtcDateTime, requirePreview: false));
            return OriginalOwnerDecision<Guid>.Commit(invocation.OperationId);
        }, cancellationToken);
        if (owner.Consume(rejection, settlement, OriginalOwnerDisposition.Committed) != invocation.OperationId)
            throw new InvalidOperationException("Failed history must retain its original invocation.");
    }

    private async Task<HumanImportSources> CaptureHumanSourcesAsync(
        HumanApplyInvocation invocation, ConfigurationImportSession session, CancellationToken cancellationToken)
    {
        HumanRollbackSource? rollback = null;
        if (invocation.SourceOperationId is { } sourceId)
        {
            var source = await operations.GetByIdAsync(sourceId, invocation.Target.AuthorityKey, cancellationToken);
            if (source is null || source.TargetTenantId != invocation.Target.TenantId
                || !IsSuccessfulRollbackSource(source)
                || !source.FidelityVerified || source.EffectOutboxId is null
                || source.FailureCode is not null || source.FailureReason is not null
                || source.SnapshotArtifactHandleId is not { } handle
                || source.SnapshotDigest != invocation.ArtifactDigest
                || source.SnapshotExpiresAt is not { } expires
                || source.CompletedAt is not { } completed || completed > invocation.StartedAt
                || completed < source.StartedAt || expires <= completed)
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.RollbackUnavailable);
            var snapshot = await artifacts.ReadAsync(new(handle), cancellationToken);
            if (ConfigurationImportDigest.ComputeBytes(snapshot.Span) != source.SnapshotDigest)
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.RollbackUnavailable);
            rollback = new(handle, source.SnapshotDigest, expires, completed);
        }
        HumanScheduleSource? schedule = null;
        if (invocation.ManagedScheduleId is { } scheduleId)
        {
            var row = await scheduleRepository
                .GetForUpdateAsync(scheduleId, invocation.Target.AuthorityKey, cancellationToken);
            if (row is null || session.PreviewBinding is not { } binding
                || row.ArtifactDigest != invocation.ArtifactDigest
                || row.ManagedPlanDigest != ConfigurationManagedApplyScheduleService.PlanDigest(binding)
                || row.TargetRevisionDigest != binding.TargetRevisionDigest
                || row.Status != ConfigurationManagedApplyScheduleStatus.Approved
                || row.ReviewedBy is not { } reviewer
                || row.UploadedBy == invocation.Subject.PrincipalId || reviewer == invocation.Subject.PrincipalId)
                throw new ConfigurationImportSessionException(ConfigurationImportFailureCodes.ApplyBlocked);
            schedule = new(row.Id, row.ManagedPlanDigest, row.TargetRevisionDigest, row.UploadedBy,
                reviewer, row.ApplyNotBefore, row.ApplyBefore, row.Revision);
        }
        var sources = new HumanImportSources(session.Artifact, session.ExpiresAt, session.PreviewBinding,
            session.Revision, rollback, schedule);
        await ValidateHumanSourcesAsync(invocation, sources, consumed: false, cancellationToken);
        RequireSourceTimes(sources, UtcNow(), requirePreview: false);
        return sources;
    }

    private async Task ValidateHumanSourcesAsync(
        HumanApplyInvocation invocation, HumanImportSources retained, bool consumed, CancellationToken cancellationToken)
    {
        var currentSources = ImportOwnerFactory().GetImportSourceReader();
        var session = await currentSources.ReadSessionAsync(invocation.SessionId, invocation.Target, cancellationToken);
        if (session is null || session.AccessTokenDigest != invocation.TokenDigest
            || session.Artifact != retained.Artifact || session.ExpiresAt != retained.SessionExpiresAt
            || session.PreviewBinding != retained.Preview
            || session.Revision != retained.SessionRevision + (consumed ? 1 : 0)
            || session.CancelledAt is not null
            || (consumed ? session.State != ConfigurationImportSessionState.Consumed || session.ConsumedAt is null
                : session.State is not (ConfigurationImportSessionState.Uploaded or ConfigurationImportSessionState.PreviewReady)
                    || session.ConsumedAt is not null))
            throw new UnauthorizedAccessException("The original import session is no longer available.");
        var bytes = await artifacts.ReadAsync(retained.Artifact.Handle, cancellationToken);
        if (bytes.Length != retained.Artifact.ByteLength
            || ConfigurationImportDigest.ComputeBytes(bytes.Span) != invocation.ArtifactDigest)
            throw new UnauthorizedAccessException("The original import artifact is no longer available.");
        if (retained.Rollback is { } rollback)
        {
            var source = await operations.GetByIdAsync(
                invocation.SourceOperationId!.Value, invocation.Target.AuthorityKey, cancellationToken);
            if (source is null || source.TargetTenantId != invocation.Target.TenantId
                || !IsSuccessfulRollbackSource(source)
                || !source.FidelityVerified || source.EffectOutboxId is null
                || source.FailureCode is not null || source.FailureReason is not null
                || source.SnapshotArtifactHandleId != rollback.HandleId || source.SnapshotDigest != rollback.Digest
                || source.SnapshotExpiresAt != rollback.ExpiresAt || source.CompletedAt != rollback.CompletedAt)
                throw new UnauthorizedAccessException("The original rollback source is no longer available.");
            var snapshot = await artifacts.ReadAsync(new(rollback.HandleId), cancellationToken);
            if (ConfigurationImportDigest.ComputeBytes(snapshot.Span) != rollback.Digest)
                throw new UnauthorizedAccessException("The original rollback snapshot is no longer available.");
        }
        if (retained.Schedule is { } schedule)
        {
            var row = await currentSources.ReadScheduleAsync(schedule.Id, invocation.Target, cancellationToken);
            if (row is null || row.ArtifactDigest != invocation.ArtifactDigest
                || row.ManagedPlanDigest != schedule.PlanDigest || row.TargetRevisionDigest != schedule.TargetRevisionDigest
                || row.UploadedBy != schedule.UploadedBy || row.ReviewedBy != schedule.ReviewedBy
                || row.ApplyNotBefore != schedule.NotBefore || row.ApplyBefore != schedule.Before
                || row.Revision != schedule.Revision + (consumed ? 1 : 0)
                || (consumed ? row.Status != ConfigurationManagedApplyScheduleStatus.Applied
                        || row.AppliedBy != invocation.Subject.PrincipalId
                    : row.Status != ConfigurationManagedApplyScheduleStatus.Approved || row.AppliedBy is not null))
                throw new UnauthorizedAccessException("The original managed import schedule is no longer available.");
        }
    }

    private static void RequireSourceTimes(HumanImportSources sources, DateTime now, bool requirePreview)
    {
        if (now >= sources.SessionExpiresAt || now >= sources.Artifact.ExpiresAt
            || requirePreview && (sources.Preview is null || now >= sources.Preview.ExpiresAt)
            || sources.Rollback is { } rollback && now >= rollback.ExpiresAt
            || sources.Schedule is { } schedule && (now < schedule.NotBefore || now >= schedule.Before))
            throw new UnauthorizedAccessException("The original import source lifetime has ended.");
    }

    private static bool IsSuccessfulRollbackSource(ConfigurationImportOperation source) =>
        source.Kind == ConfigurationImportOperationKind.Apply
            && source.Status == ConfigurationImportOperationStatus.Applied && source.SourceOperationId is null
        || source.Kind == ConfigurationImportOperationKind.ForwardRollback
            && source.Status == ConfigurationImportOperationStatus.RolledBack && source.SourceOperationId is not null;

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

    // Only the three local business branches above create a rejection. Exception codes,
    // nested callbacks and physical rollback proposals cannot construct this private value.
    private sealed record HumanApplyInvocation(
        Guid OperationId, Guid SessionId, ConfigurationImportTarget Target,
        HumanCommitSubject Subject, string ArtifactDigest, string TokenDigest,
        ConfigurationImportPreviewRequest Request, Guid? SourceOperationId, Guid? ManagedScheduleId,
        DateTime StartedAt)
    {
        public override string ToString() => nameof(HumanApplyInvocation);
    }

    private sealed record HumanApplyAttempt(ConfigurationImportOperation? Operation, HumanImportRejection? Rejection);
    private sealed record HumanApplySettlement(Guid? OperationId, HumanImportRejection? Rejection);
    private sealed record HumanImportRejection(
        HumanApplyInvocation Invocation, HumanImportSources Sources, string FailureCode, DateTime ObservedAt);

    private sealed record HumanImportSources(
        ConfigurationImportArtifactReference Artifact, DateTime SessionExpiresAt,
        ConfigurationImportPreviewBinding? Preview, long SessionRevision,
        HumanRollbackSource? Rollback, HumanScheduleSource? Schedule);
    private sealed record HumanRollbackSource(Guid HandleId, string Digest, DateTime ExpiresAt, DateTime CompletedAt);
    private sealed record HumanScheduleSource(
        Guid Id, string PlanDigest, string TargetRevisionDigest, Guid UploadedBy, Guid ReviewedBy,
        DateTime NotBefore, DateTime Before, int Revision);

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
