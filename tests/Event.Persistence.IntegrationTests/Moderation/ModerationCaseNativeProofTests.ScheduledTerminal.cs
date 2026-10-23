using System.Collections.Immutable;
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task NativeAttributedHumanResponseAdvancesFromItsProtectedOriginalWithoutGenericEvidence()
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var result = await fixture.RespondAsync(context);
        await Assert.That(result.Result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.TrustId;
        var decision = await inspect.ModerationDecisions.SingleAsync(row => row.Id == result.DecisionId);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.AwaitingFinding);
        await Assert.That(decision.Revision).IsEqualTo(2);
        await Assert.That(await inspect.ModerationQualificationEvidence.AnyAsync(row =>
            row.CaseId == fixture.Case.Id && row.Purpose == ModerationQualificationEvidencePurpose.Response)).IsFalse();
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("receipt")]
    public async Task NativeAttributedHumanResponseCannotSubstituteGenericEvidenceForLostProtectedCustody(string loss)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using (var writer = fixture.Database.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.TrustId;
            writer.ModerationQualificationEvidence.Add(ModerationQualificationEvidence.RecordResponse(
                Guid.CreateVersion7(), fixture.Case.Id, fixture.Database.TrustId, fixture.Database.TenantId,
                fixture.Case.Subject!.Id, fixture.Case.Subject.Id, "A separate generic response.", fixture.Database.Clock.Now));
            await writer.SaveChangesAsync();
            if (loss == "receipt")
                await writer.Set<ModerationPreFindingReceipt>().Where(row => row.Id == fixture.ResponseId).ExecuteDeleteAsync();
        }
        if (loss == "expiry") fixture.Database.Clock.Now = fixture.Until;
        await using var context = fixture.Database.Context();
        var result = await fixture.RespondAsync(context);
        await Assert.That(result.Result.Outcome).IsNotEqualTo(ModerationOperationOutcome.Known);
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.TrustId;
        var decision = await inspect.ModerationDecisions.SingleAsync(row => row.Id == result.DecisionId);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Proposed);
        await Assert.That(decision.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task NativeAttributedHumanCannotCaptureGenericResponseOutsideProtectedPreFindingCustody()
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var result = await fixture.Original.Repository(context, fixture.Case.Subject!.Id).ExecuteAsync(
            new RecordModerationCaseResponse
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = epoch.Revision, EvidenceId = Guid.CreateVersion7(),
                CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision,
                Statement = "Use the existing protected response channel."
            }, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Denied);
        await Assert.That(await context.ModerationQualificationEvidence.AnyAsync(row =>
            row.CaseId == fixture.Case.Id && row.Purpose == ModerationQualificationEvidencePurpose.Response)).IsFalse();
    }

    [Test]
    [Arguments("none")]
    [Arguments("expiry")]
    [Arguments("receipt")]
    public async Task NativeAttributedHumanResponseRetainsOriginalCustodyAfterCompleteFlush(string loss)
    {
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync();
        var flush = new HumanResponseTerminalFlush(fixture, loss);
        await using var context = fixture.Database.Context(flush);
        var result = await fixture.RespondAsync(context);
        await Assert.That(flush.CompleteFlushCount).IsEqualTo(1);
        await Assert.That(result.Result.Outcome == ModerationOperationOutcome.Known).IsEqualTo(loss == "none");
        await using var inspect = fixture.Database.Context();
        inspect.ModerationTrustDomainId = fixture.Database.TrustId;
        var decision = await inspect.ModerationDecisions.SingleAsync(row => row.Id == result.DecisionId);
        await Assert.That(decision.State).IsEqualTo(loss == "none"
            ? ModerationDecisionState.AwaitingFinding : ModerationDecisionState.Proposed);
        await Assert.That(decision.Revision).IsEqualTo(loss == "none" ? 2 : 1);
        await Assert.That(await inspect.ModerationEffectIntents.AnyAsync(row =>
            row.DecisionId == result.DecisionId)).IsEqualTo(loss == "none");
        await Assert.That(await inspect.Set<ModerationPreFindingReceipt>()
            .AnyAsync(row => row.Id == fixture.ResponseId)).IsTrue();
    }

    [Test]
    public Task NativeScheduledExpiryCommitsApprovedCaptureWithoutActivatingInactivePolicy() =>
        ScheduledTerminalAsync(null);

    [Test]
    [Arguments("qualification-revision")]
    [Arguments("capture-deadline")]
    [Arguments("maintenance-credential")]
    [Arguments("maintenance-binding")]
    [Arguments("maintenance-service")]
    public Task NativeScheduledExpiryRollsBackWhenFinalFlushLosesExactQualification(string loss) =>
        ScheduledTerminalAsync(loss);

    [Test]
    public Task NativeScheduledExpiryDoesNotReopenWithdrawnOriginalResponseCustody() =>
        ScheduledTerminalAsync(null, withdrawResponse: true);

    [Test]
    [Arguments("custody")]
    [Arguments("notice")]
    [Arguments("response")]
    public Task NativeScheduledExpiryDoesNotBorrowOriginalResponseAfterCompleteFlush(string responseLoss) =>
        ScheduledTerminalAsync(null, responseLoss: responseLoss);

    [Test]
    [Arguments("current")]
    [Arguments("capture-deadline")]
    [Arguments("maintenance-credential")]
    public Task NativeScheduledExpiryRechecksAuthorityAfterDisclosureFinalization(string boundary) =>
        ScheduledTerminalAsync(boundary == "current" ? null : boundary, afterDisclosure: true);

    private static async Task ScheduledTerminalAsync(
        string? loss, bool withdrawResponse = false, string? responseLoss = null, bool afterDisclosure = false)
    {
        // Only these starting aggregates and credentials are historical setup.
        // The one-second finite profile is natively published, not a conformance claim.
        await using var fixture = await ScheduledProvenanceFixture.CreateAsync(endedInterval: true);
        var database = fixture.Database;
        var operation = fixture.Reconcile();
        await Assert.That(database.Clock.Now).IsEqualTo(fixture.Decision.EndsAt!.Value);
        await Assert.That(fixture.Sanction.State).IsEqualTo(ModerationSanctionState.Scheduled);
        if (withdrawResponse)
            await Assert.That((await fixture.Original.ExecuteAsync(fixture.PrivacyOwner,
                new WithdrawModerationPreFindingCustody(Guid.CreateVersion7(), fixture.Case.Id,
                    fixture.Case.Revision, fixture.CustodyId, 2))).Outcome)
                .IsEqualTo(ModerationQualificationWriteOutcome.Known);

        var generationId = Guid.CreateVersion7();
        var selectionId = Guid.CreateVersion7();
        var profileAdoptionId = Guid.CreateVersion7();
        ModerationPolicyPublication publication;
        ModerationPolicyProfileDefinition profile;
        await using (var reader = database.Context())
        {
            publication = await reader.ModerationPolicyPublications.AsNoTracking()
                .SingleAsync(row => row.JurisdictionTenantId == fixture.Case.Scope.Id);
            profile = ModerationPolicyPublicationProjection.RestoreHistory(publication)!
                .Proposed.Profiles.Single();
        }
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Assessor,
            new RecordModerationEventGenerationSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, SourceId = generationId,
                EventId = database.EventId, ExpectedEventRevision = 0, ValidUntil = fixture.Until
            });
        var target = new ModerationPolicyTarget(fixture.Case.Scope, fixture.Decision.Subject.Kind,
            fixture.Decision.Subject.Id, database.EventId, 1, "event.publish", null);
        var contribution = new ModerationPolicyContribution(fixture.Decision.Id, fixture.Decision.Rule,
            fixture.Case.Scope, fixture.Decision.Subject.Kind, fixture.Decision.Subject.Id,
            fixture.Decision.StartsAt, null,
            profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
            [new(null, target)]);
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Assessor,
            new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, SelectionId = selectionId, EffectId = operation.EffectId,
                CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision,
                DecisionId = fixture.Decision.Id, ExpectedDecisionRevision = fixture.Decision.Revision,
                PolicyId = publication.PolicyId, ExpectedPolicyRevision = publication.Revision,
                PublicationId = publication.Id, ProfileId = profile.Id,
                Contribution = contribution, ValidUntil = fixture.Until
            });
        // Each real retained ordinal is independently adopted against the native Event generation.
        foreach (var binding in ModerationContributionSelectionProjection.Bindings(selectionId, contribution))
            await WriteScheduledTerminalSourceAsync(fixture, fixture.PrivacyOwner,
                new AdoptModerationEventTargetSource
                {
                    OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                    ExpectedQualificationRevision = 1, AdoptionId = Guid.CreateVersion7(),
                    SelectionId = selectionId, BindingOrdinal = binding.Ordinal, GenerationId = generationId,
                    ExpectedGenerationRevision = 1, CaseId = fixture.Case.Id,
                    ExpectedCaseRevision = fixture.Case.Revision, ValidUntil = fixture.Until
                });
        await WriteScheduledTerminalSourceAsync(fixture, fixture.PrivacyOwner, new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
            ExpectedQualificationRevision = 1, AdoptionId = profileAdoptionId,
            SelectionId = selectionId, Mode = ModerationNativeMode.Embedded,
            CaseId = fixture.Case.Id, ExpectedCaseRevision = fixture.Case.Revision, ValidUntil = fixture.Until
        });
        var draft = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        var reason = draft.Proposed.Reasons.Single(row => row.Reference == profile.Reason);
        var classification = draft.Proposed.Classes.Single(row => row.Reference == reason.Class);
        var disclosure = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), operation.EffectId,
            fixture.Decision.Id, fixture.Decision.Revision, fixture.Case.Id, fixture.Case.Revision,
            publication.PolicyId, publication.Revision, publication.Id, profile.Id, fixture.Decision.Id,
            ModerationNoticeSnapshotFactory.SelectionDigest(contribution),
            new(classification.Meaning), new(reason.Meaning),
            [new("The scheduled publication restriction has reached its original end.")],
            new("Protected response remains available."), new("Request independent review."),
            new("Maintenance expires a contribution; it makes no new finding."),
            new("The original interval is unchanged."), fixture.Until, database.Clock.Now.AddYears(1));
        await WriteScheduledTerminalSourceAsync(fixture, fixture.Original.Review.Caller.UserId!.Value,
            new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(), Target = fixture.Case.Scope,
                ExpectedQualificationRevision = 1, Intent = disclosure
            });

        long qualificationRevision;
        long? disclosureEpoch;
        int notificationCount;
        int outboxCount;
        await using (var reader = database.Context())
        {
            qualificationRevision = (await reader.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision;
            disclosureEpoch = await reader.Set<EventDiscoveryRevision>().AsNoTracking()
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            notificationCount = await reader.NotificationIntents.CountAsync();
            outboxCount = await reader.OutboxMessages.CountAsync();
        }
        var finalization = new BudgetDisclosureFinalizationCheckpoint(database.TenantId, disclosureEpoch, async current =>
        {
            await Assert.That(current.ChangeTracker.HasChanges()).IsFalse();
            if (loss == "capture-deadline")
                database.Clock.Now = fixture.Until;
            else if (loss == "maintenance-credential")
                await Assert.That(await current.ExternalApiKeys
                    .Where(row => row.KeyId == fixture.Principal.KeyId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked))).IsEqualTo(1);
        });
        var flush = new ScheduledTerminalFlush(fixture, operation, qualificationRevision,
            afterDisclosure ? null : loss, responseLoss, afterDisclosure ? finalization : null);
        var commit = new ScheduledTerminalCommit();
        await using var context = database.Context(flush, commit, finalization);
        var (admin, machine) = ScheduledTerminalIdentity(context, fixture, null);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var publications = new ModerationPolicyPublicationRepository(context, admin, machine, inventory, null, database.Clock);
        var activation = new ModerationNativeActivationQualification(context, inventory, null,
            new ModerationQualificationPolicySourceOwner(context, database.Clock), database.Clock);
        var keys = new ExternalApiKeyRepository(context);
        var bindings = new ModerationMachineSubjectBindingRepository(context, admin, machine, keys,
            new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), database.Clock);
        var responses = new RecordingScheduledResponseOwner(fixture.Responses(context));
        var participant = new ModerationAuthorityParticipant(context, admin, machine, bindings, keys,
            publications, inventory, activation, database.Clock,
            caseAttribution: fixture.Attribution(context), preFindingResponses: responses);
        var selections = new ModerationPolicySelectionSource(context, publications, inventory);
        var observed = new ScheduledTerminalNoticeOwner(
            new ModerationPolicySelectionNoticeSourceOwner(context, selections, participant, database.Clock),
            participant, publication, profileAdoptionId);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var materializer = new RecipientNotificationMaterializer(
            new NotificationIntentRepository(context, mutationLock, database.Clock), unitOfWork, mutationLock,
            new PrivacyErasureStateRepository(context));
        var privacy = fixture.Original.Privacy.Authority(context, database.Clock);
        var capture = new ModerationNoticeCaptureRepository(context, privacy, observed, selections,
            new ModerationNoticeRecipientRepository(context),
            new ModerationNoticeNativeMaterializer(
                new DefaultNotificationOwnershipResolver(Options.Create(new NotificationRoutingOptions())),
                new NotificationPreferenceResolver(context), materializer), database.Clock, participant);
        context.ModerationTrustDomainId = null;
        var result = await new ModerationOperationService(
            new ModerationStore(context, participant, database.Clock, privacy, capture),
            participant, new ModerationEffectPlanner(), database.Clock).ExecuteAsync(operation);

        // These callbacks witness the actual complete primary flush, not a staged acquisition.
        await Assert.That(flush.CompleteFlushCount).IsEqualTo(1);
        if (afterDisclosure)
            await Assert.That(finalization.ObservedAdvancement).IsTrue();
        await Assert.That(observed.Observation).IsNotNull();
        await Assert.That(observed.Configuration).IsNotNull();
        await Assert.That(context.CurrentUserService!.UserId).IsNull();
        // Already-ended expiry is not finding/response activation qualification.
        await Assert.That(responses.Session).IsNull();
        await using var inspect = database.Context();
        var originalNotice = await inspect.Set<ModerationPreFindingNotice>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.NoticeId);
        await Assert.That(originalNotice.RetainUntil).IsEqualTo(fixture.Until);
        await Assert.That(originalNotice.Withdrawn).IsEqualTo(withdrawResponse);
        await Assert.That(originalNotice.ResponseId).IsEqualTo(fixture.ResponseId);
        await Assert.That(originalNotice.AcknowledgedAt).IsEqualTo(originalNotice.RespondedAt);
        await Assert.That(originalNotice.RespondedAt).IsNotNull();
        if (withdrawResponse || responseLoss == "notice")
            await Assert.That(originalNotice.ProtectedNotice.IsEmpty).IsTrue();
        if (withdrawResponse || responseLoss == "response")
            await Assert.That(originalNotice.ProtectedResponse.IsEmpty).IsTrue();
        if (responseLoss == "custody")
            await Assert.That((await inspect.Set<ModerationPreFindingCustody>().AsNoTracking()
                .SingleAsync(row => row.Id == fixture.CustodyId)).Revision).IsEqualTo(3L);
        var decision = await inspect.ModerationDecisions.AsNoTracking().SingleAsync(row => row.Id == fixture.Decision.Id);
        var sanction = await inspect.ModerationSanctions.AsNoTracking().SingleAsync(row => row.Id == fixture.Sanction.Id);
        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Scheduled);
        await Assert.That(decision.Revision).IsEqualTo(fixture.Decision.Revision);
        await Assert.That(sanction.StartsAt).IsEqualTo(fixture.Sanction.StartsAt);
        await Assert.That(sanction.EndsAt).IsEqualTo(fixture.Sanction.EndsAt);
        await Assert.That((await inspect.ModerationQualificationEpochs.AsNoTracking().SingleAsync()).Revision)
            .IsEqualTo(qualificationRevision);
        if (afterDisclosure)
        {
            var committedEpoch = await inspect.Set<EventDiscoveryRevision>().AsNoTracking()
                .Select(row => (long?)row.DisclosureEpoch).SingleOrDefaultAsync();
            await Assert.That(committedEpoch).IsEqualTo(loss is null
                ? disclosureEpoch.GetValueOrDefault() + 1 : disclosureEpoch);
            await Assert.That((await inspect.ExternalApiKeys.AsNoTracking()
                .SingleAsync(row => row.KeyId == fixture.Principal.KeyId)).ExternalApiKeyStatusId)
                .IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
        }
        if (loss is not null)
        {
            await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Unavailable);
            await Assert.That(commit.CommittedCount).IsEqualTo(0);
            await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Scheduled);
            await Assert.That(sanction.Revision).IsEqualTo(fixture.Sanction.Revision);
            await Assert.That(await inspect.ModerationOperationReceipts.AnyAsync(row => row.Id == operation.OperationId)).IsFalse();
            await Assert.That(await inspect.ModerationEffectIntents.AnyAsync(row => row.Id == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNoticeSources.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.ModerationNotices.AnyAsync(row => row.EffectId == operation.EffectId)).IsFalse();
            await Assert.That(await inspect.NotificationIntents.CountAsync()).IsEqualTo(notificationCount);
            await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(outboxCount);
            await Assert.That((await inspect.ExternalApiKeys.AsNoTracking()
                .SingleAsync(row => row.KeyId == fixture.Principal.KeyId)).ExternalApiKeyStatusId)
                .IsEqualTo((int)ExternalApiKeyStatusEnum.Active);
            await Assert.That((await inspect.ModerationMachineSubjectBindings.AsNoTracking()
                .SingleAsync(row => row.KeyId == fixture.Principal.KeyId)).ServicePrincipalId)
                .IsEqualTo(fixture.MaintenanceId);
            await Assert.That((await inspect.ServicePrincipals.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.MaintenanceId)).IsDeleted).IsFalse();
            return;
        }
        await Assert.That(result.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await Assert.That(commit.CommittedCount).IsEqualTo(1);
        await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(sanction.Revision).IsEqualTo(fixture.Sanction.Revision + 1);
        await Assert.That(result.Revision).IsEqualTo(sanction.Revision);
        var receipt = await inspect.ModerationOperationReceipts.AsNoTracking().SingleAsync(row => row.Id == operation.OperationId);
        await Assert.That(receipt.PrincipalId).IsEqualTo(fixture.MaintenanceId);
        await Assert.That(receipt.ResultRevision).IsEqualTo(sanction.Revision);
        var effect = await inspect.ModerationEffectIntents.AsNoTracking().SingleAsync(row => row.Id == operation.EffectId);
        await Assert.That(effect.CompletedAt).IsNull();
        var source = await inspect.ModerationNoticeSources.AsNoTracking().SingleAsync(row => row.EffectId == effect.Id);
        var captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
        await Assert.That(captured.Matches(effect)).IsTrue();
        await Assert.That(captured.HasNoSubstantiveWork).IsFalse();
        await Assert.That(captured.Decision.Revision).IsEqualTo(fixture.Decision.Revision);
        await Assert.That(captured.Notice!.SanctionState).IsEqualTo(ModerationSanctionState.Expired);
        await Assert.That(captured.Notice.ApprovalId).IsEqualTo(disclosure.ApprovalId);
        await Assert.That(captured.ValidUntil).IsEqualTo(fixture.Until);
        await Assert.That(captured.Reviewers.Length).IsEqualTo(2);
        await Assert.That(observed.Observation!.Targets.Single().Target.TargetId).IsEqualTo(database.EventId);
        await Assert.That(observed.Observation.Targets.Single().ApprovedSource.Id).IsEqualTo(generationId);
        await Assert.That(observed.Observation.Targets.Single().ApprovedSource)
            .IsEqualTo(observed.Observation.Targets.Single().CurrentSource);
        await Assert.That(await inspect.ModerationNotices.CountAsync(row => row.EffectId == effect.Id)).IsEqualTo(3);
        await Assert.That(await inspect.NotificationIntents.CountAsync()).IsEqualTo(notificationCount + 3);
        await Assert.That(await inspect.OutboxMessages.CountAsync()).IsEqualTo(outboxCount + 1);
    }

    private static (AdminContext Admin, MachinePrincipalAccessor Machine) ScheduledTerminalIdentity(
        ExploreDbContext context, ScheduledProvenanceFixture fixture, Guid? human)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        context.CurrentUserService = new NativeCaller(human ?? Guid.Empty)
        {
            UserId = human, IsAuthenticated = human is not null
        };
        if (human is { } id)
            http.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "scheduled-terminal-source"));
        var admin = new AdminContext(http, new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        if (human is null) machine.SetPrincipal(fixture.Principal);
        return (admin, machine);
    }

    private static async Task WriteScheduledTerminalSourceAsync(
        ScheduledProvenanceFixture fixture, Guid human, ModerationQualificationMutation mutation)
    {
        await using var context = fixture.Database.Context();
        var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
        var (admin, machine) = ScheduledTerminalIdentity(context, fixture, human);
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var clock = fixture.Database.Clock;
        context.ModerationTrustDomainId = null;
        var result = await new ModerationQualificationRepository(context, admin, machine,
            new ModerationPolicyPublicationRepository(context, admin, machine, inventory, null, clock),
            new ModerationNativeActivationQualification(context, inventory, null,
                new ModerationQualificationPolicySourceOwner(context, clock), clock),
            clock, fixture.Original.Privacy.Authority(context, clock),
            responseProtection: fixture.Database.NarrativeProtection,
            caseAttribution: new ModerationCaseTargetProofOwner(context, context.CurrentUserService!, machine, clock))
            .ExecuteAsync(mutation with { ExpectedQualificationRevision = epoch.Revision }, default);
        await Assert.That(result.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
    }

    /// <summary>Records only observations returned by the real acquired native source owners.</summary>
    private sealed class ScheduledTerminalNoticeOwner(
        IModerationNoticeSourceOwner native, ModerationAuthorityParticipant participant,
        ModerationPolicyPublication publication, Guid profileAdoptionId) : IModerationNoticeSourceOwner
    {
        public ModerationNoticeSourceObservation? Observation { get; private set; }
        public ModerationActivationProfileObservation? Configuration { get; private set; }

        public async Task<ModerationNoticeSourceObservation?> AcquireAsync(
            Guid transactionId, ModerationEffectIntent effect, ModerationDecision decision,
            ModerationSanction? sanction, ModerationAuthorityGrant grant,
            DateTimeOffset observedAt, CancellationToken token)
        {
            Observation = await native.AcquireAsync(transactionId, effect, decision, sanction, grant, observedAt, token);
            if (Observation is not null)
            {
                Configuration = await participant.AcquireProfileConfigurationAsync(
                    transactionId, publication, decision, grant, observedAt, token);
                await Assert.That(Configuration).IsNotNull();
                await Assert.That(Configuration!.SourceId).IsEqualTo(profileAdoptionId);
                await Assert.That(Configuration.DecisionRevision).IsEqualTo(decision.Revision);
            }
            return Observation;
        }
    }

    /// <summary>Changes original reply custody after the new response transition and capture have flushed.</summary>
    private sealed class HumanResponseTerminalFlush(ScheduledProvenanceFixture fixture, string loss)
        : SaveChangesInterceptor
    {
        public int CompleteFlushCount { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (CompleteFlushCount != 0 || context.ChangeTracker.HasChanges())
                return result;
            var response = context.ModerationEffectIntents.Local.SingleOrDefault(row =>
                row.DecisionId != fixture.Decision.Id && row.OperationKind == (int)ModerationOperationKind.RecordResponse);
            if (response is null || !context.ModerationNoticeSources.Local.Any(row => row.EffectId == response.Id))
                return result;
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            await Assert.That(context.IdentityFenceOwnsTransaction).IsTrue();
            await Assert.That(await context.ModerationEffectIntents.AsNoTracking()
                .AnyAsync(row => row.Id == response.Id, token)).IsTrue();
            await Assert.That(await context.ModerationNoticeSources.AsNoTracking()
                .AnyAsync(row => row.EffectId == response.Id, token)).IsTrue();
            await Assert.That(await context.ModerationDecisions.AsNoTracking().AnyAsync(row =>
                row.Id == response.DecisionId && row.State == ModerationDecisionState.AwaitingFinding, token)).IsTrue();
            CompleteFlushCount++;
            if (loss == "expiry")
                fixture.Database.Clock.Now = fixture.Until;
            else if (loss == "receipt")
                await Assert.That(await context.Set<ModerationPreFindingReceipt>()
                    .Where(row => row.Id == fixture.ResponseId).ExecuteDeleteAsync(token)).IsEqualTo(1);
            return result;
        }
    }

    /// <summary>Injects loss after every notice is flushed, inside the same primary transaction.</summary>
    private sealed class ScheduledTerminalFlush(
        ScheduledProvenanceFixture fixture, ModerationOperation operation, long qualificationRevision,
        string? loss, string? responseLoss, BudgetDisclosureFinalizationCheckpoint? finalization = null)
        : SaveChangesInterceptor
    {
        public int CompleteFlushCount { get; private set; }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken token = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (CompleteFlushCount != 0 || context.ModerationNotices.Local.Count(row => row.EffectId == operation.EffectId) != 3
                || context.ChangeTracker.HasChanges())
                return result;
            var effect = await context.ModerationEffectIntents.AsNoTracking()
                .SingleAsync(row => row.Id == operation.EffectId, token);
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == operation.EffectId, token);
            var captured = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
            await Assert.That(captured.Matches(effect)).IsTrue();
            await Assert.That(captured.HasNoSubstantiveWork).IsFalse();
            await Assert.That(await context.ModerationNotices.CountAsync(row => row.EffectId == effect.Id, token)).IsEqualTo(3);
            await Assert.That(context.Database.CurrentTransaction).IsNotNull();
            CompleteFlushCount++;
            if (finalization is not null)
            {
                await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
                context.DisclosureMutations.Enlist([fixture.Database.TenantId]);
                finalization.Armed = true;
            }
            // Adversarial original-response loss is not a new finding, capture
            // permission or fabricated custody receipt. Ended expiry must not
            // borrow this evidence or resurrect its private payload.
            if (responseLoss == "custody")
                await Assert.That(await context.Set<ModerationPreFindingCustody>()
                    .Where(row => row.Id == fixture.CustodyId && row.Revision == 2)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.Revision, row => row.Revision + 1), token)).IsEqualTo(1);
            else if (responseLoss == "notice")
                await Assert.That(await context.Set<ModerationPreFindingNotice>()
                    .Where(row => row.Id == fixture.NoticeId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => EF.Property<byte[]>(row, "protectedNotice"), Array.Empty<byte>()), token)).IsEqualTo(1);
            else if (responseLoss == "response")
                await Assert.That(await context.Set<ModerationPreFindingNotice>()
                    .Where(row => row.Id == fixture.NoticeId && row.ResponseId == fixture.ResponseId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => EF.Property<byte[]>(row, "protectedResponse"), Array.Empty<byte>()), token)).IsEqualTo(1);
            if (loss == "qualification-revision")
            {
                // Adversarial revision loss, not qualification setup or a fabricated receipt.
                await Assert.That(await context.ModerationQualificationEpochs
                    .Where(row => row.Revision == qualificationRevision)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Revision, row => row.Revision + 1), token))
                    .IsEqualTo(1);
            }
            else if (loss == "capture-deadline")
                fixture.Database.Clock.Now = captured.ValidUntil!.Value;
            else if (loss == "maintenance-credential")
                await Assert.That(await context.ExternalApiKeys
                    .Where(row => row.KeyId == fixture.Principal.KeyId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.ExternalApiKeyStatusId, (int)ExternalApiKeyStatusEnum.Revoked), token)).IsEqualTo(1);
            else if (loss == "maintenance-binding")
                await Assert.That(await context.ModerationMachineSubjectBindings
                    .Where(row => row.KeyId == fixture.Principal.KeyId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.ServicePrincipalId, fixture.Database.ServiceId), token)).IsEqualTo(1);
            else if (loss == "maintenance-service")
                await Assert.That(await context.ServicePrincipals
                    .Where(row => row.Id == fixture.MaintenanceId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.IsDeleted, true), token)).IsEqualTo(1);
            return result;
        }
    }

    private sealed class ScheduledTerminalCommit : DbTransactionInterceptor
    {
        public int CommittedCount { get; private set; }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            CommittedCount++;
            return Task.CompletedTask;
        }
    }
}
