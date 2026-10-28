using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

// Source scenarios only. Programme assurance owns compilation and execution.
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Publication_continuity_qualifies_original_publish_on_the_borrowed_native_transaction_without_keys()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var matched = await scenario.MatchAsync();
        await Assert.That(matched.Actions.Length).IsEqualTo(1);
        scenario.Keys.Available = false;
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        IModerationRecognitionContinuityReader reader = scenario.Service(context);
        var current = await reader.ReadPublicationInTransactionAsync(
            scenario.FreshSubject, scenario.Fixture.Database.Scope, CancellationToken.None);
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await Assert.That(current.Actions.Single().BindingId).IsEqualTo(matched.Actions.Single().BindingId);
        await Assert.That(current.Actions.Single().FreshSubjectId).IsEqualTo(scenario.FreshSubject);
        await Assert.That(current.Actions.Single().Scope).IsEqualTo(scenario.Fixture.Database.Scope);
        await Assert.That(current.Actions.Single().Capabilities.ToArray()).IsEquivalentTo(new[] { ModerationCapability.Publish });
        await Assert.That(current.CurrentUntil).IsEqualTo(Now.AddDays(2));
        await Assert.That(string.IsNullOrEmpty(current.Generation)).IsFalse();
    }

    [Test]
    [Arguments("other-capability")]
    [Arguments("descendant-scope")]
    public async Task Publication_continuity_ignores_irrelevant_binding_source_outages(string irrelevant)
    {
        await using var scenario = await ContinuityScenario.CreateAsync(
            capabilities: [irrelevant == "other-capability" ? ModerationCapability.ReadResource : ModerationCapability.Publish],
            actionScope: tenant => irrelevant == "descendant-scope"
                ? AuthorityScope.Resource(tenant, Guid.CreateVersion7()) : tenant);
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        var before = await ReadPublicationContinuityAsync(scenario);
        await RemoveResourceContinuityReceiptAsync(scenario, "source-receipt");
        scenario.Keys.Available = false;
        var after = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(before.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(before.Actions.Length).IsEqualTo(0);
        await Assert.That(after.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(after.Actions.Length).IsEqualTo(0);
        await Assert.That(after.CurrentUntil).IsNull();
        await Assert.That(after.Generation).IsEqualTo(before.Generation);
    }

    [Test]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("settlement")]
    public async Task Publication_continuity_applicable_unknown_never_uses_binding_alone(string missing)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        await RemoveResourceContinuityReceiptAsync(scenario, missing);
        var current = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(current.ActionFailure).IsEqualTo(missing == "settlement"
            ? ModerationRecognitionContinuityFailure.SettlementRequired
            : ModerationRecognitionContinuityFailure.SourceUnavailable);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
        await Assert.That(current.CurrentUntil).IsNull();
    }

    [Test]
    [Arguments("publish")]
    [Arguments("other-capability")]
    [Arguments("descendant-scope")]
    public async Task Publication_continuity_pending_recovery_is_exactly_publish_scoped_and_finite(string contribution)
    {
        await using var scenario = await ContinuityScenario.CreateAsync(
            capabilities: [contribution == "other-capability" ? ModerationCapability.Upload : ModerationCapability.Publish],
            actionScope: tenant => contribution == "descendant-scope"
                ? AuthorityScope.Resource(tenant, Guid.CreateVersion7()) : tenant);
        scenario.Keys.Available = false;
        var pending = await MatchRecoveryAsync(scenario, Guid.CreateVersion7());
        await Assert.That(pending.ActionRecoveryStaged).IsTrue();
        await Assert.That(pending.Actions.Length).IsEqualTo(0);
        var current = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(current.ActionFailure).IsEqualTo(contribution == "publish"
            ? ModerationRecognitionContinuityFailure.ActionRecoveryRequired
            : ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
        await Assert.That(current.CurrentUntil).IsNull();
        scenario.Fixture.Clock.UtcNow = Now.AddDays(2);
        var expired = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(expired.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(expired.Actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Publication_continuity_review_does_not_extend_or_shorten_the_original_finite_action()
    {
        await using var scenario = await ContinuityScenario.CreateAsync(reviewWithin: TimeSpan.FromHours(1));
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        scenario.Keys.Available = false;
        scenario.Fixture.Clock.UtcNow = Now.AddHours(1);
        var reviewed = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(reviewed.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(reviewed.Actions.Length).IsEqualTo(1);
        await Assert.That(reviewed.Actions.Single().ReviewAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(reviewed.CurrentUntil).IsEqualTo(Now.AddDays(2));
        scenario.Fixture.Clock.UtcNow = Now.AddDays(2).AddTicks(-1);
        var beforeEnd = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(beforeEnd.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(beforeEnd.Actions.Length).IsEqualTo(1);
        scenario.Fixture.Clock.UtcNow = Now.AddDays(2);
        var expired = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(expired.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(expired.Actions.Length).IsEqualTo(0);
        await Assert.That(expired.CurrentUntil).IsNull();
        await Assert.That(expired.Generation == beforeEnd.Generation).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Publication_continuity_validates_immutable_material_before_proving_irrelevance(bool unresolved)
    {
        await using var scenario = await ContinuityScenario.CreateAsync(
            capabilities: [ModerationCapability.ReadResource],
            actionScope: tenant => AuthorityScope.Resource(tenant, Guid.CreateVersion7()));
        scenario.Keys.Available = !unresolved;
        var matched = await MatchRecoveryAsync(scenario, Guid.CreateVersion7());
        await Assert.That(matched.ActionRecoveryStaged).IsEqualTo(unresolved);
        await Assert.That(matched.Actions.Length).IsEqualTo(unresolved ? 0 : 1);
        await Assert.That((await ReadPublicationContinuityAsync(scenario)).ActionFailure)
            .IsEqualTo(ModerationRecognitionContinuityFailure.None);
        var alteredResourceId = Guid.CreateVersion7();
        await using (var corruption = scenario.Fixture.Database.Context())
            await corruption.Set<ModerationRecognitionCaptureItem>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                .Where(row => row.IntentId == scenario.Fixture.Request.IntentId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ScopeResourceId, (Guid?)alteredResourceId));
        var current = await ReadPublicationContinuityAsync(scenario);
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("resource")]
    [Arguments("instance")]
    [Arguments("sibling-tenant")]
    [Arguments("host")]
    [Arguments("null")]
    public async Task Publication_continuity_accepts_only_the_exact_current_tenant_target(string invalid)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        var tenant = scenario.Fixture.Database.Scope;
        var target = invalid switch
        {
            "resource" => AuthorityScope.Resource(tenant, Guid.CreateVersion7()),
            "instance" => tenant.Parent!,
            "sibling-tenant" => AuthorityScope.Tenant(tenant.Parent!, Guid.CreateVersion7()),
            "host" => AuthorityScope.Tenant(AuthorityScope.Instance(tenant.Parent!.Parent!, Guid.CreateVersion7()), tenant.Id),
            _ => null!
        };
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = await scenario.Service(context).ReadPublicationInTransactionAsync(
            scenario.FreshSubject, target, CancellationToken.None);
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.JurisdictionUnavailable);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("transaction")]
    [Arguments("isolation")]
    [Arguments("subject")]
    public async Task Publication_continuity_requires_the_original_serializable_boundary_and_live_subject(string missing)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = missing == "transaction" ? null :
            await context.Database.BeginTransactionAsync(missing == "isolation"
                ? IsolationLevel.ReadUncommitted : IsolationLevel.Serializable);
        var current = await scenario.Service(context).ReadPublicationInTransactionAsync(
            missing == "subject" ? Guid.CreateVersion7() : scenario.FreshSubject,
            scenario.Fixture.Database.Scope, CancellationToken.None);
        await Assert.That(current.ActionFailure).IsEqualTo(missing == "subject"
            ? ModerationRecognitionContinuityFailure.SubjectUnavailable
            : ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
    }

    private static async Task<ModerationRecognitionContinuityResult> ReadPublicationContinuityAsync(ContinuityScenario scenario)
    {
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        return await scenario.Service(context).ReadPublicationInTransactionAsync(
            scenario.FreshSubject, scenario.Fixture.Database.Scope, CancellationToken.None);
    }
}
