using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IModerationPolicyPublicationRepository publications, ModerationNativeActivationQualification activation,
    TimeProvider clock, IPrivacyIdentityFenceAuthority privacy,
    IEnumerable<IModerationConsumerRuntimeSource>? consumerSources = null,
    IModerationMeasurementReceiptVerifier? measurementReceipts = null,
    ModerationCapabilityRegistry? deploymentInventory = null,
    ModerationConsumerConformanceReader? measuredConsumerConformance = null,
    IModerationMeasuredOperationsProofSource? measuredOperationalProofs = null,
    IDataProtectionProvider? responseProtection = null,
    IModerationCaseAttributionOwner? caseAttribution = null,
    ModerationNoticeNativeMaterializer? nativeNotices = null)
    : IModerationQualificationRepository
{
    private bool _attributionAcquired;

    public async Task<ModerationQualificationWriteResult> ExecuteAsync(
        ModerationQualificationMutation mutation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        _attributionAcquired = false;
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Source qualification owns its complete clean unit of work.");
        if (machine.IsMachineCaller)
            return new(ModerationQualificationWriteOutcome.Denied);
        if (await admin.ResolveUserIdAsync(cancellationToken) is null)
            return new(ModerationQualificationWriteOutcome.Unauthenticated);

        var request = mutation;
        var originalMeasurement = request is PrepareModerationConsumerMeasurementSource { OriginalEffect: not null }
            or AcceptModerationConsumerMeasurementSource { OriginalEffect: not null };
        var originalConstitution = OriginalConstitutionAnchor(request) is not null;
        var originalMeasuredOperations = OriginalMeasuredOperationsAnchor(request) is not null;
        var preFindingFinding = request is FinalizeModerationFindingSource;
        var participantInvestigation = request is RecordModerationParticipantIncidentResponsibilitySource;
        var classificationTaxonomy = IsClassificationTaxonomyMutation(request);
        var classificationMaterial = IsClassificationMaterialMutation(request);
        var classificationControl = IsClassificationControlMutation(request);
        var profileCollectionProcedure = IsProfileCollectionProcedureMutation(request);
        var measurementNonce = request is PrepareModerationConsumerMeasurementSource
            ? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)) : null;
        var requestPayload = JsonSerializer.SerializeToUtf8Bytes(request, request.GetType());
        string digest;
        try
        {
            using var requestHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            requestHash.AppendData(Encoding.UTF8.GetBytes(request.GetType().FullName + "\n"));
            requestHash.AppendData(requestPayload);
            digest = Convert.ToHexString(requestHash.GetHashAndReset());
        }
        finally { CryptographicOperations.ZeroMemory(requestPayload); }
        var authority = new ModerationQualificationAuthority(context, admin, machine);
        var priorTrustDomain = context.ModerationTrustDomainId;
        ModerationQualificationMutationReceipt? accepted = null;
        Func<CancellationToken, Task> beforePrimaryCommit = _ => Task.CompletedTask;
        ModerationConsumerEvidenceSession? procedureSession = null;

        Task<ModerationQualificationWriteResult> InContentCustodyAsync(
            Func<CancellationToken, Task<ModerationQualificationWriteResult>> apply) =>
            context.Database.CreateExecutionStrategy().ExecuteAsync(() =>
                privacy.ExecuteEnrollmentSerializedAsync(async token =>
                {
                    _attributionAcquired = false;
                    beforePrimaryCommit = _ => Task.CompletedTask;
                    if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                        : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var transaction = context.Database.CurrentTransaction
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    if (transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var result = await apply(token);
                    if (result.Outcome != ModerationQualificationWriteOutcome.Known)
                        throw new AbortQualification(result.Outcome);
                    await beforePrimaryCommit(token);
                    await RequireCurrentCaseAttributionAsync(token);
                    if (owned is not null) await owned.CommitAsync(token);
                    return result;
                }, async token =>
                {
                    await beforePrimaryCommit(token);
                    await RequireCurrentCaseAttributionAsync(token);
                }, cancellationToken));

        try
        {
            async Task<ModerationQualificationWriteResult> ApplyInPrimaryAsync(CancellationToken token)
            {
                accepted = null;
                procedureSession?.Dispose();
                procedureSession = null;
                _attributionAcquired = false;
                context.ChangeTracker.Clear();
                var transaction = context.Database.CurrentTransaction;
                var current = await authority.AcquireAsync(request, token);
                if (current is null)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RequireHumanAcquisitionAsync(request, current, token);
                if (originalMeasurement)
                    beforePrimaryCommit = finalToken => RequireOriginalMeasurementAtCommitAsync(request, current, finalToken);
                if (originalConstitution)
                    beforePrimaryCommit = finalToken => RequireOriginalConstitutionAtCommitAsync(request, current, finalToken);
                if (originalMeasuredOperations)
                    beforePrimaryCommit = finalToken => RequireOriginalMeasuredOperationsAtCommitAsync(request, current, finalToken);
                if (preFindingFinding)
                    beforePrimaryCommit = finalToken => RequirePreFindingFindingAtCommitAsync(request, current, finalToken);
                if (participantInvestigation)
                    beforePrimaryCommit = finalToken => VerifyParticipantInvestigationWriteAsync(request, current, finalToken);
                if (classificationTaxonomy)
                    beforePrimaryCommit = finalToken => VerifyClassificationTaxonomyAtCommitAsync(request, current, transaction!, finalToken);
                if (classificationMaterial)
                    beforePrimaryCommit = finalToken => VerifyClassificationMaterialAtCommitAsync(request, current, transaction!, finalToken);
                if (classificationControl)
                    beforePrimaryCommit = finalToken => VerifyClassificationControlAtCommitAsync(request, current, transaction!, finalToken);
                if (profileCollectionProcedure)
                    beforePrimaryCommit = finalToken => VerifyProfileCollectionProcedureAtCommitAsync(
                        request, procedureSession, current, transaction!, finalToken);
                using var deployment = await AcquireDeploymentWriteAsync(request, current, clock.GetUtcNow(), token);
                using var constitution = await AcquireConstitutionWriteAsync(request, current, clock.GetUtcNow(), token);
                using var recognitionPrivacy = await AcquireRecognitionPrivacyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var childAgePolicy = await AcquireChildAgePolicyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var taxonomy = await AcquireClassificationTaxonomyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var material = await AcquireClassificationMaterialWriteAsync(request, current, clock.GetUtcNow(), token);
                using var control = await AcquireClassificationControlWriteAsync(request, current, clock.GetUtcNow(), token);
                procedureSession = await AcquireProfileCollectionProcedureWriteAsync(request, current, clock.GetUtcNow(), token);
                using var eventRights = await AcquireEventRightsWriteAsync(request, current, clock.GetUtcNow(), token);
                using var measuredOperations = await AcquireMeasuredOperationsWriteAsync(request, current, clock.GetUtcNow(), token);
                RequireConsumerRuntime(request);
                if (request is PrepareModerationConsumerMeasurementSource preparation)
                    await ReadMeasurementPreparationAsync(preparation, current, clock.GetUtcNow(), token);
                if (request is AcceptModerationConsumerMeasurementSource measurement)
                    await ReadMeasurementAcceptanceAsync(measurement, current, clock.GetUtcNow(), token);
                var existing = await ReadReceiptAsync(request.OperationId, token);
                if (existing is not null)
                {
                    var settled = MatchReceipt(existing, request, digest, current);
                    if (settled.Outcome == ModerationQualificationWriteOutcome.Known
                        && request is AcceptModerationConsumerMeasurementSource repeatedMeasurement)
                        await RequireAcceptedMeasurementReceiptAsync(repeatedMeasurement, existing, current, token);
                    if (settled.Outcome == ModerationQualificationWriteOutcome.Known)
                    {
                        await VerifyDeploymentWriteAsync(request, deployment, current, token);
                        await VerifyConstitutionWriteAsync(request, constitution, current, token);
                        await VerifyRecognitionPrivacyWriteAsync(request, recognitionPrivacy, current, token);
                        await VerifyChildAgePolicyWriteAsync(request, childAgePolicy, current, token);
                        await VerifyClassificationTaxonomyWriteAsync(request, taxonomy, current, token);
                        await VerifyClassificationMaterialWriteAsync(request, material, current, token);
                        await VerifyClassificationControlWriteAsync(request, control, current, token);
                        await VerifyProfileCollectionProcedureWriteAsync(request, procedureSession, current, token);
                        await VerifyEventRightsWriteAsync(request, eventRights, current, token);
                        await VerifyMeasuredOperationsWriteAsync(request, measuredOperations, current, token);
                        await VerifyCaseNarrativeWriteAsync(request, current, token);
                        await VerifyOperationalBudgetWriteAsync(request, current, token);
                        await VerifyParticipantInvestigationWriteAsync(request, current, token);
                        await VerifyContentNoticeWriteAsync(request, current, token);
                        accepted = existing;
                        if (request is ApproveModerationNoticeDisclosureSource content)
                            beforePrimaryCommit = finalToken => RequireContentNoticeAtCommitAsync(
                                content, current, existing, digest, transaction!, finalToken);
                        if (request is ApproveModerationNoticeCorrectionDisclosureSource correction)
                            beforePrimaryCommit = finalToken => RequireCorrectionNoticeAtCommitAsync(
                                correction, current, existing, digest, transaction!, finalToken);
                    }
                    return settled;
                }
                if (current.Epoch.Revision != request.ExpectedQualificationRevision)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);

                var now = clock.GetUtcNow();
                var source = await ApplyAsync(request, current, now, measurementNonce, deployment, constitution, recognitionPrivacy, childAgePolicy, eventRights, measuredOperations, taxonomy, material, control, procedureSession, token);
                await VerifyProfileCollectionProcedureBeforeSaveAsync(request, procedureSession, current, token);
                await VerifyClassificationTaxonomyBeforeSaveAsync(request, taxonomy, current, token);
                await VerifyClassificationMaterialBeforeSaveAsync(request, material, current, token);
                await VerifyClassificationControlBeforeSaveAsync(request, control, current, token);
                current.Epoch.Advance(request.ExpectedQualificationRevision);
                accepted = ModerationQualificationMutationReceipt.Complete(
                    request.OperationId, digest, current.Binding.Id, current.Binding.TrustDomainId,
                    current.Tenant.Id, source.Kind, source.Id, current.UserId,
                    source.Revision, current.Epoch.Revision, now);
                context.ModerationQualificationMutationReceipts.Add(accepted);
                await context.SaveChangesAsync(token);
                await VerifyDeploymentWriteAsync(request, deployment, current, token);
                await VerifyConstitutionWriteAsync(request, constitution, current, token);
                await VerifyRecognitionPrivacyWriteAsync(request, recognitionPrivacy, current, token);
                await VerifyChildAgePolicyWriteAsync(request, childAgePolicy, current, token);
                await VerifyClassificationTaxonomyWriteAsync(request, taxonomy, current, token);
                await VerifyClassificationMaterialWriteAsync(request, material, current, token);
                await VerifyClassificationControlWriteAsync(request, control, current, token);
                await VerifyProfileCollectionProcedureWriteAsync(request, procedureSession, current, token);
                await VerifyEventRightsWriteAsync(request, eventRights, current, token);
                await VerifyMeasuredOperationsWriteAsync(request, measuredOperations, current, token);
                await VerifyCaseNarrativeWriteAsync(request, current, token);
                await VerifyOperationalBudgetWriteAsync(request, current, token);
                await VerifyParticipantInvestigationWriteAsync(request, current, token);
                await VerifyContentNoticeWriteAsync(request, current, token);
                var providerMatches = await authority.ProviderTenantMatchesAsync(accepted.TenantId, token);
                RequireConsumerRuntime(request);
                if (request is PrepareModerationConsumerMeasurementSource flushedPreparation)
                    await ReadMeasurementPreparationAsync(flushedPreparation, current, clock.GetUtcNow(), token);
                if (request is AcceptModerationConsumerMeasurementSource flushedMeasurement)
                    await ReadMeasurementAcceptanceAsync(flushedMeasurement, current, clock.GetUtcNow(), token);
                var completedAt = clock.GetUtcNow();
                if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                    || !providerMatches
                    || context.ModerationTrustDomainId != current.Binding.TrustDomainId
                    || completedAt < now
                    || source.MutationValidUntil is { } deadline && completedAt >= deadline
                     || source.NoticeRetention is { } notice && !ModerationNoticeRetention.CanNotifyAt(
                         completedAt, notice.CalendarMonths, notice.RetainUntil)
                    || source.Policy is { } policy && completedAt >= policy.ValidUntil)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                if (request is ApproveModerationNoticeDisclosureSource contentApproval)
                    beforePrimaryCommit = async finalToken =>
                    {
                        await RequireContentNoticeAtCommitAsync(
                            contentApproval, current, accepted, digest, transaction!, finalToken);
                        var finalAt = clock.GetUtcNow();
                        if (finalAt < now || source.MutationValidUntil is { } limit && finalAt >= limit
                            || source.NoticeRetention is { } retention
                                && !ModerationNoticeRetention.CanNotifyAt(finalAt, retention.CalendarMonths, retention.RetainUntil))
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    };
                if (request is ApproveModerationNoticeCorrectionDisclosureSource correctionApproval)
                    beforePrimaryCommit = finalToken => RequireCorrectionNoticeAtCommitAsync(
                        correctionApproval, current, accepted, digest, transaction!, finalToken);
                await RequireCurrentCaseAttributionAsync(token);
                return new ModerationQualificationWriteResult(ModerationQualificationWriteOutcome.Known, accepted);
            }

            async Task<bool> VerifyCommittedAsync(CancellationToken token)
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var current = await authority.AcquireAsync(request, token);
                if (current is null || accepted is null)
                    return false;
                await RequireHumanAcquisitionAsync(request, current, token);
                using var deployment = await AcquireDeploymentWriteAsync(request, current, clock.GetUtcNow(), token);
                using var constitution = await AcquireConstitutionWriteAsync(request, current, clock.GetUtcNow(), token);
                using var recognitionPrivacy = await AcquireRecognitionPrivacyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var childAgePolicy = await AcquireChildAgePolicyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var taxonomy = await AcquireClassificationTaxonomyWriteAsync(request, current, clock.GetUtcNow(), token);
                using var material = await AcquireClassificationMaterialWriteAsync(request, current, clock.GetUtcNow(), token);
                using var control = await AcquireClassificationControlWriteAsync(request, current, clock.GetUtcNow(), token);
                using var procedure = await AcquireProfileCollectionProcedureWriteAsync(request, current, clock.GetUtcNow(), token);
                using var eventRights = await AcquireEventRightsWriteAsync(request, current, clock.GetUtcNow(), token);
                using var measuredOperations = await AcquireMeasuredOperationsWriteAsync(request, current, clock.GetUtcNow(), token);
                RequireConsumerRuntime(request);
                if (request is PrepareModerationConsumerMeasurementSource preparation)
                    await ReadMeasurementPreparationAsync(preparation, current, clock.GetUtcNow(), token);
                if (request is AcceptModerationConsumerMeasurementSource measurement)
                    await ReadMeasurementAcceptanceAsync(measurement, current, clock.GetUtcNow(), token);
                var receipt = await ReadReceiptAsync(request.OperationId, token);
                if (receipt is not null && request is AcceptModerationConsumerMeasurementSource acceptedMeasurement)
                    await RequireAcceptedMeasurementReceiptAsync(acceptedMeasurement, receipt, current, token);
                if (receipt is not null)
                {
                    await VerifyDeploymentWriteAsync(request, deployment, current, token);
                    await VerifyConstitutionWriteAsync(request, constitution, current, token);
                    await VerifyRecognitionPrivacyWriteAsync(request, recognitionPrivacy, current, token);
                    await VerifyChildAgePolicyWriteAsync(request, childAgePolicy, current, token);
                    await VerifyClassificationTaxonomyWriteAsync(request, taxonomy, current, token);
                    await VerifyClassificationMaterialWriteAsync(request, material, current, token);
                    await VerifyClassificationControlWriteAsync(request, control, current, token);
                    if (classificationControl)
                        await VerifyClassificationControlAtCommitAsync(request, current, transaction, token);
                    await VerifyProfileCollectionProcedureWriteAsync(request, procedure, current, token);
                    await VerifyEventRightsWriteAsync(request, eventRights, current, token);
                    await VerifyMeasuredOperationsWriteAsync(request, measuredOperations, current, token);
                    await VerifyCaseNarrativeWriteAsync(request, current, token);
                    await VerifyOperationalBudgetWriteAsync(request, current, token);
                    await VerifyParticipantInvestigationWriteAsync(request, current, token);
                    await VerifyContentNoticeWriteAsync(request, current, token);
                }
                return receipt is not null && receipt.ResultRevision == accepted.ResultRevision
                    && receipt.QualificationRevision == accepted.QualificationRevision
                    && MatchReceipt(receipt, request, digest, current).Outcome == ModerationQualificationWriteOutcome.Known;
            }

            if (request is ApproveModerationNoticeDisclosureSource or ApproveModerationNoticeCorrectionDisclosureSource || originalMeasurement || originalConstitution || originalMeasuredOperations || preFindingFinding || participantInvestigation || classificationTaxonomy || classificationMaterial || classificationControl || profileCollectionProcedure)
                return await InContentCustodyAsync(ApplyInPrimaryAsync);
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: ApplyInPrimaryAsync, verifySucceeded: VerifyCommittedAsync,
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortQualification aborted)
        {
            return new(aborted.Outcome);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ModerationQualificationWriteOutcome.Conflict);
        }
        catch (DbUpdateException exception) when (RegistrationUniqueConflictClassifier.IsProviderUniqueConflict(exception))
        {
            context.ChangeTracker.Clear();
            try
            {
                async Task<ModerationQualificationWriteResult> RecoverReceiptInPrimaryAsync(CancellationToken token)
                {
                    var transaction = context.Database.CurrentTransaction
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var current = await authority.AcquireAsync(request, token);
                    if (current is null)
                        return new ModerationQualificationWriteResult(ModerationQualificationWriteOutcome.Denied);
                    await RequireHumanAcquisitionAsync(request, current, token);
                    if (originalMeasurement)
                        beforePrimaryCommit = finalToken => RequireOriginalMeasurementAtCommitAsync(request, current, finalToken);
                    if (originalConstitution)
                        beforePrimaryCommit = finalToken => RequireOriginalConstitutionAtCommitAsync(request, current, finalToken);
                    if (originalMeasuredOperations)
                        beforePrimaryCommit = finalToken => RequireOriginalMeasuredOperationsAtCommitAsync(request, current, finalToken);
                    if (preFindingFinding)
                        beforePrimaryCommit = finalToken => RequirePreFindingFindingAtCommitAsync(request, current, finalToken);
                    if (participantInvestigation)
                        beforePrimaryCommit = finalToken => VerifyParticipantInvestigationWriteAsync(request, current, finalToken);
                    if (classificationTaxonomy)
                        beforePrimaryCommit = finalToken => VerifyClassificationTaxonomyAtCommitAsync(request, current, transaction, finalToken);
                    if (classificationMaterial)
                        beforePrimaryCommit = finalToken => VerifyClassificationMaterialAtCommitAsync(request, current, transaction, finalToken);
                    if (classificationControl)
                        beforePrimaryCommit = finalToken => VerifyClassificationControlAtCommitAsync(request, current, transaction, finalToken);
                    if (profileCollectionProcedure)
                        beforePrimaryCommit = finalToken => VerifyProfileCollectionProcedureAtCommitAsync(
                            request, procedureSession, current, transaction, finalToken);
                    using var deployment = await AcquireDeploymentWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var constitution = await AcquireConstitutionWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var recognitionPrivacy = await AcquireRecognitionPrivacyWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var childAgePolicy = await AcquireChildAgePolicyWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var taxonomy = await AcquireClassificationTaxonomyWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var material = await AcquireClassificationMaterialWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var control = await AcquireClassificationControlWriteAsync(request, current, clock.GetUtcNow(), token);
                    procedureSession?.Dispose();
                    procedureSession = await AcquireProfileCollectionProcedureWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var eventRights = await AcquireEventRightsWriteAsync(request, current, clock.GetUtcNow(), token);
                    using var measuredOperations = await AcquireMeasuredOperationsWriteAsync(request, current, clock.GetUtcNow(), token);
                    RequireConsumerRuntime(request);
                    var receipt = await ReadReceiptAsync(request.OperationId, token);
                    var result = receipt is null ? new(ModerationQualificationWriteOutcome.Conflict)
                        : MatchReceipt(receipt, request, digest, current);
                    if (request is PrepareModerationConsumerMeasurementSource preparation)
                        await ReadMeasurementPreparationAsync(preparation, current, clock.GetUtcNow(), token);
                    if (request is AcceptModerationConsumerMeasurementSource measurement)
                        await ReadMeasurementAcceptanceAsync(measurement, current, clock.GetUtcNow(), token);
                    if (result.Outcome == ModerationQualificationWriteOutcome.Known && receipt is not null
                        && request is AcceptModerationConsumerMeasurementSource repeatedMeasurement)
                        await RequireAcceptedMeasurementReceiptAsync(repeatedMeasurement, receipt, current, token);
                    if (result.Outcome == ModerationQualificationWriteOutcome.Known)
                    {
                        await VerifyDeploymentWriteAsync(request, deployment, current, token);
                        await VerifyConstitutionWriteAsync(request, constitution, current, token);
                        await VerifyRecognitionPrivacyWriteAsync(request, recognitionPrivacy, current, token);
                        await VerifyChildAgePolicyWriteAsync(request, childAgePolicy, current, token);
                        await VerifyClassificationTaxonomyWriteAsync(request, taxonomy, current, token);
                        await VerifyClassificationMaterialWriteAsync(request, material, current, token);
                        await VerifyClassificationControlWriteAsync(request, control, current, token);
                        await VerifyProfileCollectionProcedureWriteAsync(request, procedureSession, current, token);
                        await VerifyEventRightsWriteAsync(request, eventRights, current, token);
                        await VerifyMeasuredOperationsWriteAsync(request, measuredOperations, current, token);
                        await VerifyCaseNarrativeWriteAsync(request, current, token);
                        await VerifyOperationalBudgetWriteAsync(request, current, token);
                        await VerifyParticipantInvestigationWriteAsync(request, current, token);
                        await VerifyContentNoticeWriteAsync(request, current, token);
                        if (request is ApproveModerationNoticeDisclosureSource content && receipt is not null)
                            beforePrimaryCommit = finalToken => RequireContentNoticeAtCommitAsync(
                                content, current, receipt, digest, transaction, finalToken);
                        if (request is ApproveModerationNoticeCorrectionDisclosureSource correction && receipt is not null)
                            beforePrimaryCommit = finalToken => RequireCorrectionNoticeAtCommitAsync(
                                correction, current, receipt, digest, transaction, finalToken);
                    }
                    return result;
                }
                if (request is ApproveModerationNoticeDisclosureSource or ApproveModerationNoticeCorrectionDisclosureSource || originalMeasurement || originalConstitution || originalMeasuredOperations || preFindingFinding || participantInvestigation || classificationTaxonomy || classificationMaterial || classificationControl || profileCollectionProcedure)
                    return await InContentCustodyAsync(RecoverReceiptInPrimaryAsync);
                return await context.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    var result = await RecoverReceiptInPrimaryAsync(token);
                    await transaction.CommitAsync(token);
                    return result;
                }, cancellationToken);
            }
            catch (AbortQualification aborted)
            {
                return new(aborted.Outcome);
            }
        }
        finally
        {
            procedureSession?.Dispose();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = priorTrustDomain;
        }
    }

    private async Task RequireHumanAcquisitionAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (mutation is EnrollModerationMeasurementIssuerSource or RevokeModerationMeasurementIssuerSource
            && !authority.IsInstanceAdmin)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        Guid? caseId = mutation switch
        {
            AdoptModerationProfileSource source => source.CaseId,
            RevokeModerationProfileSource source => source.CaseId,
            AdoptModerationEventTargetSource source => source.CaseId,
            RevokeModerationEventTargetSource source => source.CaseId,
            RecordModerationContributionSelectionSource source => source.CaseId,
            ApproveModerationNoticeDisclosureSource source => source.Intent.CaseId,
            ApproveModerationNoticeCorrectionDisclosureSource source => source.Intent.CaseId,
            RevokeModerationNoticeDisclosureSource source => source.CaseId,
            PublishModerationQualificationPolicySource source => source.CaseId,
            AdoptModerationQualificationPolicySource source => source.CaseId,
            RevokeModerationQualificationPolicyAdoption source => source.CaseId,
            RecordModerationCaseResponse source => source.CaseId,
            AssignModerationReviewerSource source => source.CaseId,
            QualifyModerationCaseUrgency source => source.CaseId,
            FinalizeModerationFindingSource source => source.CaseId,
            _ => null
        };
        var assignmentId = mutation switch
        {
            QualifyModerationReviewerController source => source.AssignmentId,
            AssessModerationReviewerConflict source => source.AssignmentId,
            WithdrawModerationReviewerSource source => source.AssignmentId,
            _ => (Guid?)null
        };
        if (assignmentId is { } reviewerId)
            caseId = await context.ModerationReviewerAssignments.AsNoTracking()
                .Where(row => row.Id == reviewerId).Select(row => (Guid?)row.CaseId).SingleOrDefaultAsync(token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var humans = new HashSet<Guid>();
        if (caseId is { } actualCaseId)
        {
            var moderationCase = await context.ModerationCases.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == actualCaseId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            if (moderationCase.Subject is null
                && mutation is not (AssignModerationReviewerSource or WithdrawModerationReviewerSource))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
            if (moderationCase.Subject is { Kind: ModerationSubjectKind.Human } caseSubject)
            {
                if (mutation is RecordModerationCaseResponse && moderationCase.SubjectId != authority.UserId)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                humans.Add(caseSubject.Id);
            }
        }
        if (mutation is ReverseModerationFindingSource reversal)
        {
            var finding = await context.ModerationFindings.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == reversal.FindingId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            humans.Add(finding.AdjudicatorId);
            if (finding.SubjectKind == ModerationSubjectKind.Human) humans.Add(finding.SubjectId);
        }
        var proceduralSourceId = mutation switch
        {
            PublishModerationQualificationPolicySource source => (Guid?)source.SourceId,
            AdoptModerationQualificationPolicySource source => source.SourceId,
            RevokeModerationQualificationPolicyAdoption source => source.SourceId,
            _ => null
        };
        if (proceduralSourceId is { } policyId)
        {
            var policy = await context.ModerationQualificationPolicySources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == policyId, token);
            if (policy is not null && !authority.FencedHumans.Contains(policy.PublishedByHumanId))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        if (mutation is RevokeModerationQualificationPolicyAdoption revokePolicy)
        {
            var adoption = await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokePolicy.AdoptionId, token);
            if (adoption is not null && (!authority.FencedHumans.Contains(adoption.AdoptedByHumanId)
                || adoption.RevokedByHumanId is { } revoker && !authority.FencedHumans.Contains(revoker)))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        if (mutation is RevokeModerationNoticeDisclosureSource revokeDisclosure)
        {
            var approval = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == revokeDisclosure.ApprovalId, token);
            if (approval is not null && (!authority.FencedHumans.Contains(approval.ApprovedByHumanId)
                || approval.RevokedByHumanId is { } revoker && !authority.FencedHumans.Contains(revoker)))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        // Do not acquire a human fence after the helper's subordinate host/tenant/epoch fences.
        // A helper that omitted a real participant cannot authorize mutation or receipt replay.
        if (humans.Any(id => !authority.FencedHumans.Contains(id)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var ids = humans.ToArray();
        if (!ModerationQualificationAuthority.IsRetainedCaseCorrection(mutation) && ids.Length != 0
            && await context.Users.AsNoTracking().CountAsync(row => ids.Contains(row.Id) && !row.IsDeleted, token) != ids.Length)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task<QualifiedSource> ApplyAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority,
        DateTimeOffset now, string? measurementNonce, ModerationConsumerEvidenceSession? deployment,
        ModerationConsumerEvidenceSession? constitution, ModerationConsumerEvidenceSession? recognitionPrivacy,
        ModerationConsumerEvidenceSession? childAgePolicy, ModerationConsumerEvidenceSession? eventRights,
        ModerationConsumerEvidenceSession? measuredOperations, ModerationConsumerEvidenceSession? taxonomy,
        ModerationConsumerEvidenceSession? material, ModerationConsumerEvidenceSession? control,
        ModerationConsumerEvidenceSession? procedure, CancellationToken token)
    {
        switch (mutation)
        {
            case RecordModerationOperationalBudgetSource source:
                return await RecordOperationalBudgetAsync(source, authority, now, token);
            case AdoptModerationOperationalBudgetSource source:
                return await AdoptOperationalBudgetAsync(source, authority, now, token);
            case WithdrawModerationOperationalBudgetAdoption source:
                return await WithdrawOperationalBudgetAsync(source, authority, now, token);
            case RecordModerationClassificationControlSelection source:
                return await RecordClassificationControlSelectionAsync(source, authority, control!, now, token);
            case AdoptModerationClassificationEventTarget source:
                return await AdoptClassificationEventTargetAsync(source, authority, control!, now, token);
            case RevokeModerationClassificationEventTarget source:
                return await RevokeClassificationEventTargetAsync(source, authority, now, token);
            case RecordModerationProfileCollectionProcedureSource source:
                return await RecordProfileCollectionProcedureAsync(source, authority, procedure!, now, token);
            case AdoptModerationProfileCollectionProcedureSource source:
                return await AdoptProfileCollectionProcedureAsync(source, authority, procedure!, now, token);
            case WithdrawModerationProfileCollectionProcedureAdoption source:
                return await WithdrawProfileCollectionProcedureAsync(source, authority, now, token);
            case RecordModerationClassificationMaterialSource source:
                return await RecordClassificationMaterialAsync(source, authority, material!, now, token);
            case AcceptModerationClassificationMaterialSource source:
                return await AcceptClassificationMaterialAsync(source, authority, material!, now, token);
            case EndModerationClassificationMaterialReview source:
                return await EndClassificationMaterialAsync(source, authority, now, token);
            case RecordModerationClassificationTaxonomySource source:
                return await RecordClassificationTaxonomyAsync(source, authority, taxonomy!, now, token);
            case AdoptModerationClassificationTaxonomySource source:
                return await AdoptClassificationTaxonomyAsync(source, authority, taxonomy!, now, token);
            case WithdrawModerationClassificationTaxonomyAdoption source:
                return await WithdrawClassificationTaxonomyAsync(source, authority, now, token);
            case RecordModerationParticipantIncidentResponsibilitySource source:
                return await RecordParticipantInvestigationAsync(source, authority, now, token);
            case RecordModerationCaseNarrativePolicySource source:
                return await RecordCaseNarrativeAsync(source, authority, now, token);
            case AdoptModerationCaseNarrativePolicySource source:
                return await AdoptCaseNarrativeAsync(source, authority, now, token);
            case WithdrawModerationCaseNarrativeAdoption source:
                return await WithdrawCaseNarrativeAsync(source, authority, now, token);
            case RecordModerationMeasuredOperationsSource source:
                return await RecordMeasuredOperationsAsync(source, authority, measuredOperations!, now, token);
            case AcceptModerationMeasuredOperationsSource source:
                return await AcceptMeasuredOperationsAsync(source, authority, measuredOperations!, now, token);
            case WithdrawModerationMeasuredOperationsAcceptance source:
                return await WithdrawMeasuredOperationsAsync(source, authority, now, token);
            case RecordModerationEventRightsSource source:
                return await RecordEventRightsAsync(source, authority, eventRights!, now, token);
            case AdoptModerationEventRightsSource source:
                return await AdoptEventRightsAsync(source, authority, eventRights!, now, token);
            case WithdrawModerationEventRightsAdoption source:
                return await WithdrawEventRightsAsync(source, authority, now, token);
            case RecordModerationChildAgePolicySource source:
                return await RecordChildAgePolicyAsync(source, authority, childAgePolicy!, now, token);
            case AdoptModerationChildAgePolicySource source:
                return await AdoptChildAgePolicyAsync(source, authority, childAgePolicy!, now, token);
            case WithdrawModerationChildAgePolicyAdoption source:
                return await WithdrawChildAgePolicyAsync(source, authority, now, token);
            case RecordModerationRecognitionPrivacySource source:
                return await RecordRecognitionPrivacyAsync(source, authority, recognitionPrivacy!, now, token);
            case AdoptModerationRecognitionPrivacySource source:
                return await AdoptRecognitionPrivacyAsync(source, authority, recognitionPrivacy!, now, token);
            case WithdrawModerationRecognitionPrivacyAdoption source:
                return await WithdrawRecognitionPrivacyAsync(source, authority, now, token);
            case RecordModerationConstitutionSource source:
                return await RecordConstitutionAsync(source, authority, constitution!, now, token);
            case AdoptModerationConstitutionSource source:
                return await AdoptConstitutionAsync(source, authority, constitution!, now, token);
            case WithdrawModerationConstitutionAdoption source:
                return await WithdrawConstitutionAsync(source, authority, now, token);
            case RecordModerationConstitutionAvailability source:
                return await RecordConstitutionAvailabilityAsync(source, authority, now, token);
            case WithdrawModerationConstitutionAvailability source:
                return await WithdrawConstitutionAvailabilityAsync(source, authority, now, token);
            case RecordModerationDeploymentSource source:
                return await RecordDeploymentAsync(source, authority, deployment!, now, token);
            case AdoptModerationDeploymentSource source:
                return await AdoptDeploymentAsync(source, authority, deployment!, now, token);
            case WithdrawModerationDeploymentAdoption source:
                return await WithdrawDeploymentAsync(source, authority, now, token);
            case AcceptModerationConsumerMeasurementSource source:
                return await AcceptConsumerMeasurementAsync(source, authority, now, token);
            case EnrollModerationMeasurementIssuerSource source:
                return await EnrollMeasurementIssuerAsync(source, authority, now, token);
            case RevokeModerationMeasurementIssuerSource source:
                return await RevokeMeasurementIssuerAsync(source, authority, now, token);
            case PrepareModerationConsumerMeasurementSource source:
                return await PrepareConsumerMeasurementAsync(source, authority, now, measurementNonce!, token);
            case InstallModerationConsumerSource source:
                return await InstallConsumerAsync(source, authority, now, token);
            case WithdrawModerationConsumerSource source:
                return await WithdrawConsumerAsync(source, authority, now, token);
            case AdoptModerationProfileSource source:
                return await AdoptProfileAsync(source, authority, now, token);
            case RevokeModerationProfileSource source:
                return await RevokeProfileAsync(source, authority, now, token);
            case AdoptModerationEventTargetSource source:
                return await AdoptEventTargetAsync(source, authority, now, token);
            case RevokeModerationEventTargetSource source:
                return await RevokeEventTargetAsync(source, authority, now, token);
            case AdoptModerationResourceTargetSource source:
                return await AdoptResourceTargetAsync(source, authority, now, token);
            case RevokeModerationResourceTargetSource source:
                return await RevokeResourceTargetAsync(source, authority, now, token);
            case RecordModerationEventGenerationSource source:
                {
                    if (source.Target != authority.Tenant || source.ValidUntil <= now
                        || authority.EventGenerationPlan is null || authority.EventGenerationPlanSourceId != source.SourceId
                        || source.ExpectedEventRevision is < 0 or >= long.MaxValue - 1)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var generation = await ModerationEventGenerationReader.AcquireAsync(
                        context, source.SourceId, source.EventId, authority.Binding.Id, authority.Binding.TrustDomainId,
                        authority.Tenant.Id, checked(source.ExpectedEventRevision + 1), authority.UserId,
                        checked(authority.Epoch.Revision + 1), now, source.ValidUntil, token,
                        ownershipPlan: authority.EventGenerationPlan)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.event-generation:{authority.Binding.Id:N}:{source.EventId:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationEventGeneration>(context, source.SourceId, token);
                    var latest = await context.ModerationEventGenerations.AsNoTracking()
                        .Where(row => row.HostBindingId == authority.Binding.Id && row.EventId == source.EventId)
                        .OrderByDescending(row => row.Revision).FirstOrDefaultAsync(token);
                    if ((latest?.Revision ?? 0) != source.ExpectedEventRevision
                        || await context.ModerationEventGenerations.AsNoTracking().AnyAsync(row => row.Id == source.SourceId, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    context.ModerationEventGenerations.Add(generation);
                    return new(ModerationQualificationSourceKind.EventGeneration, generation.Id, generation.Revision,
                        MutationValidUntil: generation.ValidUntil);
                }
            case RecordModerationResourceGenerationSource source:
                {
                    if (source.Target != authority.Tenant || source.ValidUntil <= now
                        || authority.ResourceGenerationPlan is null || authority.ResourceGenerationPlanSourceId != source.SourceId
                        || source.ExpectedResourceRevision is < 0 or >= long.MaxValue - 1)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var generation = await ModerationResourceGenerationReader.AcquireAsync(
                        context, source.SourceId, source.ResourceId, authority.Binding.Id, authority.Binding.TrustDomainId,
                        authority.Tenant.Id, checked(source.ExpectedResourceRevision + 1), authority.UserId,
                        checked(authority.Epoch.Revision + 1), now, source.ValidUntil, token,
                        ancestryPlan: authority.ResourceGenerationPlan)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    // Native publisher/event/resource/storage fences precede the generation slot.
                    // The slot allocates a numeric revision, never a conversion of those GUID stamps.
                    await using var slot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.resource-generation:{authority.Binding.Id:N}:{source.ResourceId:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationResourceGeneration>(context, source.SourceId, token);
                    var latest = await context.ModerationResourceGenerations.AsNoTracking()
                        .Where(row => row.HostBindingId == authority.Binding.Id && row.ResourceId == source.ResourceId)
                        .OrderByDescending(row => row.Revision).FirstOrDefaultAsync(token);
                    if ((latest?.Revision ?? 0) != source.ExpectedResourceRevision
                        || await context.ModerationResourceGenerations.AsNoTracking().AnyAsync(row => row.Id == source.SourceId, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    context.ModerationResourceGenerations.Add(generation);
                    return new(ModerationQualificationSourceKind.ResourceGeneration, generation.Id, generation.Revision,
                        MutationValidUntil: generation.ValidUntil);
                }
            case RecordModerationContributionSelectionSource source:
                {
                    var moderationCase = await ReadCaseAsync(
                        source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, source.DecisionId, token);
                    var decision = await context.ModerationDecisions.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == source.DecisionId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    decision.ValidatePersistedState();
                    var contribution = source.Contribution;
                    if (decision.CaseId != moderationCase.Id || decision.Subject != moderationCase.Subject
                        || decision.Scope != source.Target || contribution.Id != decision.Id
                        || contribution.Rule != decision.Rule || contribution.Scope != decision.Scope
                        || contribution.SubjectKind != decision.Subject.Kind || contribution.SubjectId != decision.Subject.Id
                        || contribution.StartsAt != decision.StartsAt || source.ValidUntil <= now)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    // Authoring may name a contribution-only transition or a decision transition.
                    // The lifecycle capture enforces which exact revision the operation can use.
                    if (decision.Revision >= long.MaxValue - 1
                        || source.ExpectedDecisionRevision != decision.Revision
                            && source.ExpectedDecisionRevision != decision.Revision + 1)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    AuthorityScope? jurisdiction = source.Target;
                    while (jurisdiction is not null && jurisdiction.Id != decision.Rule.OwnerId) jurisdiction = jurisdiction.Parent;
                    if (jurisdiction is null || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var publication = await publications.AcquireSelectedAsync(
                        context.Database.CurrentTransaction!.TransactionId, authority.Binding.Id,
                        jurisdiction, source.PolicyId, false, token);
                    if (publication is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    if (publication.Id != source.PublicationId || publication.Revision != source.ExpectedPolicyRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
                    if (draft is null || !draft.Profiles.Any(row => row.Definition.Id == source.ProfileId
                            && row.Definition.Rule == decision.Rule))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var composed = ModerationPolicyProposedStateCompiler.ComposeRetained(draft, [contribution], now);
                    if (composed.Outcome != ModerationPolicyCompositionOutcome.Preview || composed.Consequences.IsEmpty
                        || composed.Effects.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt)
                        || composed.Consequences.Any(row => row.Interval.EndsAt != decision.EndsAt || row.Interval.ReviewAt != decision.ReviewAt))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    await using var selectionSlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.contribution:{authority.Binding.Id:N}:{source.EffectId:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationContributionSelection>(context, source.SelectionId, token);
                    if (await context.ModerationContributionSelections.AsNoTracking().AnyAsync(
                            row => row.Id == source.SelectionId || row.EffectId == source.EffectId, token)
                        || await context.ModerationEffectIntents.AsNoTracking().AnyAsync(row => row.Id == source.EffectId, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var selection = new ModerationContributionSelection
                    {
                        Id = source.SelectionId,
                        EffectId = source.EffectId,
                        DecisionId = decision.Id,
                        DecisionRevision = source.ExpectedDecisionRevision,
                        CaseId = moderationCase.Id,
                        CaseRevision = moderationCase.Revision,
                        PolicyId = publication.PolicyId,
                        PolicyRevision = publication.Revision,
                        PublicationId = publication.Id,
                        ProfileId = source.ProfileId,
                        HostBindingId = authority.Binding.Id,
                        TrustDomainId = authority.Binding.TrustDomainId,
                        TenantId = authority.Tenant.Id,
                        AuthoredByHumanId = authority.UserId,
                        WriterGeneration = checked(authority.Epoch.Revision + 1),
                        Revision = 1,
                        SelectionDigest = ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
                        StartsAt = contribution.StartsAt,
                        EventWindowEnd = contribution.EventWindowEnd,
                        RecordedAt = now,
                        ValidUntil = source.ValidUntil,
                        Bindings = ModerationContributionSelectionProjection.Bindings(source.SelectionId, contribution)
                    };
                    selection.ValidatePersistedState();
                    context.ModerationContributionSelections.Add(selection);
                    return new(ModerationQualificationSourceKind.ContributionSelection, selection.Id, selection.Revision,
                        MutationValidUntil: selection.ValidUntil);
                }
            case ApproveModerationNoticeCorrectionDisclosureSource correction:
                return await ApproveCorrectionNoticeAsync(correction, authority, now, token);
            case ApproveModerationNoticeDisclosureSource source:
                {
                    source.Intent.Validate();
                    var intent = source.Intent;
                    var moderationCase = await ReadCaseAsync(
                        intent.CaseId, source.Target, intent.ExpectedCaseRevision, authority, token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDecision>(context, intent.DecisionId, token);
                    var decision = await context.ModerationDecisions.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == intent.DecisionId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    decision.ValidatePersistedState();
                    if (decision.CaseId != moderationCase.Id || decision.Subject != moderationCase.Subject
                        || decision.Scope != source.Target)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (decision.Revision >= long.MaxValue - 1
                        || intent.ExpectedDecisionRevision != decision.Revision
                            && intent.ExpectedDecisionRevision != decision.Revision + 1)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    AuthorityScope? jurisdiction = source.Target;
                    while (jurisdiction is not null && jurisdiction.Id != decision.Rule.OwnerId) jurisdiction = jurisdiction.Parent;
                    if (jurisdiction is null || jurisdiction.Kind is not (ModerationScopeKind.Instance or ModerationScopeKind.Tenant))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var publication = await publications.AcquireSelectedAsync(
                        context.Database.CurrentTransaction!.TransactionId, authority.Binding.Id,
                        jurisdiction, intent.PolicyId, false, token);
                    if (publication is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    if (publication.Id != intent.PublicationId || publication.Revision != intent.ExpectedPolicyRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication);
                    var profile = draft?.Profiles.SingleOrDefault(row => row.Definition.Id == intent.ProfileId
                        && row.Definition.Rule == decision.Rule && row.Definition.ScopeKinds.Contains(source.Target.Kind)
                        && row.Reference.AcceptsScope(publication.Jurisdiction, source.Target));
                    if (profile is null || draft is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var reason = draft.Proposed.Reasons.Single(row => row.Reference == profile.Definition.Reason);
                    var classification = draft.Proposed.Classes.Single(row => row.Reference == reason.Class);
                    if (intent.RuleMeaning.Value != classification.Meaning || intent.ReasonMeaning.Value != reason.Meaning
                        || intent.ValidUntil <= now || !ModerationNoticeRetention.CanNotifyAt(
                            now, profile.Definition.ContestationCalendarMonths, intent.RetainUntil))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (decision.SubjectKind == ModerationSubjectKind.Content && authority.DisclosureAuthority is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var custodyUntil = await ModerationNoticeContentCustody.AcquireAsync(context, privacy,
                        decision.Subject, authority.Tenant, intent.ContentRepresentatives, authority.FencedHumans,
                        now, intent.ValidUntil, token, authority.DisclosureAuthority);
                    if (custodyUntil is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    await using var disclosureSlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.disclosure:{authority.Binding.Id:N}:{intent.EffectId:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(
                        context, intent.ApprovalId, token);
                    if (await context.ModerationNoticeDisclosureApprovals.AsNoTracking().AnyAsync(
                        row => row.Id == intent.ApprovalId || row.EffectId == intent.EffectId, token)
                        || await context.ModerationEffectIntents.AsNoTracking().AnyAsync(row => row.Id == intent.EffectId, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var approval = ModerationNoticeDisclosureApproval.Approve(
                        intent, authority.Binding.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
                        authority.UserId, checked(authority.Epoch.Revision + 1), now);
                    context.ModerationNoticeDisclosureApprovals.Add(approval);
                    return new(ModerationQualificationSourceKind.NoticeDisclosure, approval.Id, approval.Revision,
                        MutationValidUntil: custodyUntil,
                        NoticeRetention: (approval.RetainUntil, profile.Definition.ContestationCalendarMonths));
                }
            case RevokeModerationNoticeDisclosureSource source:
                {
                    await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token,
                        requireOpen: false, requireCurrentAttribution: false);
                    var discovered = await context.ModerationNoticeDisclosureApprovals.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == source.ApprovalId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    await using var disclosureSlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.disclosure:{authority.Binding.Id:N}:{discovered.EffectId:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationNoticeDisclosureApproval>(context, source.ApprovalId, token);
                    var approval = await context.ModerationNoticeDisclosureApprovals.SingleAsync(
                        row => row.Id == source.ApprovalId, token);
                    approval.ValidatePersistedState();
                    if (approval.HostBindingId != authority.Binding.Id || approval.TrustDomainId != authority.Binding.TrustDomainId
                        || approval.TenantId != authority.Tenant.Id || approval.CaseId != source.CaseId
                        || approval.EffectId != discovered.EffectId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (approval.Revision != source.ExpectedApprovalRevision || approval.RevokedAt is not null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    approval.Revoke(source.ExpectedApprovalRevision, authority.UserId, now);
                    return new(ModerationQualificationSourceKind.NoticeDisclosure, approval.Id, approval.Revision);
                }
            case PublishModerationQualificationPolicySource source:
                {
                    var moderationCase = await ReadCaseAsync(
                        source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
                    await using var policySlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.qualification-policy:{authority.Binding.Id:N}:{moderationCase.Id:N}", token);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicySource>(
                        context, source.SourceId, token);
                    var policy = await context.ModerationQualificationPolicySources
                        .SingleOrDefaultAsync(row => row.Id == source.SourceId, token);
                    var generation = checked(authority.Epoch.Revision + 1);
                    if (source.ValidUntil <= now || source.ValidUntil == DateTimeOffset.MaxValue)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (source.ExpectedSourceRevision == 0)
                    {
                        if (policy is not null || await context.ModerationQualificationPolicySources.AsNoTracking()
                            .AnyAsync(row => row.HostBindingId == authority.Binding.Id && row.CaseId == moderationCase.Id, token))
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                        policy = ModerationQualificationPolicySource.Publish(
                            source.SourceId, authority.Binding.Id, authority.Binding.TrustDomainId,
                            authority.Tenant.Id, moderationCase.Id, moderationCase.Revision, generation,
                            source.MaximumReviewerLifetime, source.MaximumUrgencyLifetime, authority.UserId, now, source.ValidUntil);
                        context.ModerationQualificationPolicySources.Add(policy);
                    }
                    else
                    {
                        if (policy is null)
                            throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                        RequirePolicyCoordinates(policy, moderationCase, authority, requireCaseRevision: false);
                        if (policy.Revision != source.ExpectedSourceRevision)
                            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                        policy.Replace(source.ExpectedSourceRevision, moderationCase.Revision, generation,
                            source.MaximumReviewerLifetime, source.MaximumUrgencyLifetime, authority.UserId, now, source.ValidUntil);
                    }
                    return new(ModerationQualificationSourceKind.QualificationPolicy, policy.Id, policy.Revision,
                        MutationValidUntil: policy.ValidUntil);
                }
            case AdoptModerationQualificationPolicySource source:
                {
                    var moderationCase = await ReadCaseAsync(
                        source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
                    await using var policySlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.qualification-policy:{authority.Binding.Id:N}:{moderationCase.Id:N}", token);
                    var policy = await ReadQualificationPolicySourceAsync(
                        source.SourceId, source.ExpectedSourceRevision, moderationCase, authority, token);
                    if (policy.WriterGeneration != source.ExpectedWriterGeneration)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    if (!authority.Administrators.Contains(policy.PublishedByHumanId))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var publicationReceipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.SourceKind == ModerationQualificationSourceKind.QualificationPolicy
                            && row.SourceId == policy.Id && row.ResultRevision == policy.Revision, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    publicationReceipt.ValidatePersistedState();
                    if (publicationReceipt.HostBindingId != policy.HostBindingId
                        || publicationReceipt.TrustDomainId != policy.TrustDomainId
                        || publicationReceipt.TenantId != policy.TenantId
                        || publicationReceipt.PrincipalId != policy.PublishedByHumanId
                        || publicationReceipt.QualificationRevision != policy.WriterGeneration
                        || publicationReceipt.CompletedAt != policy.PublishedAt
                        || policy.WriterGeneration > authority.Epoch.Revision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (now < policy.PublishedAt || now >= policy.ValidUntil
                        || source.ValidUntil <= now || source.ValidUntil > policy.ValidUntil)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicyAdoption>(
                        context, source.AdoptionId, token);
                    if (await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
                        .AnyAsync(row => row.Id == source.AdoptionId
                            || row.SourceId == policy.Id && row.SourceRevision == policy.Revision, token))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var adoption = ModerationQualificationPolicyAdoption.Adopt(
                        source.AdoptionId, policy, authority.UserId, now, source.ValidUntil);
                    context.ModerationQualificationPolicyAdoptions.Add(adoption);
                    return new(ModerationQualificationSourceKind.QualificationPolicyAdoption, adoption.Id, adoption.Revision,
                        MutationValidUntil: adoption.ValidUntil);
                }
            case RevokeModerationQualificationPolicyAdoption source:
                {
                    var moderationCase = await ReadCaseAsync(
                        source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token,
                        requireOpen: false, requireCurrentAttribution: false);
                    await using var policySlot = await RelationalNamedLock.AcquireTransactionAsync(
                        context, $"moderation.qualification-policy:{authority.Binding.Id:N}:{moderationCase.Id:N}", token);
                    var policy = await ReadQualificationPolicySourceAsync(
                        source.SourceId, source.ExpectedSourceRevision, moderationCase, authority, token,
                        requireCaseRevision: false);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicyAdoption>(
                        context, source.AdoptionId, token);
                    var adoption = await context.ModerationQualificationPolicyAdoptions
                        .SingleOrDefaultAsync(row => row.Id == source.AdoptionId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    adoption.ValidatePersistedState();
                    if (adoption.SourceId != policy.Id || adoption.HostBindingId != policy.HostBindingId
                        || adoption.TrustDomainId != policy.TrustDomainId || adoption.TenantId != policy.TenantId
                        || adoption.CaseId != moderationCase.Id || adoption.CaseRevision != policy.CaseRevision)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (!authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    if (adoption.Revision != source.ExpectedAdoptionRevision || adoption.RevokedAt is not null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    adoption.Revoke(source.ExpectedAdoptionRevision, authority.UserId, now);
                    return new(ModerationQualificationSourceKind.QualificationPolicyAdoption, adoption.Id, adoption.Revision);
                }
            case OpenModerationCaseSource source:
                {
                    var subject = await ResolveSubjectAsync(source, authority, token);
                    var created = ModerationCase.Open(source.CaseId, subject, source.Target, source.OccurrenceId, now);
                    context.ModerationCases.Add(created);
                    return new(ModerationQualificationSourceKind.Case, created.Id, created.Revision);
                }
            case RecordModerationCaseResponse source:
                {
                    var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
                    if (moderationCase.AttributionSourceId is not null
                        || moderationCase.Subject is not { Kind: ModerationSubjectKind.Human } caseSubject
                        || caseSubject.Id != authority.UserId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var evidence = ModerationQualificationEvidence.RecordResponse(
                        source.EvidenceId, moderationCase.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
                        caseSubject.Id, authority.UserId, source.Statement, now);
                    context.ModerationQualificationEvidence.Add(evidence);
                    return new(ModerationQualificationSourceKind.Evidence, evidence.Id, evidence.Revision);
                }
            case AssignModerationReviewerSource source:
                {
                    // Triage ownership precedes responsibility attribution and confers no qualified review.
                    var moderationCase = await ReadCaseAsync(source.CaseId, source.Target,
                        source.ExpectedCaseRevision, authority, token, allowUnattributed: true);
                    var assignment = ModerationReviewerAssignment.Assign(
                        source.AssignmentId, moderationCase, source.ExpectedCaseRevision, source.ReviewerHumanId, authority.UserId, now);
                    context.ModerationReviewerAssignments.Add(assignment);
                    return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
                }
            case QualifyModerationReviewerController source:
                {
                    var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target, source.ExpectedAssignmentRevision, authority, token);
                    RequireIndependentAssessor(assignment, authority.UserId);
                    if (source.ControllerHumanId == authority.UserId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var moderationCase = await ReadCaseAsync(assignment.CaseId, source.Target, null, authority, token);
                    var policy = await RequireQualificationPolicyAsync(moderationCase, authority, now, token);
                    RequireValidityCeiling(source.QualifiedUntil, now, policy.Current.MaximumReviewerLifetime);
                    var evidence = ModerationQualificationEvidence.QualifyController(
                        source.EvidenceId, assignment.CaseId, authority.Binding.TrustDomainId, authority.Tenant.Id,
                        assignment.ReviewerHumanId, source.ControllerHumanId, authority.UserId, source.Justification, source.QualifiedUntil, now);
                    assignment.QualifyController(source.ExpectedAssignmentRevision, source.ControllerHumanId,
                        evidence.Id, authority.UserId, source.QualifiedUntil, now);
                    context.ModerationQualificationEvidence.Add(evidence);
                    return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision, policy);
                }
            case AssessModerationReviewerConflict source:
                {
                    var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target, source.ExpectedAssignmentRevision, authority, token);
                    RequireIndependentAssessor(assignment, authority.UserId);
                    if (assignment.ControllerHumanId is not { } controller || assignment.QualifiedUntil is not { } until || until <= now)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    var moderationCase = await ReadCaseAsync(assignment.CaseId, source.Target, null, authority, token);
                    var policy = await RequireQualificationPolicyAsync(moderationCase, authority, now, token);
                    var identity = assignment.IdentityEvidenceId is { } identityId
                        ? await context.ModerationQualificationEvidence.AsNoTracking()
                            .SingleOrDefaultAsync(row => row.Id == identityId, token) : null;
                    if (identity is null)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, identity.Id, token);
                    identity = await context.ModerationQualificationEvidence.AsNoTracking().SingleAsync(row => row.Id == identity.Id, token);
                    identity.ValidatePersistedState();
                    if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
                        || identity.CaseId != moderationCase.Id || identity.ReviewerHumanId != assignment.ReviewerHumanId
                        || identity.ControllerHumanId != controller || identity.ValidUntil != until
                        || identity.AssertedByHumanId != assignment.QualifiedByHumanId
                        || !authority.FencedHumans.Contains(identity.AssertedByHumanId)
                        || !authority.Administrators.Contains(identity.AssertedByHumanId)
                        || identity.RecordedAt > now)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    RequireValidityCeiling(until, identity.RecordedAt, policy.Current.MaximumReviewerLifetime);
                    var evidence = ModerationQualificationEvidence.AssessConflict(
                        source.EvidenceId, assignment.CaseId, authority.Binding.TrustDomainId, authority.Tenant.Id,
                        assignment.ReviewerHumanId, controller, authority.UserId, (int)source.State, source.Justification, until, now);
                    assignment.RecordConflictAssessment(source.ExpectedAssignmentRevision, source.State, evidence.Id, authority.UserId, now);
                    context.ModerationQualificationEvidence.Add(evidence);
                    return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision, policy);
                }
            case WithdrawModerationReviewerSource source:
                {
                    var assignment = await ReadReviewerAsync(source.AssignmentId, source.Target,
                        source.ExpectedAssignmentRevision, authority, token, requireCurrentCase: false);
                    assignment.Withdraw(source.ExpectedAssignmentRevision, authority.UserId, now);
                    return new(ModerationQualificationSourceKind.Reviewer, assignment.Id, assignment.Revision);
                }
            case QualifyModerationCaseUrgency source:
                {
                    var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
                    if (moderationCase.SubjectKind == ModerationSubjectKind.Human && moderationCase.SubjectId == authority.UserId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    var policy = await RequireQualificationPolicyAsync(moderationCase, authority, now, token);
                    RequireValidityCeiling(source.ValidUntil, now, policy.Current.MaximumUrgencyLifetime);
                    var evidence = ModerationQualificationEvidence.QualifyUrgency(
                        source.EvidenceId, moderationCase.Id, authority.Binding.TrustDomainId, authority.Tenant.Id,
                        moderationCase.SubjectKind == ModerationSubjectKind.Human ? moderationCase.SubjectId : null,
                        authority.UserId, source.Justification, source.ValidUntil, now);
                    context.ModerationQualificationEvidence.Add(evidence);
                    return new(ModerationQualificationSourceKind.Evidence, evidence.Id, evidence.Revision, policy);
                }
            case FinalizeModerationFindingSource source:
                return await FinalizeFindingAsync(source, authority, now, token);
            case ReverseModerationFindingSource source:
                {
                    var discovered = await context.ModerationFindings.AsNoTracking()
                        .SingleOrDefaultAsync(row => row.Id == source.FindingId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    if (!authority.FencedHumans.Contains(discovered.AdjudicatorId)
                        || discovered.SubjectKind == ModerationSubjectKind.Human
                            && !authority.FencedHumans.Contains(discovered.SubjectId))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                    var owner = await ReadCaseAsync(discovered.CaseId, source.Target, null, authority, token,
                        requireOpen: false, requireCurrentAttribution: false);
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationFinding>(context, source.FindingId, token);
                    var finding = await context.ModerationFindings.SingleOrDefaultAsync(row => row.Id == source.FindingId, token)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                    finding.ValidatePersistedState();
                    if (finding.Scope != source.Target || !finding.MatchesRetainedCase(owner))
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (finding.SubjectKind == ModerationSubjectKind.Human && finding.SubjectId == authority.UserId)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                    if (finding.Revision != source.ExpectedFindingRevision || finding.State != ModerationFindingState.Final)
                        throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
                    finding.Reverse(source.ReversalReferenceId, SubjectReference.Create(ModerationSubjectKind.Human,
                        authority.UserId, authority.Instance), source.Justification, source.ExpectedFindingRevision, now);
                    return new(ModerationQualificationSourceKind.Finding, finding.Id, finding.Revision);
                }
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private async Task<QualifiedSource> FinalizeFindingAsync(
        FinalizeModerationFindingSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var moderationCase = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
        if (source.Rule.OwnerId != authority.Instance.Id && source.Rule.OwnerId != authority.Tenant.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var publication = await publications.AcquireSelectedAsync(
            context.Database.CurrentTransaction!.TransactionId, authority.Binding.Id,
            source.Rule.OwnerId == authority.Instance.Id ? authority.Instance : authority.Tenant,
            source.PolicyId, false, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var selected = ModerationPolicyPublicationProjection.RestoreHistory(publication)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (publication.Revision != source.ExpectedPolicyRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var profile = selected.Profiles.SingleOrDefault(row => row.Definition.Rule == source.Rule);
        if (publication.HostBindingId != authority.Binding.Id || publication.PolicyId != source.PolicyId
            || !publication.Jurisdiction.Contains(source.Target) || profile is null
            || profile.Definition.Reason != source.Reason
            || moderationCase.SubjectKind is not { } subjectKind
            || !profile.Definition.SubjectKinds.Contains(subjectKind)
            || !profile.Definition.ScopeKinds.Contains(source.Target.Kind)
            || !profile.Reference.AcceptsScope(publication.Jurisdiction, source.Target))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var policy = await RequireQualificationPolicyAsync(moderationCase, authority, now, token);
        var assignmentId = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => row.CaseId == source.CaseId && row.ReviewerHumanId == authority.UserId && row.WithdrawnAt == null)
            .Select(row => row.Id).SingleOrDefaultAsync(token);
        var assignment = await ReadReviewerAsync(assignmentId, moderationCase.Scope, null, authority, token);
        if (!assignment.IsQualifiedAt(now))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RequireReviewerEvidenceAsync(assignment, authority, policy.Current.MaximumReviewerLifetime, now, token);
        var protectedResponse = moderationCase.AttributionSourceId is null ? null
            : await AcquirePreFindingResponseAsync(moderationCase, source.EvidenceId, authority, token);
        ModerationQualificationEvidence? evidence = null;
        if (protectedResponse is null)
        {
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, source.EvidenceId, token);
            evidence = await context.ModerationQualificationEvidence.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == source.EvidenceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            evidence.ValidatePersistedState();
            if (evidence.CaseId != moderationCase.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        ModerationFindingEvidenceReference reference;
        if (protectedResponse is not null)
            reference = protectedResponse;
        else if (moderationCase.AttributionSourceId is null && evidence?.Purpose == ModerationQualificationEvidencePurpose.Response
            && moderationCase.SubjectKind == ModerationSubjectKind.Human && evidence.SubjectHumanId == moderationCase.SubjectId)
            reference = ModerationFindingEvidenceReference.Response(evidence.Id, moderationCase.Id, moderationCase.Revision, evidence.RecordedAt);
        else if (evidence?.Purpose == ModerationQualificationEvidencePurpose.Urgency && evidence.ValidUntil > now
            && evidence.RecordedAt <= now
            && evidence.ValidUntil - evidence.RecordedAt <= policy.Current.MaximumUrgencyLifetime)
        {
            var discovered = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.SourceKind == ModerationQualificationSourceKind.Evidence
                    && row.SourceId == evidence.Id, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, discovered.Id, token);
            var qualification = await context.ModerationQualificationMutationReceipts.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == discovered.Id, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            qualification.ValidatePersistedState();
            if (qualification.Id == evidence.Id || qualification.SourceKind != ModerationQualificationSourceKind.Evidence
                || qualification.SourceId != evidence.Id || qualification.ResultRevision != evidence.Revision
                || qualification.HostBindingId != authority.Binding.Id
                || qualification.TrustDomainId != authority.Binding.TrustDomainId
                || qualification.TenantId != authority.Tenant.Id
                || qualification.PrincipalId != evidence.AssertedByHumanId
                || !authority.FencedHumans.Contains(evidence.AssertedByHumanId)
                || !authority.Administrators.Contains(evidence.AssertedByHumanId)
                || qualification.CompletedAt < evidence.RecordedAt || qualification.CompletedAt > now)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
            reference = ModerationFindingEvidenceReference.QualifiedUrgency(
                evidence.Id, qualification.Id, moderationCase.Id, moderationCase.Revision, evidence.RecordedAt);
        }
        else
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var attribution = moderationCase.AttributionSourceId is not null
            ? await ReadCurrentCaseAttributionAsync(moderationCase, authority, token)
            : null;
        var finding = ModerationFinding.Finalize(source.FindingId, moderationCase, source.ExpectedCaseRevision,
            source.Target, attribution?.Id, attribution?.Generation,
            source.Rule, profile.Definition.Rule, source.Reason, profile.Definition.Reason,
            SubjectReference.Create(ModerationSubjectKind.Human, authority.UserId, authority.Instance),
            reference, source.Justification, now);
        context.ModerationFindings.Add(finding);
        return new(ModerationQualificationSourceKind.Finding, finding.Id, finding.Revision, policy);
    }

    private async Task<ModerationCase> ReadCaseAsync(
        Guid id, AuthorityScope target, long? revision, ModerationSourceAuthority authority, CancellationToken token,
        bool requireOpen = true, IModerationFindingProvenanceSession? provenance = null,
        bool requireCurrentAttribution = true, bool allowUnattributed = false)
    {
        // Discovery must be completed by the native authority helper before it takes subordinate
        // fences. Never repair a missing human acquisition by taking a late human lock here.
        var discovered = await context.ModerationCases.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        if (discovered.Subject is null && !allowUnattributed)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (discovered.Subject is { Kind: ModerationSubjectKind.Human } caseSubject
            && !authority.FencedHumans.Contains(caseSubject.Id))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationCase>(context, id, token);
        var source = await context.ModerationCases.SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        if (source.Subject is null && !allowUnattributed || source.Scope != target
            && (source.AttributionSourceId is null || target.Kind != ModerationScopeKind.Resource
                || source.Scope != target.Parent))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (requireOpen && source.State != ModerationCaseState.Open || revision is { } expected && source.Revision != expected)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        if (requireCurrentAttribution && source.AttributionSourceId is not null)
        {
            if (caseAttribution is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            var attribution = provenance is null
                ? await caseAttribution.AcquireCurrentAttributionAsync(source.Id, token)
                : await caseAttribution.AcquireCurrentAttributionAsync(provenance, source.Id, token);
            if (attribution is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            _attributionAcquired = true;
        }
        return source;
    }

    private async Task<ModerationCaseAttributionSource> ReadCurrentCaseAttributionAsync(
        ModerationCase owner, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!_attributionAcquired || owner.AttributionSourceId is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var attribution = await (
            from head in context.ModerationCaseAttributionHeads.AsNoTracking()
            join source in context.ModerationCaseAttributionSources.AsNoTracking() on head.SourceId equals source.Id
            where head.CaseId == owner.Id && source.CaseId == owner.Id && source.Generation == head.Generation
            select source).SingleOrDefaultAsync(token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        attribution.ValidatePersistedState();
        if (attribution.HostBindingId != authority.Binding.Id || attribution.TenantId != authority.Tenant.Id
            || attribution.TrustDomainId != authority.Binding.TrustDomainId
            || !attribution.IsCurrentAt(clock.GetUtcNow()))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return attribution;
    }

    private async Task RequireCurrentCaseAttributionAsync(CancellationToken token)
    {
        if (_attributionAcquired && (caseAttribution is null
            || !await caseAttribution.RecheckAttributionAsync(token)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task<ModerationReviewerAssignment> ReadReviewerAsync(
        Guid id, AuthorityScope target, long? revision, ModerationSourceAuthority authority, CancellationToken token,
        bool requireCurrentCase = true)
    {
        var discovered = await context.ModerationReviewerAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var moderationCase = await ReadCaseAsync(discovered.CaseId, target, null, authority, token,
            requireOpen: requireCurrentCase, requireCurrentAttribution: requireCurrentCase,
            allowUnattributed: !requireCurrentCase);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationReviewerAssignment>(context, id, token);
        var source = await context.ModerationReviewerAssignments.SingleAsync(row => row.Id == id, token);
        source.ValidatePersistedState();
        if (source.CaseId != discovered.CaseId
            || source.SubjectHumanId != (moderationCase.SubjectKind == ModerationSubjectKind.Human ? moderationCase.SubjectId : (Guid?)null)
            || !authority.FencedHumans.Contains(source.ReviewerHumanId)
            || source.ControllerHumanId is { } controller && !authority.FencedHumans.Contains(controller))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (source.WithdrawnAt is not null || revision is { } expected && source.Revision != expected)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        return source;
    }

    private async Task<ModerationQualificationPolicyObservation> RequireQualificationPolicyAsync(
        ModerationCase moderationCase, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token) =>
        await activation.AcquireQualificationPolicyAsync(authority.Binding.Id, moderationCase, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);

    private async Task<ModerationQualificationPolicySource> ReadQualificationPolicySourceAsync(
        Guid sourceId, long expectedRevision, ModerationCase moderationCase,
        ModerationSourceAuthority authority, CancellationToken token, bool requireCaseRevision = true)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationPolicySource>(context, sourceId, token);
        var source = await context.ModerationQualificationPolicySources
            .SingleOrDefaultAsync(row => row.Id == sourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        RequirePolicyCoordinates(source, moderationCase, authority, requireCaseRevision);
        if (source.Revision != expectedRevision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        return source;
    }

    private static void RequirePolicyCoordinates(
        ModerationQualificationPolicySource source, ModerationCase moderationCase, ModerationSourceAuthority authority,
        bool requireCaseRevision = true)
    {
        source.ValidatePersistedState();
        if (!authority.FencedHumans.Contains(source.PublishedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || source.CaseId != moderationCase.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (requireCaseRevision && source.CaseRevision != moderationCase.Revision)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
    }

    private static void RequireValidityCeiling(DateTimeOffset until, DateTimeOffset recordedAt, TimeSpan ceiling)
    {
        if (until <= recordedAt || until - recordedAt > ceiling)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task RequireReviewerEvidenceAsync(
        ModerationReviewerAssignment assignment, ModerationSourceAuthority authority,
        TimeSpan ceiling, DateTimeOffset now, CancellationToken token)
    {
        if (assignment.IdentityEvidenceId is not { } identityId || assignment.ConflictEvidenceId is not { } conflictId
            || assignment.QualifiedByHumanId is not { } qualifier || assignment.AssessedByHumanId is not { } assessor
            || !authority.Administrators.Contains(qualifier) || !authority.Administrators.Contains(assessor))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        foreach (var id in new[] { identityId, conflictId }.Distinct().Order())
            await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationEvidence>(context, id, token);
        var identity = await context.ModerationQualificationEvidence.AsNoTracking().SingleOrDefaultAsync(row => row.Id == identityId, token);
        var conflict = await context.ModerationQualificationEvidence.AsNoTracking().SingleOrDefaultAsync(row => row.Id == conflictId, token);
        if (identity is null || conflict is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        identity.ValidatePersistedState();
        conflict.ValidatePersistedState();
        if (identity.Purpose != ModerationQualificationEvidencePurpose.Controller
            || conflict.Purpose != ModerationQualificationEvidencePurpose.Conflict
            || identity.CaseId != assignment.CaseId || conflict.CaseId != assignment.CaseId
            || identity.ReviewerHumanId != assignment.ReviewerHumanId || conflict.ReviewerHumanId != assignment.ReviewerHumanId
            || identity.ControllerHumanId != assignment.ControllerHumanId || conflict.ControllerHumanId != assignment.ControllerHumanId
            || identity.AssertedByHumanId != qualifier || conflict.AssertedByHumanId != assessor
            || conflict.ConflictState != (int)ModerationReviewerConflictState.Independent
            || identity.ValidUntil != assignment.QualifiedUntil || conflict.ValidUntil != assignment.QualifiedUntil
            || identity.ValidUntil <= now || conflict.ValidUntil <= now
            || identity.ValidUntil - identity.RecordedAt > ceiling || conflict.ValidUntil - conflict.RecordedAt > ceiling
            || identity.RecordedAt > conflict.RecordedAt || conflict.RecordedAt > now)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private async Task<SubjectReference> ResolveSubjectAsync(
        OpenModerationCaseSource source, ModerationSourceAuthority authority, CancellationToken token)
    {
        var tenantId = authority.Tenant.Id;
        switch (source.SubjectKind)
        {
            case ModerationSubjectKind.Human:
                if (source.Target == authority.Tenant && !await context.TenantUsers.AsNoTracking()
                    .AnyAsync(row => row.TenantId == tenantId && row.UserId == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Service:
                if (!authority.IsInstanceAdmin) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RelationalEntityRowFence.AcquireGlobalAsync<ServicePrincipal>(context, source.SubjectId, token);
                if (!await context.ServicePrincipals.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Organization:
                if (!authority.IsInstanceAdmin) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                await RelationalEntityRowFence.AcquireGlobalAsync<Organization>(context, source.SubjectId, token);
                if (!await context.Organizations.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Instance);
            case ModerationSubjectKind.Content:
                await RelationalEntityRowFence.AcquireAsync<Explore.Domain.Event>(context, tenantId, row => row.Id, source.SubjectId, token);
                if (!await context.Events.AsNoTracking().AnyAsync(row => row.Id == source.SubjectId && row.TenantId == tenantId && !row.IsDeleted, token))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
                return SubjectReference.Create(source.SubjectKind, source.SubjectId, authority.Tenant);
            default:
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
    }

    private Task<ModerationQualificationMutationReceipt?> ReadReceiptAsync(Guid operationId, CancellationToken token) =>
        context.ModerationQualificationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(row => row.Id == operationId, token);

    private static ModerationQualificationWriteResult MatchReceipt(
        ModerationQualificationMutationReceipt receipt, ModerationQualificationMutation request, string digest, ModerationSourceAuthority authority)
    {
        receipt.ValidatePersistedState();
        var expected = request switch
        {
            RecordModerationProfileCollectionProcedureSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ProfileCollectionProcedureSource, source.SourceId, 1),
            AdoptModerationProfileCollectionProcedureSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, source.AdoptionId, 1),
            WithdrawModerationProfileCollectionProcedureAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationClassificationMaterialSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationMaterialSource, source.SourceId, 1),
            AcceptModerationClassificationMaterialSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationMaterialReview, source.ReviewId, 1),
            EndModerationClassificationMaterialReview source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationMaterialReview, source.ReviewId, checked(source.ExpectedReviewRevision + 1)),
            RecordModerationClassificationTaxonomySource source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationTaxonomySource, source.SourceId, 1),
            AdoptModerationClassificationTaxonomySource source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, source.AdoptionId, 1),
            WithdrawModerationClassificationTaxonomyAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationParticipantIncidentResponsibilitySource source => new QualifiedSource(
                ModerationQualificationSourceKind.ParticipantIncidentResponsibilitySource, source.SourceId, 1),
            RecordModerationCaseNarrativePolicySource source => new QualifiedSource(
                ModerationQualificationSourceKind.CaseNarrativePolicySource, source.SourceId, 1),
            RecordModerationOperationalBudgetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.OperationalBudgetSource, source.SourceId, 1),
            AdoptModerationOperationalBudgetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.OperationalBudgetAdoption, source.AdoptionId, 1),
            WithdrawModerationOperationalBudgetAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.OperationalBudgetAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            AdoptModerationCaseNarrativePolicySource source => new QualifiedSource(
                ModerationQualificationSourceKind.CaseNarrativeAdoption, source.AdoptionId, 1),
            WithdrawModerationCaseNarrativeAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.CaseNarrativeAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationMeasuredOperationsSource source => new QualifiedSource(
                ModerationQualificationSourceKind.MeasuredOperationsSource, source.SourceId, 1),
            AcceptModerationMeasuredOperationsSource source => new QualifiedSource(
                ModerationQualificationSourceKind.MeasuredOperationsAcceptance, source.AcceptanceId, 1),
            WithdrawModerationMeasuredOperationsAcceptance source => new QualifiedSource(
                ModerationQualificationSourceKind.MeasuredOperationsAcceptance, source.AcceptanceId, checked(source.ExpectedAcceptanceRevision + 1)),
            RecordModerationEventRightsSource source => new QualifiedSource(
                ModerationQualificationSourceKind.EventRightsSource, source.SourceId, 1),
            AdoptModerationEventRightsSource source => new QualifiedSource(
                ModerationQualificationSourceKind.EventRightsAdoption, source.AdoptionId, 1),
            WithdrawModerationEventRightsAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.EventRightsAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationChildAgePolicySource source => new QualifiedSource(
                ModerationQualificationSourceKind.ChildAgePolicySource, source.SourceId, 1),
            AdoptModerationChildAgePolicySource source => new QualifiedSource(
                ModerationQualificationSourceKind.ChildAgePolicyAdoption, source.AdoptionId, 1),
            WithdrawModerationChildAgePolicyAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.ChildAgePolicyAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationRecognitionPrivacySource source => new QualifiedSource(
                ModerationQualificationSourceKind.RecognitionPrivacySource, source.SourceId, 1),
            AdoptModerationRecognitionPrivacySource source => new QualifiedSource(
                ModerationQualificationSourceKind.RecognitionPrivacyAdoption, source.AdoptionId, 1),
            WithdrawModerationRecognitionPrivacyAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.RecognitionPrivacyAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationConstitutionSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConstitutionSource, source.SourceId, 1),
            AdoptModerationConstitutionSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConstitutionAdoption, source.AdoptionId, 1),
            WithdrawModerationConstitutionAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.ConstitutionAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationConstitutionAvailability source => new QualifiedSource(
                ModerationQualificationSourceKind.ConstitutionAvailability, source.AvailabilityId, 1),
            WithdrawModerationConstitutionAvailability source => new QualifiedSource(
                ModerationQualificationSourceKind.ConstitutionAvailability, source.AvailabilityId, checked(source.ExpectedAvailabilityRevision + 1)),
            RecordModerationDeploymentSource source => new QualifiedSource(
                ModerationQualificationSourceKind.DeploymentSource, source.SourceId, 1),
            AdoptModerationDeploymentSource source => new QualifiedSource(
                ModerationQualificationSourceKind.DeploymentAdoption, source.AdoptionId, 1),
            WithdrawModerationDeploymentAdoption source => new QualifiedSource(
                ModerationQualificationSourceKind.DeploymentAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            AcceptModerationConsumerMeasurementSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConsumerMeasurementReceipt, source.ReceiptId, 1),
            EnrollModerationMeasurementIssuerSource source => new QualifiedSource(
                ModerationQualificationSourceKind.MeasurementIssuerEnrollment, source.EnrollmentId, 1),
            RevokeModerationMeasurementIssuerSource source => new QualifiedSource(
                ModerationQualificationSourceKind.MeasurementIssuerEnrollment, source.EnrollmentId, checked(source.ExpectedEnrollmentRevision + 1)),
            PrepareModerationConsumerMeasurementSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConsumerMeasurementChallenge, source.ChallengeId, 1),
            InstallModerationConsumerSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConsumerInstallation, source.InstallationId, 1),
            WithdrawModerationConsumerSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ConsumerInstallation, source.InstallationId, checked(source.ExpectedInstallationRevision + 1)),
            AdoptModerationResourceTargetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ResourceTargetAdoption, source.AdoptionId, 1),
            RevokeModerationResourceTargetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ResourceTargetAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            AdoptModerationProfileSource source => new QualifiedSource(ModerationQualificationSourceKind.ProfileAdoption, source.AdoptionId, 1),
            RevokeModerationProfileSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ProfileAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            AdoptModerationEventTargetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.EventTargetAdoption, source.AdoptionId, 1),
            RevokeModerationEventTargetSource source => new QualifiedSource(
                ModerationQualificationSourceKind.EventTargetAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            RecordModerationEventGenerationSource source => new QualifiedSource(
                ModerationQualificationSourceKind.EventGeneration, source.SourceId, checked(source.ExpectedEventRevision + 1)),
            RecordModerationResourceGenerationSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ResourceGeneration, source.SourceId, checked(source.ExpectedResourceRevision + 1)),
            RecordModerationContributionSelectionSource source => new QualifiedSource(
                ModerationQualificationSourceKind.ContributionSelection, source.SelectionId, 1),
            ApproveModerationNoticeDisclosureSource source => new QualifiedSource(
                ModerationQualificationSourceKind.NoticeDisclosure, source.Intent.ApprovalId, 1),
            ApproveModerationNoticeCorrectionDisclosureSource source => new QualifiedSource(
                ModerationQualificationSourceKind.NoticeDisclosure, source.Intent.ApprovalId, 1),
            RevokeModerationNoticeDisclosureSource source => new(
                ModerationQualificationSourceKind.NoticeDisclosure, source.ApprovalId, checked(source.ExpectedApprovalRevision + 1)),
            PublishModerationQualificationPolicySource source => new QualifiedSource(
                ModerationQualificationSourceKind.QualificationPolicy, source.SourceId, checked(source.ExpectedSourceRevision + 1)),
            AdoptModerationQualificationPolicySource source => new(
                ModerationQualificationSourceKind.QualificationPolicyAdoption, source.AdoptionId, 1),
            RevokeModerationQualificationPolicyAdoption source => new(
                ModerationQualificationSourceKind.QualificationPolicyAdoption, source.AdoptionId, checked(source.ExpectedAdoptionRevision + 1)),
            OpenModerationCaseSource source => new QualifiedSource(ModerationQualificationSourceKind.Case, source.CaseId, 1),
            RecordModerationCaseResponse source => new(ModerationQualificationSourceKind.Evidence, source.EvidenceId, 1),
            AssignModerationReviewerSource source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, 1),
            QualifyModerationReviewerController source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            AssessModerationReviewerConflict source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            WithdrawModerationReviewerSource source => new(ModerationQualificationSourceKind.Reviewer, source.AssignmentId, checked(source.ExpectedAssignmentRevision + 1)),
            QualifyModerationCaseUrgency source => new(ModerationQualificationSourceKind.Evidence, source.EvidenceId, 1),
            FinalizeModerationFindingSource source => new(ModerationQualificationSourceKind.Finding, source.FindingId, 1),
            ReverseModerationFindingSource source => new(ModerationQualificationSourceKind.Finding, source.FindingId, checked(source.ExpectedFindingRevision + 1)),
            _ => null
        };
        return receipt.RequestDigest == digest && receipt.PrincipalId == authority.UserId
            && receipt.HostBindingId == authority.Binding.Id && receipt.TrustDomainId == authority.Binding.TrustDomainId
            && receipt.TenantId == authority.Tenant.Id
            && expected is not null && receipt.SourceKind == expected.Kind && receipt.SourceId == expected.Id
            && receipt.ResultRevision == expected.Revision && receipt.QualificationRevision == checked(request.ExpectedQualificationRevision + 1)
            ? new(ModerationQualificationWriteOutcome.Known, receipt)
            : new(ModerationQualificationWriteOutcome.Conflict);
    }

    private static void RequireIndependentAssessor(ModerationReviewerAssignment assignment, Guid userId)
    {
        if (assignment.SubjectHumanId == userId || assignment.ReviewerHumanId == userId || assignment.ControllerHumanId == userId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    private sealed class AbortQualification(ModerationQualificationWriteOutcome outcome) : Exception
    {
        public ModerationQualificationWriteOutcome Outcome { get; } = outcome;
    }

    private sealed record QualifiedSource(
        ModerationQualificationSourceKind Kind, Guid Id, long Revision,
        ModerationQualificationPolicyObservation? Policy = null, DateTimeOffset? MutationValidUntil = null,
        (DateTimeOffset RetainUntil, int CalendarMonths)? NoticeRetention = null);
}
