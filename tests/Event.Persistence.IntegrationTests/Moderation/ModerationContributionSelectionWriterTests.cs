using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationContributionSelectionWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompleteDraft_ExactRetryRetainsEveryTypedBindingWithoutCreatingTargetApprovalOrEffect(bool unchangedDecision)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        if (unchangedDecision)
            source = source with { ExpectedDecisionRevision = 1 };
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        await using var reader = Read(database);
        var selection = await reader.ModerationContributionSelections.Include(row => row.Bindings).SingleAsync();
        var decision = await reader.ModerationDecisions.SingleAsync();
        var restored = ModerationContributionSelectionProjection.Restore(selection, decision);
        await Assert.That(ModerationNoticeSnapshotFactory.SelectionDigest(restored))
            .IsEqualTo(ModerationNoticeSnapshotFactory.SelectionDigest(source.Contribution));
        await Assert.That(selection.Bindings.Count).IsEqualTo(source.Contribution.Effects.Length + source.Contribution.Consequences.Length);
        await Assert.That(selection.DecisionRevision).IsEqualTo(source.ExpectedDecisionRevision);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(decision.Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Draft_ChangedGenerationPublicationOrIncompleteBindingsCannotAdvanceEpoch(int substitution)
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        if (substitution == 4)
        {
            await using var writer = Read(database);
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.RecordResponse(1, Now);
            await writer.SaveChangesAsync();
        }
        source = substitution switch
        {
            0 => source with { ExpectedCaseRevision = 2 },
            1 => source with { ExpectedDecisionRevision = 3 },
            2 => source with { PublicationId = Guid.CreateVersion7() },
            4 => source with { ExpectedDecisionRevision = 1 },
            _ => source with { Contribution = source.Contribution with { Consequences = [] } }
        };
        var result = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationContributionSelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<ModerationContributionBinding>().CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task Draft_ExpiryDuringFlushRollsBackSelectionBindingsAndReceipt()
    {
        var (database, source) = await CreateAsync();
        await using var owner = database;
        database.ExpireGovernanceAfterSave = true;
        var result = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationContributionSelections.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.Set<ModerationContributionBinding>().CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    private static async Task<(SourceDatabase Database, RecordModerationContributionSelectionSource Source)> CreateAsync()
    {
        var database = await SourceDatabase.CreateAsync();
        var opened = database.OpenCase();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
        var request = database.Publication(2);
        var publication = await database.PublishAsync(database.AdministratorId, request);
        await Assert.That(publication.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        var profile = request.Proposed.Profiles.Single();
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
            SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance),
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish],
            Now, Now.AddHours(1), null, 1, Now);
        await using (var writer = Read(database))
        {
            writer.ModerationDecisions.Add(decision);
            await writer.SaveChangesAsync();
        }
        // Unknown object coordinates are legitimate draft inputs, never current target authority.
        var target = new ModerationPolicyTarget(database.Scope, decision.Subject.Kind, decision.Subject.Id,
            Guid.CreateVersion7(), 1, "resource.read", null);
        var contribution = new ModerationPolicyContribution(decision.Id, decision.Rule, decision.Scope,
            decision.Subject.Kind, decision.Subject.Id, decision.StartsAt, null,
            profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
            ImmutableArray.Create(new ModerationPolicyConsequenceTarget(null, target)));
        return (database, new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            SelectionId = Guid.CreateVersion7(),
            EffectId = Guid.CreateVersion7(),
            CaseId = opened.CaseId,
            ExpectedCaseRevision = 1,
            DecisionId = decision.Id,
            ExpectedDecisionRevision = 2,
            PolicyId = request.Proposed.PolicyId,
            ExpectedPolicyRevision = 1,
            PublicationId = publication.Publication!.Id,
            ProfileId = profile.Id,
            Contribution = contribution,
            ValidUntil = Now.AddMinutes(10)
        });
    }

    private static Explore.Persistence.ExploreDbContext Read(SourceDatabase database)
    {
        var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        return context;
    }
}
