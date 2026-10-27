using System.Collections.Immutable;
using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

// Source scenarios only. Programme assurance owns compilation and execution.
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("settlement")]
    public async Task Resource_continuity_applicable_unknown_closes_only_its_original_resource(string missing)
    {
        var resourceId = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            capabilities: [ModerationCapability.ReadResource],
            actionScope: tenant => AuthorityScope.Resource(tenant, resourceId));
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        var affected = AuthorityScope.Resource(scenario.Fixture.Database.Scope, resourceId);
        var sibling = AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7());
        var before = await ReadResourceContinuityAsync(scenario, [sibling, affected, sibling]);
        await Assert.That(before[0].Actions.Length).IsEqualTo(0);
        await Assert.That(before[1].Actions.Length).IsEqualTo(1);
        await Assert.That(before[1].Actions.Single().Scope).IsEqualTo(affected);
        await RemoveResourceContinuityReceiptAsync(scenario, missing);
        var after = await ReadResourceContinuityAsync(scenario, [sibling, affected, sibling]);
        await Assert.That(after.Length).IsEqualTo(3);
        await Assert.That(after[1].ActionFailure).IsEqualTo(missing == "settlement"
            ? ModerationRecognitionContinuityFailure.SettlementRequired
            : ModerationRecognitionContinuityFailure.SourceUnavailable);
        await Assert.That(after[1].Actions.Length).IsEqualTo(0);
        foreach (var index in new[] { 0, 2 })
        {
            await Assert.That(after[index].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
            await Assert.That(after[index].Actions.Length).IsEqualTo(0);
            await Assert.That(after[index].CurrentUntil).IsNull();
            await Assert.That(after[index].Generation).IsEqualTo(before[index].Generation);
        }
        await Assert.That(after[0].Generation).IsEqualTo(after[2].Generation);
    }

    [Test]
    public async Task Resource_continuity_due_review_preserves_action_without_shortening_sibling_lifetime()
    {
        var resourceId = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            reviewWithin: TimeSpan.FromHours(1), capabilities: [ModerationCapability.ReadResource],
            actionScope: tenant => AuthorityScope.Resource(tenant, resourceId));
        await scenario.MatchAsync();
        scenario.Keys.Available = false;
        scenario.Fixture.Clock.UtcNow = Now.AddHours(1);
        var affected = AuthorityScope.Resource(scenario.Fixture.Database.Scope, resourceId);
        var sibling = AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7());
        var result = await ReadResourceContinuityAsync(scenario, [affected, sibling]);
        await Assert.That(result[0].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(result[0].Actions.Length).IsEqualTo(1);
        await Assert.That(result[0].Actions.Single().ReviewAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(result[0].CurrentUntil).IsEqualTo(Now.AddDays(2));
        await Assert.That(result[1].Actions.Length).IsEqualTo(0);
        await Assert.That(result[1].CurrentUntil).IsNull();
        scenario.Fixture.Clock.UtcNow = Now.AddDays(2);
        var expired = await ReadResourceContinuityAsync(scenario, [affected, sibling]);
        await Assert.That(expired[0].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(expired[0].Actions.Length).IsEqualTo(0);
        await Assert.That(expired[0].Generation == result[0].Generation).IsFalse();
        await Assert.That(expired[1].Generation).IsEqualTo(result[1].Generation);
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("community")]
    public async Task Resource_continuity_original_ancestor_applies_only_with_exact_ancestry(string ancestor)
    {
        var communityId = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            capabilities: [ModerationCapability.ReadResource],
            actionScope: tenant => ancestor == "community" ? AuthorityScope.Community(tenant, communityId) : tenant);
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        var tenant = scenario.Fixture.Database.Scope;
        var first = AuthorityScope.Resource(AuthorityScope.Community(tenant, communityId), Guid.CreateVersion7());
        var second = AuthorityScope.Resource(AuthorityScope.Community(tenant, Guid.CreateVersion7()), first.Id);
        var result = await ReadResourceContinuityAsync(scenario, [first, second]);
        await Assert.That(result[0].Actions.Length).IsEqualTo(1);
        await Assert.That(result[1].Actions.Length).IsEqualTo(ancestor == "tenant" ? 1 : 0);
        await RemoveResourceContinuityReceiptAsync(scenario, "source-receipt");
        var unavailable = await ReadResourceContinuityAsync(scenario, [first, second]);
        await Assert.That(unavailable[0].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.SourceUnavailable);
        await Assert.That(unavailable[1].ActionFailure).IsEqualTo(ancestor == "tenant"
            ? ModerationRecognitionContinuityFailure.SourceUnavailable : ModerationRecognitionContinuityFailure.None);
    }

    [Test]
    public async Task Resource_continuity_maximum_batch_preserves_duplicate_targets_and_current_actions()
    {
        await using var scenario = await ContinuityScenario.CreateAsync(capabilities: [ModerationCapability.ReadResource]);
        var matched = await scenario.MatchAsync();
        await Assert.That(matched.Actions.Length).IsEqualTo(1);
        var target = AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7());
        var targets = Enumerable.Repeat(target, EventResourceAuthorityRequest.MaximumBatchResources).ToImmutableArray();
        var result = await ReadResourceContinuityAsync(scenario, targets);
        await Assert.That(result.Length).IsEqualTo(EventResourceAuthorityRequest.MaximumBatchResources);
        await Assert.That(result.All(item => item.ActionFailure == ModerationRecognitionContinuityFailure.None)).IsTrue();
        await Assert.That(result.All(item => item.Actions.Length == 1
            && item.Actions[0].BindingId == matched.Actions[0].BindingId)).IsTrue();
        await Assert.That(result.All(item => item.CurrentUntil == Now.AddDays(2))).IsTrue();
        await Assert.That(result.Select(item => item.Generation).Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("settlement")]
    public async Task Resource_continuity_ignores_unavailable_publish_only_contributions(string missing)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        var first = AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7());
        var second = AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7());
        var before = await ReadResourceContinuityAsync(scenario, [first, second]);
        await RemoveResourceContinuityReceiptAsync(scenario, missing);
        scenario.Keys.Available = false;
        var after = await ReadResourceContinuityAsync(scenario, [first, second]);
        for (var index = 0; index < after.Length; index++)
        {
            await Assert.That(after[index].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
            await Assert.That(after[index].Actions.Length).IsEqualTo(0);
            await Assert.That(after[index].CurrentUntil).IsNull();
            await Assert.That(after[index].Generation).IsEqualTo(before[index].Generation);
        }
        await Assert.That(after.Length).IsEqualTo(2);
        await Assert.That(after[0].Generation == after[1].Generation).IsFalse();
    }

    [Test]
    [Arguments("transaction")]
    [Arguments("subject")]
    public async Task Resource_continuity_unavailable_native_boundary_or_subject_closes_every_target(string missing)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        await using var context = scenario.Fixture.Database.Context();
        var targets = ImmutableArray.Create(
            AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7()),
            AuthorityScope.Resource(scenario.Fixture.Database.Scope, Guid.CreateVersion7()));
        await using var transaction = missing == "transaction" ? null :
            await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var result = await scenario.Service(context).ReadResourcesInTransactionAsync(
            missing == "subject" ? Guid.CreateVersion7() : scenario.FreshSubject,
            ModerationCapability.ReadResource, targets, CancellationToken.None);
        await Assert.That(result.Length).IsEqualTo(2);
        foreach (var target in result)
        {
            await Assert.That(target.ActionFailure).IsEqualTo(missing == "subject"
                ? ModerationRecognitionContinuityFailure.SubjectUnavailable
                : ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
            await Assert.That(target.Actions.Length).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments("tenant")]
    [Arguments("host")]
    [Arguments("non-resource")]
    [Arguments("capability")]
    [Arguments("oversized")]
    public async Task Resource_continuity_rejects_invalid_batch_authority(string invalid)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        var tenant = scenario.Fixture.Database.Scope;
        var target = invalid switch
        {
            "tenant" => AuthorityScope.Resource(AuthorityScope.Tenant(tenant.Parent!, Guid.CreateVersion7()), Guid.CreateVersion7()),
            "host" => AuthorityScope.Resource(AuthorityScope.Tenant(
                AuthorityScope.Instance(tenant.Parent!.Parent!, Guid.CreateVersion7()), tenant.Id), Guid.CreateVersion7()),
            "non-resource" => tenant,
            _ => AuthorityScope.Resource(tenant, Guid.CreateVersion7())
        };
        var targets = invalid == "oversized"
            ? Enumerable.Repeat(target, EventResourceAuthorityRequest.MaximumBatchResources + 1).ToImmutableArray()
            : ImmutableArray.Create(target);
        var result = await ReadResourceContinuityAsync(scenario, targets,
            invalid == "capability" ? ModerationCapability.Publish : ModerationCapability.ReadResource);
        await Assert.That(result.Length).IsEqualTo(targets.Length);
        foreach (var item in result)
        {
            await Assert.That(item.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.JurisdictionUnavailable);
            await Assert.That(item.Actions.Length).IsEqualTo(0);
        }
    }

    private static async Task<ImmutableArray<ModerationRecognitionContinuityResult>> ReadResourceContinuityAsync(
        ContinuityScenario scenario, ImmutableArray<AuthorityScope> targets,
        ModerationCapability capability = ModerationCapability.ReadResource)
    {
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        return await scenario.Service(context).ReadResourcesInTransactionAsync(
            scenario.FreshSubject, capability, targets, CancellationToken.None);
    }

    private static async Task RemoveResourceContinuityReceiptAsync(ContinuityScenario scenario, string missing)
    {
        await using var corrupt = scenario.Fixture.Database.Context();
        var item = await corrupt.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters("Target-qualified continuity missing receipt witness").SingleAsync();
        if (missing == "settlement")
        {
            await corrupt.Set<ModerationRecognitionPending>()
                .IgnoreAllFilters("Target-qualified continuity missing settlement witness")
                .Where(row => row.IntentId == item.IntentId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.MaterialCount, row => row.MaterialCount + 1));
        }
        else
        {
            var source = missing == "source-receipt" ? item.SourceId : item.AdoptionId;
            await corrupt.Set<ModerationQualificationMutationReceipt>()
                .IgnoreAllFilters("Target-qualified continuity missing source receipt witness")
                .Where(row => row.SourceId == source).ExecuteDeleteAsync();
        }
    }
}
