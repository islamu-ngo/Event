using System.Data;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Action_key_outage_can_commit_minimized_uncertainty_without_claiming_an_admission_match()
    {
        var resource = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        var loginId = Guid.CreateVersion7();
        scenario.Keys.Available = false;
        var result = await MatchRecoveryAsync(scenario, loginId);
        await Assert.That(result.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.KeyUnavailable);
        await Assert.That(result.ActionRecoveryStaged).IsTrue();
        await Assert.That(result.AdmissionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(result.AdmissionExcluded).IsFalse();
        await Assert.That(result.Actions.Length).IsEqualTo(0);
        await using var context = scenario.Fixture.Database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var read = await scenario.Service(context).ReadResourcesInTransactionAsync(scenario.FreshSubject,
            ModerationCapability.ReadResource,
            [AuthorityScope.Resource(scenario.Fixture.Database.Scope, resource),
             AuthorityScope.Resource(scenario.Fixture.Database.Scope, sibling)], CancellationToken.None);
        await Assert.That(read[0].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.ActionRecoveryRequired);
        await Assert.That(read[0].Actions.Length).IsEqualTo(0);
        await Assert.That(read[1].ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(read[1].CurrentUntil).IsNull();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.Id == loginId)).IsTrue();
        context.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        await Assert.That(await context.ModerationSanctions.AnyAsync(row => row.SubjectId == scenario.FreshSubject)).IsFalse();
    }

    [Test]
    public async Task Action_outage_cannot_commit_recovery_from_a_local_receipt_with_mismatched_retained_fact()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using (var authority = scenario.Fixture.Retained.CreateDbContext())
            await authority.ErasureIntents.Where(row => row.IntentId == scenario.Fixture.Request.IntentId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PolicyVersion, row => row.PolicyVersion + 1));
        scenario.Keys.Available = false;
        var result = await MatchRecoveryAsync(scenario, Guid.CreateVersion7());
        await Assert.That(result.ActionRecoveryStaged).IsFalse();
        await Assert.That(result.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.KeyUnavailable);
        await using var context = scenario.Fixture.Database.Context();
        await Assert.That(await context.Set<ModerationRecognitionActionRecovery>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
        await Assert.That(await context.Users.AnyAsync(row => row.Id == scenario.FreshSubject)).IsFalse();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.UserId == scenario.FreshSubject)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Changed_capture_scope_cannot_prove_target_irrelevance_or_settle_original_uncertainty(bool unresolved)
    {
        var resource = Guid.CreateVersion7();
        var sibling = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        scenario.Keys.Available = !unresolved;
        var matched = await MatchRecoveryAsync(scenario, Guid.CreateVersion7());
        await Assert.That(matched.ActionRecoveryStaged).IsEqualTo(unresolved);
        await using (var corruption = scenario.Fixture.Database.Context())
            await corruption.Set<ModerationRecognitionCaptureItem>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
                .Where(row => row.IntentId == scenario.Fixture.Request.IntentId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ScopeResourceId, (Guid?)sibling));
        await using (var context = scenario.Fixture.Database.Context())
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await scenario.Service(context).ReadResourcesInTransactionAsync(scenario.FreshSubject,
                ModerationCapability.ReadResource,
                [AuthorityScope.Resource(scenario.Fixture.Database.Scope, resource),
                 AuthorityScope.Resource(scenario.Fixture.Database.Scope, sibling)], CancellationToken.None);
            await Assert.That(current.All(row =>
                row.ActionFailure == ModerationRecognitionContinuityFailure.CanonicalUnavailable)).IsTrue();
            await Assert.That(current.All(row => row.Actions.IsEmpty)).IsTrue();
        }
        if (unresolved)
        {
            scenario.Keys.Available = true;
            await RetryRecoveriesAsync(scenario);
            await using var verify = scenario.Fixture.Database.Context();
            await Assert.That((await verify.Set<ModerationRecognitionActionRecovery>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync()).ResolvedAt).IsNull();
            await Assert.That(await verify.Set<ModerationRecognitionContinuityBinding>()
                .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
        }
    }

    [Test]
    public async Task Actual_matching_of_the_pinned_live_login_resolves_uncertainty_and_retains_original_action_only()
    {
        var resource = Guid.CreateVersion7();
        await using var scenario = await ContinuityScenario.CreateAsync(
            actionScope: tenant => AuthorityScope.Resource(tenant, resource),
            capabilities: [ModerationCapability.ReadResource]);
        var loginId = Guid.CreateVersion7();
        scenario.Keys.Available = false;
        await Assert.That((await MatchRecoveryAsync(scenario, loginId)).ActionRecoveryStaged).IsTrue();
        scenario.Keys.Available = true;
        var resolved = await MatchRecoveryAsync(scenario, loginId);
        await Assert.That(resolved.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(resolved.ActionRecoveryStaged).IsFalse();
        await Assert.That(resolved.Actions.Length).IsEqualTo(1);
        var current = await scenario.ReadAsync();
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Single().Scope).IsEqualTo(AuthorityScope.Resource(scenario.Fixture.Database.Scope, resource));
        await Assert.That(current.Actions.Single().EndsAt).IsEqualTo(Now.AddDays(2));
    }

    [Test]
    public async Task Native_recovery_retries_the_committed_pinned_login_without_an_enrollment_request()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var loginId = Guid.CreateVersion7();
        scenario.Keys.Available = false;
        await Assert.That((await MatchRecoveryAsync(scenario, loginId)).ActionRecoveryStaged).IsTrue();
        scenario.Keys.Available = true;
        await RetryRecoveriesAsync(scenario);
        var current = await scenario.ReadAsync();
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await using var context = scenario.Fixture.Database.Context();
        context.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        var recovered = await context.Set<ModerationRecognitionActionRecovery>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(recovered.VerifiedBindingId).IsEqualTo(loginId);
        await Assert.That(recovered.ResolvedAt).IsEqualTo(Now);
        await Assert.That(await context.ModerationSanctions.AnyAsync(row => row.SubjectId == scenario.FreshSubject)).IsFalse();
    }

    [Test]
    public async Task Native_recovery_does_not_substitute_another_login_when_the_pinned_binding_disappears()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var loginId = Guid.CreateVersion7();
        var substituteId = Guid.CreateVersion7();
        scenario.Keys.Available = false;
        await Assert.That((await MatchRecoveryAsync(scenario, loginId)).ActionRecoveryStaged).IsTrue();
        await using (var context = scenario.Fixture.Database.Context())
        {
            var login = await context.UserExternalLogins.SingleAsync(row => row.Id == loginId);
            context.UserExternalLogins.Remove(login);
            await context.SaveChangesAsync();
            context.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = substituteId, UserId = scenario.FreshSubject,
                User = await context.Users.SingleAsync(row => row.Id == scenario.FreshSubject),
                AuthenticationProviderId = (int)scenario.Account.ProviderKind,
                AuthenticationProvider = await context.Set<AuthenticationProvider>()
                    .SingleAsync(row => row.Id == (int)scenario.Account.ProviderKind),
                ProviderKey = scenario.Account.Value
            });
            await context.SaveChangesAsync();
        }
        scenario.Keys.Available = true;
        await RetryRecoveriesAsync(scenario);
        await using var verify = scenario.Fixture.Database.Context();
        verify.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        await Assert.That((await verify.Set<ModerationRecognitionActionRecovery>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync()).ResolvedAt).IsNull();
        await Assert.That(await verify.Set<ModerationRecognitionContinuityBinding>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
        await Assert.That(await verify.UserExternalLogins.AnyAsync(row => row.Id == substituteId)).IsTrue();
    }

    [Test]
    public async Task Native_recovery_settles_at_original_end_without_readable_keys()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var loginId = Guid.CreateVersion7();
        scenario.Keys.Available = false;
        await Assert.That((await MatchRecoveryAsync(scenario, loginId)).ActionRecoveryStaged).IsTrue();
        scenario.Fixture.Clock.UtcNow = Now.AddDays(2);
        await RetryRecoveriesAsync(scenario);
        await using var context = scenario.Fixture.Database.Context();
        context.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        var recovered = await context.Set<ModerationRecognitionActionRecovery>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(recovered.DispositionBy).IsEqualTo(Now.AddDays(2));
        await Assert.That(recovered.ResolvedAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(await context.Set<ModerationRecognitionContinuityBinding>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Native_recovery_rolls_back_references_and_resolution_when_ring_bound_expires_after_save()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        scenario.Keys.Available = false;
        await Assert.That((await MatchRecoveryAsync(scenario, Guid.CreateVersion7())).ActionRecoveryStaged).IsTrue();
        scenario.Keys.Available = true;
        var boundary = new RecoverySaveExpiry(scenario.Fixture.Clock);
        await RetryRecoveriesAsync(scenario, boundary);
        await Assert.That(boundary.ObservedNativeSave).IsTrue();
        await using var context = scenario.Fixture.Database.Context();
        var retained = await context.Set<ModerationRecognitionActionRecovery>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).SingleAsync();
        await Assert.That(retained.ResolvedAt).IsNull();
        await Assert.That(await context.Set<ModerationRecognitionContinuityBinding>()
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure).AnyAsync()).IsFalse();
        await Assert.That(await context.UserExternalLogins.AnyAsync(row => row.Id == retained.VerifiedBindingId)).IsTrue();
    }

    private static async Task RetryRecoveriesAsync(ContinuityScenario scenario, params IInterceptor[] interceptors)
    {
        await using var context = scenario.Fixture.Database.Context(interceptors);
        var repository = scenario.Repository(context);
        await scenario.Service(context).ExecuteWithRecognitionLockAsync(async token =>
        {
            var received = await repository.ReadActionRecoveryAfterAsync(null, 100, token);
            foreach (var recovery in received)
                await repository.RetryActionRecoveryAsync(recovery.Id, token);
            return true;
        }, CancellationToken.None);
    }

    private sealed class RecoverySaveExpiry(DeploymentClock clock) : SaveChangesInterceptor
    {
        public bool ObservedNativeSave { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!ObservedNativeSave && eventData.Context!.ChangeTracker
                .Entries<ModerationRecognitionContinuityBinding>().Any())
            {
                ObservedNativeSave = true;
                clock.UtcNow = clock.UtcNow.AddMinutes(5);
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task<ModerationRecognitionContinuityResult> MatchRecoveryAsync(
        ContinuityScenario scenario, Guid loginId)
    {
        await using var context = scenario.Fixture.Database.Context();
        var service = scenario.Service(context);
        return await service.ExecuteWithRecognitionLockAsync(ct =>
            scenario.Fixture.Retained.Repository.ExecuteSerializedAsync(async inner =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, inner);
                var user = await context.Users.SingleOrDefaultAsync(row => row.Id == scenario.FreshSubject, inner);
                if (user is null)
                {
                    user = new User { Id = scenario.FreshSubject, Pii = new UserPii
                    { UserId = scenario.FreshSubject, Email = $"{scenario.FreshSubject:N}@example.invalid",
                        FirstName = "Fresh", LastName = "Subject" } };
                    context.Users.Add(user);
                }
                if (!await context.UserExternalLogins.AnyAsync(row => row.Id == loginId, inner))
                    context.UserExternalLogins.Add(new UserExternalLogin
                    {
                        Id = loginId, UserId = scenario.FreshSubject, User = user,
                        AuthenticationProviderId = (int)scenario.Account.ProviderKind,
                        AuthenticationProvider = await context.Set<AuthenticationProvider>()
                            .SingleAsync(row => row.Id == (int)scenario.Account.ProviderKind, inner),
                        ProviderKey = scenario.Account.Value
                    });
                await context.SaveChangesAsync(inner);
                var result = await service.MatchAndBindAsync(
                    new(scenario.Account, scenario.FreshSubject, scenario.Fixture.Database.Scope.Id)
                    { VerifiedBindingId = loginId }, inner);
                if (result.AdmissionFailure == ModerationRecognitionContinuityFailure.None && !result.AdmissionExcluded
                    && (result.ActionFailure == ModerationRecognitionContinuityFailure.None || result.ActionRecoveryStaged))
                {
                    await context.SaveChangesAsync(inner);
                    await transaction.CommitAsync(inner);
                }
                return result;
            }, ct), CancellationToken.None);
    }
}
