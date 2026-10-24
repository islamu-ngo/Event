using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationNoticeDisclosureWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeApproval_cannot_write_a_retention_ceiling_shorter_than_the_selected_calendar_floor()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        intent = intent with { RetainUntil = Now.AddDays(1) };
        var source = Approve(database, intent);
        var result = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExplicitNativeApproval_ExactRetryPreservesSafeContentWithoutCreatingFindingOrEffect(bool unchangedDecision)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        if (unchangedDecision)
            intent = intent with { ExpectedDecisionRevision = 1 };
        var source = Approve(database, intent);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        await using var reader = Read(database);
        var retained = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(retained.IsCurrentAt(intent, Now)).IsTrue();
        await Assert.That(retained.ApprovedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(retained.RetainUntil).IsEqualTo(intent.RetainUntil);
        await Assert.That(retained.DecisionRevision).IsEqualTo(intent.ExpectedDecisionRevision);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That((await reader.ModerationDecisions.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That(await reader.ModerationFindings.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationNotices.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task NativeApproval_ChangedCaseDecisionPublicationOrMeaningWritesNothing(int substitution)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        if (substitution == 4)
        {
            await using var writer = Read(database);
            var decision = await writer.ModerationDecisions.SingleAsync();
            decision.RecordResponse(1, Now);
            await writer.SaveChangesAsync();
        }
        intent = substitution switch
        {
            0 => intent with { ExpectedCaseRevision = intent.ExpectedCaseRevision + 1 },
            1 => intent with { ExpectedDecisionRevision = intent.ExpectedDecisionRevision + 1 },
            2 => intent with { PublicationId = Guid.CreateVersion7() },
            4 => intent with { ExpectedDecisionRevision = 1 },
            _ => intent with { RuleMeaning = new("Unapproved substituted rule meaning.") }
        };
        var result = await database.ExecuteAsync(database.AdministratorId, Approve(database, intent));
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task NativeApproval_ExpiryDuringFlushRollsBackApprovalEpochAndReceipt()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var source = Approve(database, intent);
        database.ExpireGovernanceAfterSave = true;
        var result = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task NativeApproval_calendar_floor_lost_during_flush_rolls_back_without_extending_retention()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        intent = intent with { RetainUntil = Now.AddMonths(6) };
        var source = Approve(database, intent);
        database.QualificationSaved = () => database.EvaluationTime = Now.AddTicks(1);

        var result = await database.ExecuteAsync(database.AdministratorId, source);

        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeApproval_ClaimedRoleOrDelegatedKeyCannotApproveSafeContent(bool machine)
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        var result = await database.ExecuteAsync(machine ? database.AdministratorId : database.SubjectId,
            Approve(database, intent), machineCaller: machine);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var reader = Read(database);
        await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    public async Task NativeRevocation_PreservesOriginalContentAndExactRetryWithoutRenewingApproval()
    {
        var (database, intent) = await CreateAsync();
        await using var owner = database;
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, Approve(database, intent))).IsSuccess).IsTrue();
        var revoke = new RevokeModerationNoticeDisclosureSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 4,
            ApprovalId = intent.ApprovalId,
            CaseId = intent.CaseId,
            ExpectedCaseRevision = intent.ExpectedCaseRevision,
            ExpectedApprovalRevision = 1
        };
        var first = await database.ExecuteAsync(database.AdministratorId, revoke);
        var replay = await database.ExecuteAsync(database.AdministratorId, revoke);
        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.QualificationRevision).IsEqualTo(first.QualificationRevision);
        await using var reader = Read(database);
        var row = await reader.ModerationNoticeDisclosureApprovals.SingleAsync();
        await Assert.That(row.IsCurrentAt(intent, Now)).IsFalse();
        await Assert.That(row.RuleMeaning).IsEqualTo(intent.RuleMeaning.Value);
        await Assert.That(row.ValidUntil).IsEqualTo(intent.ValidUntil);
        await Assert.That(row.RetainUntil).IsEqualTo(intent.RetainUntil);
        await Assert.That(row.Revision).IsEqualTo(2L);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    private static async Task<(SourceDatabase Database, ModerationNoticeDisclosureIntent Intent)> CreateAsync()
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
            database.Scope, database.Scope, profile.Rule, [ModerationCapability.Publish], Now, Now.AddHours(1), null, 1, Now);
        await using (var writer = Read(database))
        {
            writer.ModerationDecisions.Add(decision);
            await writer.SaveChangesAsync();
        }
        var intent = new ModerationNoticeDisclosureIntent(
            Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 2, opened.CaseId, 1,
            request.Proposed.PolicyId, 1, publication.Publication!.Id, profile.Id, Guid.CreateVersion7(), new string('a', 64),
            new(request.Proposed.Classes.Single().Meaning), new(request.Proposed.Reasons.Single().Meaning),
            ImmutableArray.Create(new ModerationNoticeSafeText("Explicitly approved subject-visible fact.")),
            new("Protected response remains available."), new("Request independent review."),
            new("No automated finding."), new("Purpose-bound retention."), Now.AddMinutes(10), Now.AddYears(1));
        return (database, intent);
    }

    private static ApproveModerationNoticeDisclosureSource Approve(SourceDatabase database, ModerationNoticeDisclosureIntent intent) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = database.Scope,
        ExpectedQualificationRevision = 3,
        Intent = intent
    };

    private static Explore.Persistence.ExploreDbContext Read(SourceDatabase database)
    {
        var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        return context;
    }
}
