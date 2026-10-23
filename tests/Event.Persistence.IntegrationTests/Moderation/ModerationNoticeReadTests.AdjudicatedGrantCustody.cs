using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task A_notice_of_a_noncreating_transition_cannot_authorize_an_adjudicated_case_grant()
    {
        await using var fixture = await NoticeDatabase.CreateAsync();
        await using var context = fixture.Database.Context();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var source = await context.ModerationNoticeSources.AsNoTracking()
            .SingleAsync(row => row.EffectId == fixture.EffectId);
        var decision = await context.ModerationDecisions.AsNoTracking()
            .SingleAsync(row => row.Id == fixture.DecisionId);
        var allegation = await context.ModerationCases.AsNoTracking().SingleAsync(row => row.Id == decision.CaseId);
        var (identity, machine) = CorrectionIdentity(context, fixture.Database.AdministratorId);
        context.ChangeTracker.Clear();
        context.ModerationTrustDomainId = null;
        var repository = new ModerationAdjudicatedCaseReadGrantRepository(context,
            fixture.Privacy.Authority(context, fixture.Clock), identity, machine, fixture.Clock);

        var result = await repository.GrantAsync(new(Guid.CreateVersion7(), allegation.Id, allegation.Revision,
            fixture.Database.SubjectId, new ModerationOriginalEffectAnchor(source.EffectId,
                source.SnapshotHash, source.EffectBindingHash), fixture.Clock.GetUtcNow().AddSeconds(30)));

        await Assert.That(result).IsNull();
        context.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationAdjudicatedCaseReadGrant>().AnyAsync()).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }
}
