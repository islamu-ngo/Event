using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task PreparationCommitsExactFutureBindingWithoutSelectingOrActivatingItsDefinition()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var prepared = await database.Writer(context).PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(prepared.Candidate!.Id).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(prepared.Candidate.DefinitionId == prepared.Candidate.Id).IsFalse();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var retained = await database.Writer(context).AcquireCandidateAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now);
        await Assert.That(retained).IsNotNull();
        var restored = ModerationPolicyPublicationProjection.RestoreCandidate(retained!, database.Inventory)!;
        await Assert.That(restored.Publication.PublicationId).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(restored.Publication.ProposedRevision).IsEqualTo(1L);
        await Assert.That(ModerationPolicyPublicationProjection.ProposedDigest(restored.Proposed))
            .IsEqualTo(ModerationPolicyPublicationProjection.ProposedDigest(request.Proposed));
        await Assert.That(await database.Writer(context).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, false)).IsNull();
        await Assert.That(await database.Writer(context).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, true)).IsNull();
        await Assert.That(retained!.Definition!.AdoptedRevision).IsNull();
        await Assert.That(retained.Definition.Evidence.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExactPreparationReplayDoesNotAdvanceEpochOrDuplicateDefinition()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var writer = database.Writer(context);
        var first = await writer.PrepareAsync(request, Now.AddMinutes(10));
        var repeated = await writer.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(first.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(repeated.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(repeated.Candidate!.DefinitionId).IsEqualTo(first.Candidate!.DefinitionId);
        await database.AssertCountsAsync(1, 0, 2);
        var changed = await writer.PrepareAsync(request, Now.AddMinutes(11));
        await Assert.That(changed.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Conflict);
        await database.AssertCountsAsync(1, 0, 2);
    }

    [Test]
    public async Task ChangedPredecessorInvalidatesTheCandidate()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        var active = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var preparation = database.Context();
        await Assert.That((await database.Writer(preparation).PrepareAsync(active, Now.AddMinutes(10))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var adopted = request with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 2,
            Proposed = request.Proposed with { PublicationId = Guid.CreateVersion7() }
        };
        await Assert.That((await database.PublishAsync(adopted)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.Writer(context).AcquireCandidateAsync(transaction.TransactionId,
            database.HostId, database.Scope, active.Proposed.PolicyId, active.Proposed.PublicationId, Now)).IsNull();
    }

    [Test]
    public async Task LostOriginalPreparerRoleInvalidatesTheCandidate()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using (var preparation = database.Context())
            await Assert.That((await database.Writer(preparation).PrepareAsync(request, Now.AddMinutes(10))).Outcome)
                .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.PlatformUserRoles.Remove(await context.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdminId));
        await context.SaveChangesAsync();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.Writer(context).AcquireCandidateAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now)).IsNull();
    }

    [Test]
    public async Task CompetingUnadoptedDefinitionsDoNotOccupyTheUniqueAdoptedRevision()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var writer = database.Writer(context);
        var first = await writer.PrepareAsync(request, Now.AddMinutes(10));
        var competing = request with
        {
            OperationId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 2,
            Proposed = request.Proposed with { PublicationId = Guid.CreateVersion7() }
        };
        var second = await writer.PrepareAsync(competing, Now.AddMinutes(10));
        await Assert.That(first.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(second.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(first.Candidate!.DefinitionId == second.Candidate!.DefinitionId).IsFalse();
        await database.AssertCountsAsync(2, 0, 3);
    }

    [Test]
    public async Task CandidateExpiryRejectsCurrentAcquisitionWithoutAnyTimingWait()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        await Assert.That((await database.Writer(context).PrepareAsync(request, Now.AddMinutes(10))).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.That(await database.Writer(context).AcquireCandidateAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId,
            Now.AddMinutes(10))).IsNull();
    }

    [Test]
    public async Task FailureAfterPreparedGraphFlushRollsBackDefinitionCandidateAndEpoch()
    {
        await using var database = await Database.CreateAsync();
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context(new RejectSavedGraph());
        await Assert.That(async () => await database.Writer(context).PrepareAsync(request, Now.AddMinutes(10)))
            .Throws<PublicationWriteInterrupted>();
        await database.AssertCountsAsync(0, 0, 1);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationPolicyPublicationCandidates.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<Explore.Domain.ModerationPolicyPublicationPlan>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AClosedOrReplacedOriginalBorrowCannotReadOrBecomeAConformanceSource(bool replaceTransaction)
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        request = await database.PrepareActiveAsync(request);
        await using (var writerContext = database.Context())
            await Assert.That((await database.Writer(writerContext, new EvidenceOwner(database, writerContext))
                .PublishAsync(request)).Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var evidence = await database.ConsumerEvidenceAsync(context, request);
        var nativeEvidence = (ModerationConsumerEvidenceSession)evidence;
        var acquired = await nativeEvidence.AcquireNativeAsync(transaction.TransactionId, Now, default);
        await Assert.That(acquired).IsNotNull();
        await Assert.That(ReferenceEquals(acquired!.Context, context)).IsTrue();
        await Assert.That(ReferenceEquals(acquired.Transaction, transaction)).IsTrue();
        await Assert.That(acquired.Authority.Binding.Id).IsEqualTo(database.HostId);
        await Assert.That(acquired.Policy.Publication.PublicationId).IsEqualTo(request.Proposed.PublicationId);
        await Assert.That(await nativeEvidence.AcquireNativeAsync(Guid.CreateVersion7(), Now, default)).IsNull();
        var primary = await evidence.AcquirePrimaryDatabaseAsync(Now, default);
        await Assert.That(primary).IsNotNull();
        await Assert.That(primary!.ProviderCode).IsEqualTo("Sqlite");
        await Assert.That(primary.ProviderVersion).IsEqualTo(context.Database.GetDbConnection().ServerVersion);
        await Assert.That(primary.TransactionId).IsEqualTo(transaction.TransactionId);
        Event.Tests.Shared.Moderation.ModerationMeasurementJournalAttribute.RecordDatabaseObservation(
            TUnit.Core.TestContext.Current!, primary.ProviderCode, primary.ProviderVersion, primary.ConfigurationDigest);
        var requalifier = database.Writer(context, new EvidenceOwner(database, context));
        await Assert.That(await requalifier.RequalifyActivationAsync(evidence, transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now))
            .IsEqualTo(Now.AddHours(1));
        var publication = await database.Writer(context).AcquireSelectedAsync(transaction.TransactionId,
            database.HostId, database.Scope, request.Proposed.PolicyId, true);
        var policy = ModerationPolicyPublicationProjection.Restore(publication!, database.Inventory)!;
        await Assert.That(await nativeEvidence.ReadProfileConfigurationAsync(transaction.TransactionId,
            publication!, ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
                SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), database.Scope.Parent!),
                SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), database.Scope.Parent!),
                database.Scope, database.Scope, policy.Profiles[0].Definition.Rule,
                [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now), Now, default)).IsNull();
        var binding = policy.Consumers[0].Binding;
        await Assert.That(await evidence.AcquireInstallationAsync(binding, "resource.payload", Now, default)).IsNull();
        await Assert.That(await evidence.AcquireMeasurementAsync(binding, "resource.payload", Now, default)).IsNull();
        if (replaceTransaction)
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.That(await nativeEvidence.AcquireNativeAsync(transaction.TransactionId, Now, default)).IsNull();
            await Assert.That(await nativeEvidence.AcquireNativeAsync(replacement.TransactionId, Now, default)).IsNull();
            await Assert.That(await requalifier.RequalifyActivationAsync(evidence, replacement.TransactionId,
                database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now)).IsNull();
            await Assert.That(await evidence.AcquirePrimaryDatabaseAsync(Now, default)).IsNull();
            await Assert.That(await evidence.AcquireInstallationAsync(binding, "resource.payload", Now, default)).IsNull();
            await Assert.That(await evidence.AcquireMeasurementAsync(binding, "resource.payload", Now, default)).IsNull();
        }
        else
        {
            evidence.Dispose();
            await Assert.That(evidence.TransactionId).IsEqualTo(Guid.Empty);
            await Assert.That(ReferenceEquals(context.Database.CurrentTransaction, transaction)).IsTrue();
            await Assert.That(await nativeEvidence.AcquireNativeAsync(transaction.TransactionId, Now, default)).IsNull();
            await Assert.That(await evidence.AcquirePrimaryDatabaseAsync(Now, default)).IsNull();
            await Assert.That(await requalifier.RequalifyActivationAsync(evidence, transaction.TransactionId,
                database.HostId, database.Scope, request.Proposed.PolicyId, request.Proposed.PublicationId, Now)).IsNull();
            await Assert.That(await evidence.AcquireInstallationAsync(binding, "resource.payload", Now, default)).IsNull();
            await Assert.That(await evidence.AcquireMeasurementAsync(binding, "resource.payload", Now, default)).IsNull();
        }
    }

    [Test]
    public async Task ActivePublicationCannotBypassCommittedPreparation()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var owner = new EvidenceOwner(database, context);
        var result = await database.Writer(context, owner).PublishAsync(request);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Unavailable);
        await database.AssertCountsAsync(0, 0, 1);
    }

    [Test]
    public async Task QualifiedFinalPublicationPreservesReservedIdentityAndLeavesPreparationImmutable()
    {
        await using var database = await Database.CreateAsync(implementedInventory: true);
        var request = database.Request();
        request = request with { Proposed = request.Proposed with { RequestedState = ModerationPolicyRequestedState.Active } };
        await using var context = database.Context();
        var writer = database.Writer(context, new EvidenceOwner(database, context));
        var prepared = await writer.PrepareAsync(request, Now.AddMinutes(10));
        await Assert.That(prepared.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var final = request with { OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 2 };
        var published = await writer.PublishAsync(final);
        await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        await Assert.That(published.Publication!.Id).IsEqualTo(prepared.Candidate!.Id);
        await Assert.That(published.Publication.AdoptedRevision).IsEqualTo(1L);
        await Assert.That(published.Publication.DeclaredStateDigest).IsEqualTo(prepared.Candidate.ProposedStateDigest);
        await database.AssertCountsAsync(2, 1, 3);
    }
}
