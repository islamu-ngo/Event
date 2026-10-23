using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Application.Settings.Groups;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Enrollment_reuses_held_groups_and_restores_outer_ownership_after_inner_commit()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var settings = new RelationalSettingMutationLock(context, unit);
        var visitor = VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray();
        string[] recognition = [ModerationRecognitionKeyRing.SecretSettingKey];
        await settings.ExecuteOrderedGroupsAsync([visitor], async outer =>
        {
            await settings.ExecuteOrderedGroupsAsync([visitor, recognition], token =>
                unit.ExecuteInTransactionAsync(ct => settings.ExecuteOrderedGroupsAsync(
                    [visitor, recognition], async inner =>
                    {
                        context.Users.Add(new User
                        {
                            Id = scenario.FreshSubject,
                            Pii = new UserPii { FirstName = "Fresh", LastName = "Identity", Email = string.Empty }
                        });
                        await context.SaveChangesAsync(inner);
                        return true;
                    }, ct), token), outer);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                unit.ExecuteInTransactionAsync(ct =>
                    settings.ExecuteOrderedGroupsAsync([recognition], _ => Task.FromResult(true), ct), outer));
            return true;
        }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await using var observer = scenario.Fixture.Database.Context();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == scenario.FreshSubject)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Enrollment_preserves_preexisting_visitor_or_SMTP_setting_ownership(bool smtp)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var unit = new EfCoreUnitOfWork(context);
        var settings = new RelationalSettingMutationLock(context, unit);
        var continuity = new ModerationRecognitionContinuity(scenario.Repository(context), settings);
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            continuity, scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        string[] keys = smtp ? EmailSettingGroup.SettingKeys.ToArray() : VisitorAccessCapabilityResolver.AuthoritySettingKeys.ToArray();

        bool committed = await settings.ExecuteOrderedGroupsAsync([keys], outer =>
            fence.ExecuteIdentityMutationAsync(token => unit.ExecuteRecognitionEnrollmentAsync(ct =>
                settings.ExecuteManyAsync(keys, async inner =>
                {
                    await fence.EnsureSubjectMayEnrollAsync(scenario.FreshSubject, inner);
                    context.Users.Add(new User
                    {
                        Id = scenario.FreshSubject,
                        Pii = new UserPii { FirstName = "Fresh", LastName = "Identity", Email = string.Empty }
                    });
                    return true;
                }, ct), fence.EnsureCurrentAsync, token), outer), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(committed).IsTrue();
        await using var observer = scenario.Fixture.Database.Context();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == scenario.FreshSubject)).IsTrue();
    }

    [Test]
    public async Task Enrollment_commits_exact_action_continuity_without_restoring_old_identity()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        Guid loginId = Guid.CreateVersion7();

        await fence.ExecuteEnrollmentAsync(scenario.Account, token =>
            new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async ct =>
            {
                await fence.MatchAndBindAsync(scenario.Account, scenario.FreshSubject, loginId,
                    scenario.Fixture.Database.Scope.Id, replacementProvisioning: true, ct);
                context.Users.Add(new User
                {
                    Id = scenario.FreshSubject,
                    Pii = new UserPii { FirstName = "Fresh", LastName = "Identity", Email = string.Empty }
                });
                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = loginId,
                    UserId = scenario.FreshSubject,
                    User = null!,
                    AuthenticationProviderId = (int)scenario.Account.ProviderKind,
                    AuthenticationProvider = null!,
                    ProviderKey = scenario.Account.Value
                });
                await context.SaveChangesAsync(ct);
                return true;
            }, fence.EnsureCurrentAsync, token), CancellationToken.None);

        var current = await scenario.ReadAsync();
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await Assert.That(current.Actions.Single().FreshSubjectId).IsEqualTo(scenario.FreshSubject);
        await using var observer = scenario.Fixture.Database.Context();
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.UserId == scenario.FreshSubject)).IsFalse();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.UserId == scenario.Fixture.Subject)).IsFalse();
    }

    [Test]
    public async Task Local_nested_enrollment_reuses_outer_native_authority_before_transaction()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        var account = new ProviderAccountKey(AuthenticationProviderKind.Local, scenario.FreshSubject.ToString("D"));

        bool completed = await fence.ExecuteIdentityMutationAsync(token =>
            new EfCoreUnitOfWork(context).ExecuteSerializableAsync(ct =>
                fence.ExecuteEnrollmentAsync(account, async inner =>
                {
                    await fence.EnsureSubjectMayEnrollAsync(scenario.FreshSubject, inner);
                    return true;
                }, ct), token), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(completed).IsTrue();
        await Assert.That((await scenario.ReadAsync()).Actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Local_unsettled_privacy_request_prevents_native_identity_mutation()
    {
        await using var scenario = await ContinuityScenario.CreateAsync(settle: false);
        await using var context = scenario.Fixture.Database.Context();
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        bool changed = false;

        await Assert.That(() => fence.ExecuteIdentityMutationAsync(token =>
            new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async ct =>
            {
                await fence.EnsureSubjectMayEnrollAsync(scenario.Fixture.Subject, ct);
                changed = true;
                return true;
            }, token), CancellationToken.None)).Throws<InvalidOperationException>();

        await Assert.That(changed).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Enrollment_rolls_back_when_commit_loses_lease_or_exact_binding(bool changeBinding)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        Guid loginId = Guid.CreateVersion7();

        await Assert.That(() => fence.ExecuteEnrollmentAsync(scenario.Account, token =>
            new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async ct =>
            {
                await fence.MatchAndBindAsync(scenario.Account, scenario.FreshSubject, loginId,
                    scenario.Fixture.Database.Scope.Id, replacementProvisioning: true, ct);
                context.Users.Add(new User
                {
                    Id = scenario.FreshSubject,
                    Pii = new UserPii { FirstName = "Fresh", LastName = "Identity", Email = string.Empty }
                });
                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = changeBinding ? Guid.CreateVersion7() : loginId,
                    UserId = scenario.FreshSubject,
                    User = null!,
                    AuthenticationProviderId = (int)scenario.Account.ProviderKind,
                    AuthenticationProvider = null!,
                    ProviderKey = scenario.Account.Value
                });
                if (!changeBinding) scenario.Fixture.Clock.UtcNow = Now.AddMinutes(6);
                return true;
            }, fence.EnsureCurrentAsync, token), CancellationToken.None)).Throws<InvalidOperationException>();

        await using var observer = scenario.Fixture.Database.Context();
        await Assert.That(await observer.Users.AnyAsync(row => row.Id == scenario.FreshSubject)).IsFalse();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.UserId == scenario.FreshSubject)).IsFalse();
        await Assert.That((await scenario.ReadAsync()).Actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Enrollment_with_durable_action_uncertainty_keeps_identity_distinct_from_protected_capability()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        scenario.Keys.Available = false;
        await using var context = scenario.Fixture.Database.Context();
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));
        Guid loginId = Guid.CreateVersion7();

        await fence.ExecuteEnrollmentAsync(scenario.Account, token =>
            new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async ct =>
            {
                await fence.MatchAndBindAsync(scenario.Account, scenario.FreshSubject, loginId,
                    scenario.Fixture.Database.Scope.Id, replacementProvisioning: true, ct);
                context.Users.Add(new User
                {
                    Id = scenario.FreshSubject,
                    Pii = new UserPii { FirstName = "Fresh", LastName = "Identity", Email = string.Empty }
                });
                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = loginId,
                    UserId = scenario.FreshSubject,
                    User = null!,
                    AuthenticationProviderId = (int)scenario.Account.ProviderKind,
                    AuthenticationProvider = null!,
                    ProviderKey = scenario.Account.Value
                });
                return true;
            }, fence.EnsureCurrentAsync, token), CancellationToken.None);

        await using var observer = scenario.Fixture.Database.Context();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(
            row => row.Id == loginId && row.UserId == scenario.FreshSubject)).IsTrue();
        var protectedContinuity = await scenario.ReadAsync();
        await Assert.That(protectedContinuity.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.ActionRecoveryRequired);
        await Assert.That(protectedContinuity.AdmissionExcluded).IsFalse();
        await Assert.That(protectedContinuity.Actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Exact_existing_sign_in_does_not_reenter_replacement_provisioning_when_keys_are_unavailable()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        await using var context = scenario.Fixture.Database.Context();
        Guid loginId = Guid.CreateVersion7();
        context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = loginId,
            UserId = scenario.FreshSubject,
            User = null!,
            AuthenticationProviderId = (int)scenario.Account.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = scenario.Account.Value
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        scenario.Keys.Available = false;
        var fence = new PrivacyIdentityFenceOperation(scenario.Fixture.Retained.Repository,
            scenario.Service(context), scenario.Fixture.Clock, new UserExternalLoginRepository(context));

        bool signedIn = await fence.ExecuteEnrollmentAsync(scenario.Account, token =>
            new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async ct =>
            {
                await fence.MatchAndBindAsync(scenario.Account, scenario.FreshSubject, loginId,
                    scenario.Fixture.Database.Scope.Id, replacementProvisioning: false, ct);
                return true;
            }, fence.EnsureCurrentAsync, token), CancellationToken.None);

        await Assert.That(signedIn).IsTrue();
        var current = await scenario.ReadAsync();
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await Assert.That(current.Actions.Single().FreshSubjectId).IsEqualTo(scenario.FreshSubject);
        await Assert.That(current.Actions.Single().EndsAt).IsEqualTo(Now.AddDays(2));
    }
}
