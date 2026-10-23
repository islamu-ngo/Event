using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationProfileAdoptionWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(false, 1, 0, true)]
    [Arguments(false, 2, 0, true)]
    [Arguments(false, 2, 1, true)]
    [Arguments(false, 1, 1, false)]
    [Arguments(false, 2, 2, false)]
    [Arguments(true, 1, 0, true)]
    [Arguments(true, 2, 0, true)]
    [Arguments(true, 2, 1, true)]
    [Arguments(true, 1, 1, false)]
    [Arguments(true, 2, 2, false)]
    public async Task NativeProfileAdoption_AcceptsOnlyExactCurrentOrNextSelectionWithoutAuthorizingLifecycle(
        bool content, long selectionDecisionRevision, int decisionTransitions, bool accepted)
    {
        var (database, targetSource) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync(
            content, selectionDecisionRevision: selectionDecisionRevision);
        await using var owner = database;
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var decision = await writer.ModerationDecisions.SingleAsync();
            if (decisionTransitions > 0) decision.RecordResponse(1, Now);
            if (decisionTransitions > 1) decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
            await writer.SaveChangesAsync();
        }
        var source = new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = targetSource.Target, ExpectedQualificationRevision = 5,
            AdoptionId = Guid.CreateVersion7(), SelectionId = targetSource.SelectionId, Mode = ModerationNativeMode.Embedded,
            CaseId = targetSource.CaseId, ExpectedCaseRevision = 1, ValidUntil = Now.AddMinutes(5)
        };
        var result = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(result.IsSuccess).IsEqualTo(accepted);
        if (!accepted)
            await Assert.That(result.FailureCode).IsEqualTo(Explore.Application.Responses.FailureCodes.ConcurrencyConflict);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var selection = await reader.ModerationContributionSelections.SingleAsync();
        var unchanged = await reader.ModerationDecisions.SingleAsync();
        await Assert.That(selection.DecisionRevision).IsEqualTo(selectionDecisionRevision);
        await Assert.That(unchanged.Revision).IsEqualTo(1L + decisionTransitions);
        await Assert.That(unchanged.State).IsEqualTo(decisionTransitions switch
        {
            0 => ModerationDecisionState.Proposed,
            1 => ModerationDecisionState.AwaitingFinding,
            _ => ModerationDecisionState.AwaitingApproval
        });
        await Assert.That(await reader.ModerationProfileAdoptions.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        if (accepted)
        {
            var adoption = await reader.ModerationProfileAdoptions.SingleAsync();
            await Assert.That(adoption.SelectionId).IsEqualTo(selection.Id);
            await Assert.That(adoption.SelectionWriterGeneration).IsEqualTo(selection.WriterGeneration);
            await Assert.That(adoption.AdoptedByHumanId).IsEqualTo(database.UrgencyAssessorId);
        }
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId))
            .IsEqualTo(accepted);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(accepted ? 6L : 5L);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.EventResources.AllAsync(
            row => row.PublicationStateId == (int)Explore.Domain.Enums.EventResourcePublicationStateEnum.Draft)).IsTrue();
    }

    [Test]
    public async Task IndependentProfileAdoption_RetryAndRevocationRetainTheOriginalCompleteSelection()
    {
        var (database, targetSource) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        var source = new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
            AdoptionId = Guid.CreateVersion7(), SelectionId = targetSource.SelectionId, Mode = ModerationNativeMode.Embedded,
            CaseId = targetSource.CaseId, ExpectedCaseRevision = 1, ValidUntil = Now.AddMinutes(5)
        };
        var accepted = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        var replay = await database.ExecuteAsync(database.UrgencyAssessorId, source);
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        var revoke = new RevokeModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
            AdoptionId = source.AdoptionId, ExpectedAdoptionRevision = 1, CaseId = source.CaseId, ExpectedCaseRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, revoke)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var adoption = await reader.ModerationProfileAdoptions.SingleAsync();
        var selection = await reader.ModerationContributionSelections.SingleAsync();
        await Assert.That(adoption.Revision).IsEqualTo(2L);
        await Assert.That(adoption.SelectionId).IsEqualTo(selection.Id);
        await Assert.That(adoption.SelectionWriterGeneration).IsEqualTo(selection.WriterGeneration);
        await Assert.That(adoption.ProfileId).IsEqualTo(selection.ProfileId);
        await Assert.That(adoption.PublicationId).IsEqualTo(selection.PublicationId);
        await Assert.That(adoption.ModeCode).IsEqualTo((int)ModerationNativeMode.Embedded);
        await Assert.That((adoption.PrerequisiteMask & 17) == 17).IsTrue();
        await Assert.That(adoption.RevokedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task UnqualifiedProfileAdoption_DoesNotCommitAcceptedConfigurationPrefix(int change)
    {
        var (database, targetSource) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        var source = new AdoptModerationProfileSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
            AdoptionId = Guid.CreateVersion7(), SelectionId = targetSource.SelectionId, Mode = ModerationNativeMode.Embedded,
            CaseId = targetSource.CaseId, ExpectedCaseRevision = 1, ValidUntil = Now.AddMinutes(5)
        };
        if (change is 1 or 4)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change == 1)
                writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.SourceKind == ModerationQualificationSourceKind.ContributionSelection));
            else
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdministratorId));
            await writer.SaveChangesAsync();
        }
        if (change == 2) source = source with { Mode = ModerationNativeMode.RemoteStrict };
        if (change == 3) source = source with { ExpectedCaseRevision = 2 };
        if (change == 5) database.ExpireGovernanceAfterSave = true;
        await Assert.That((await database.ExecuteAsync(
            change == 0 ? database.AdministratorId : database.UrgencyAssessorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationProfileAdoptions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }
}
