using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task ResourceConsumer_bounds_direct_canonical_read_protection_only_for_its_original_scope()
    {
        var resource = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await using var fixture = await RecognitionErasureFixture.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        await using var context = fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var continuity = new Explore.Application.Services.Moderation.ModerationRecognitionContinuity(
            fixture.Repository(context), new Explore.Persistence.RelationalSettingMutationLock(context,
                new Explore.Persistence.EfCoreUnitOfWork(context)));
        var reader = new EventResourceModerationSnapshotReader(context,
            Substitute.For<IModerationMachineSubjectBindingRepository>(), continuity);
        var facts = await reader.ReadBatchAsync(
            [new(fixture.Database.Scope.Id, resource, fixture.Subject, false, "download"),
             new(fixture.Database.Scope.Id, sibling, fixture.Subject, false, "download")],
            fixture.Clock.UtcNow, CancellationToken.None);
        await Assert.That(facts[0].Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts[0].CurrentUntil).IsEqualTo(Now.AddDays(2));
        await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts[1].CurrentUntil).IsNull();
    }

    [Test]
    public async Task ResourceConsumer_applies_only_the_original_target_and_requalifies_reversal_without_keys()
    {
        var resource = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        scenario.Keys.Available = false;
        await using (var context = scenario.Fixture.Database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var reader = new EventResourceModerationSnapshotReader(context,
                Substitute.For<IModerationMachineSubjectBindingRepository>(), scenario.Service(context));
            var facts = await reader.ReadBatchAsync(
                [new(scenario.Fixture.Database.Scope.Id, resource, scenario.FreshSubject, false, "download"),
                 new(scenario.Fixture.Database.Scope.Id, sibling, scenario.FreshSubject, false, "download"),
                 new(scenario.Fixture.Database.Scope.Id, resource, scenario.FreshSubject, false, "view")],
                scenario.Fixture.Clock.UtcNow, CancellationToken.None);
            await Assert.That(facts[0].Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
            await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
            await Assert.That(facts[0].Generation).IsEqualTo(facts[2].Generation);
            await Assert.That(facts[0].CurrentUntil).IsEqualTo(Now.AddDays(2));
            await Assert.That(facts[1].CurrentUntil).IsNull();
            await Assert.That(facts[0].Subject!.Id).IsEqualTo(scenario.FreshSubject);
            await Assert.That(await context.ModerationSanctions.AnyAsync(row => row.SubjectId == scenario.FreshSubject)).IsFalse();
        }
        await using (var reversal = scenario.Fixture.Database.Context())
        {
            reversal.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
            var original = await reversal.ModerationSanctions.SingleAsync(row => row.Id == scenario.Fixture.SanctionId);
            original.Revoke(original.Revision, scenario.Fixture.Clock.UtcNow);
            await reversal.SaveChangesAsync();
        }
        await using var current = scenario.Fixture.Database.Context();
        await using var currentTransaction = await current.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var currentReader = new EventResourceModerationSnapshotReader(current,
            Substitute.For<IModerationMachineSubjectBindingRepository>(), scenario.Service(current));
        var released = await currentReader.ReadAsync(
            new(scenario.Fixture.Database.Scope.Id, resource, scenario.FreshSubject, false, "download"),
            scenario.Fixture.Clock.UtcNow, CancellationToken.None);
        await Assert.That(released.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(released.CurrentUntil).IsNull();
    }

    [Test]
    public async Task ResourceConsumer_closes_applicable_bad_receipt_without_closing_an_unrelated_resource()
    {
        var resource = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        await using (var corruption = scenario.Fixture.Database.Context())
        {
            var pending = (await scenario.Fixture.Repository(corruption).FindAsync(
                scenario.Fixture.Request.IntentId, CancellationToken.None))!;
            var receipt = pending.MaterialReceipt!;
            corruption.Attach(pending);
            corruption.Entry(pending).Property(nameof(ModerationRecognitionPending.MaterialReceipt)).CurrentValue =
                (receipt[0] == '0' ? "1" : "0") + receipt[1..];
            await corruption.SaveChangesAsync();
        }
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var reader = new EventResourceModerationSnapshotReader(context,
            Substitute.For<IModerationMachineSubjectBindingRepository>(), scenario.Service(context));
        var facts = await reader.ReadBatchAsync(
            [new(scenario.Fixture.Database.Scope.Id, resource, scenario.FreshSubject, false, "download"),
             new(scenario.Fixture.Database.Scope.Id, sibling, scenario.FreshSubject, false, "download")],
            scenario.Fixture.Clock.UtcNow, CancellationToken.None);
        await Assert.That(facts[0].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(facts[0].PermitsNativeEvaluationAt(scenario.Fixture.Clock.UtcNow)).IsFalse();
        await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts[1].CurrentUntil).IsNull();
    }
}
