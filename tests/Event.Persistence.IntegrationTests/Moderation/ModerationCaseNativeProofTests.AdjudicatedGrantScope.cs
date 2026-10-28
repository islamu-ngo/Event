using Explore.Application.Contracts.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationCaseNativeProofTests
{
    [Test]
    public async Task Adjudicated_read_grant_preserves_attributed_tenant_custody_of_a_resource_decision()
    {
        await using var fixture = await OriginalCaseScopeFixture.CreateAsync();
        GrantModerationAdjudicatedCaseRead request;
        await using (var reader = fixture.Context())
        {
            var owner = await reader.ModerationCases.AsNoTracking()
                .SingleAsync(row => row.Id == fixture.Original.Case.Id);
            var decision = await reader.ModerationDecisions.AsNoTracking()
                .SingleAsync(row => row.CaseId == owner.Id);
            var effect = await reader.ModerationEffectIntents.AsNoTracking()
                .SingleAsync(row => row.DecisionId == decision.Id
                    && row.OperationKind == (int)ModerationOperationKind.Activate);
            var source = await reader.ModerationNoticeSources.AsNoTracking()
                .SingleAsync(row => row.EffectId == effect.Id);
            await Assert.That(owner.AttributionSourceId).IsNotNull();
            await Assert.That(owner.Scope.Kind).IsEqualTo(ModerationScopeKind.Tenant);
            await Assert.That(decision.Scope.Kind).IsEqualTo(ModerationScopeKind.Resource);
            await Assert.That(decision.Scope.Parent).IsEqualTo(owner.Scope);
            request = new(Guid.CreateVersion7(), owner.Id, owner.Revision, fixture.Database.CallerId,
                new(effect.Id, source.SnapshotHash, source.EffectBindingHash),
                fixture.Database.Clock.GetUtcNow().AddMinutes(1));
        }
        await using (var writer = fixture.Context())
        {
            var (admin, machine) = fixture.Identity(writer, fixture.Original.Assessor);
            var repository = new ModerationAdjudicatedCaseReadGrantRepository(writer,
                fixture.Original.Privacy.Authority(writer, fixture.Database.Clock), admin, machine, fixture.Database.Clock);
            var granted = await repository.GrantAsync(request, default);
            await Assert.That(granted).IsNotNull();
            await Assert.That(granted!.TenantId).IsEqualTo(fixture.Database.TenantId);
            await Assert.That(granted.CaseId).IsEqualTo(request.CaseId);
            await Assert.That(granted.OriginalEffectId).IsEqualTo(request.Original.EffectId);
        }
        await using var recipient = fixture.Context();
        var (recipientIdentity, recipientMachine) = fixture.Identity(recipient, request.GranteeUserId);
        var readerRepository = new ModerationAdjudicatedCaseReadGrantRepository(recipient,
            fixture.Original.Privacy.Authority(recipient, fixture.Database.Clock),
            recipientIdentity, recipientMachine, fixture.Database.Clock);
        var current = await readerRepository.ReadCurrentAsync(request.OperationId,
            request.CaseId, request.ExpectedCaseRevision, default);
        await Assert.That(current).IsNotNull();
        await Assert.That(current!.RecipientUserId).IsEqualTo(request.GranteeUserId);
        await Assert.That(recipient.Database.CurrentTransaction).IsNull();
    }
}
