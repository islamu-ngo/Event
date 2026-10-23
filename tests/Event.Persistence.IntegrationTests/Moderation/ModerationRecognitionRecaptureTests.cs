using System.Data;
using System.Security.Cryptography;
using Explore.Application.Authentication;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

// Authored for final programme assurance; no test build or execution during implementation.
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Recapture_chained_native_aliases_preserve_original_authority_after_all_input_destruction()
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var firstOwner = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(firstOwner, scenario.Continuity.Account);
        var alias = scenario.Alias();
        await scenario.EnrollAsync(firstOwner, alias, provisioning: false);
        await scenario.Continuity.RotateAsync();
        scenario.Clock.UtcNow = Now.AddMinutes(1);
        var firstRequest = scenario.Request(firstOwner);
        var firstFact = await scenario.EraseAsync(firstRequest);
        var first = await scenario.PendingAsync(firstRequest.IntentId);
        await Assert.That(first.Material.Count).IsEqualTo(2);
        await Assert.That(first.Material.All(item => item.SubjectId == scenario.OriginalSubject
            && item.ErasureSubjectId == firstOwner && item.KeyGeneration == 2)).IsTrue();

        var secondOwner = Guid.CreateVersion7();
        await scenario.EnrollAsync(secondOwner, alias);
        var secondAlias = scenario.Alias();
        await scenario.EnrollAsync(secondOwner, secondAlias, provisioning: false);
        scenario.Clock.UtcNow = Now.AddMinutes(2);
        var secondRequest = scenario.Request(secondOwner);
        var secondFact = await scenario.EraseAsync(secondRequest);
        var second = await scenario.PendingAsync(secondRequest.IntentId);
        await Assert.That(second.Material.Count).IsEqualTo(2);
        await Assert.That(second.Material.All(item => item.OriginalCapturedAt == Now
            && item.RetainUntil == first.Material[0].RetainUntil
            && item.FindingId == first.Material[0].FindingId
            && item.SanctionId == scenario.OriginalSanction
            && item.SanctionRevision == first.Material[0].SanctionRevision
            && item.DecisionRevision == first.Material[0].DecisionRevision
            && item.SourceId == first.Material[0].SourceId && item.AdoptionId == first.Material[0].AdoptionId
            && first.Material.Any(parent => parent.Id == item.ParentCaptureItemId))).IsTrue();

        // Recreate readers with no keys after both privacy owners and every original login
        // have been destroyed. Exact retained facts, not local receipts alone, authorize replay.
        scenario.Continuity.Keys.Available = false;
        await scenario.ReplayAsync(firstFact);
        await scenario.ReplayAsync(secondFact);
        scenario.Continuity.Keys.Available = true;
        var thirdOwner = Guid.CreateVersion7();
        await scenario.EnrollAsync(thirdOwner, secondAlias);
        var current = await scenario.ReadAsync(thirdOwner);
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await Assert.That(current.Actions.Single().SanctionId).IsEqualTo(scenario.OriginalSanction);
        await Assert.That(current.Actions.Single().EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(current.Actions.Single().ReviewAt).IsEqualTo(Now.AddHours(1));
        await using var observer = scenario.Database.Context();
        observer.ModerationTrustDomainId = scenario.Database.Scope.TrustDomainId;
        var sanction = await observer.ModerationSanctions.SingleAsync(row => row.Id == scenario.OriginalSanction);
        await Assert.That(sanction.SubjectId).IsEqualTo(scenario.OriginalSubject);
        await Assert.That(sanction.Revision).IsEqualTo(1L);
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row =>
            row.UserId == scenario.OriginalSubject || row.UserId == firstOwner || row.UserId == secondOwner)).IsFalse();
        await Assert.That(await observer.Set<UserPii>().AnyAsync(row =>
            row.UserId == scenario.OriginalSubject || row.UserId == firstOwner || row.UserId == secondOwner)).IsFalse();
        await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.UserId == thirdOwner)).IsFalse();
    }

    [Test]
    [Arguments("reversal")]
    [Arguments("expiry")]
    [Arguments("parent-drop")]
    public async Task Recapture_revalidation_drops_only_affected_bound_contribution_and_keeps_direct_authority(string correction)
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(fresh, scenario.Continuity.Account);
        await scenario.EnrollAsync(fresh, scenario.Alias(), provisioning: false);
        var directId = await scenario.AddDirectSanctionAsync(fresh);
        var request = scenario.Request(fresh);
        var fact = await scenario.EraseAsync(request);
        var before = await scenario.PendingAsync(request.IntentId);
        await Assert.That(before.Material.Count).IsEqualTo(4);
        await scenario.CorrectOriginalAsync(correction);
        scenario.Continuity.Keys.Available = false;
        await scenario.ReplayAsync(fact);
        var after = await scenario.PendingAsync(request.IntentId);
        await Assert.That(after.HasCompleteMaterial).IsTrue();
        await Assert.That(after.MaterialReceipt).IsEqualTo(before.MaterialReceipt);
        await Assert.That(after.Material.Where(row => row.SanctionId == scenario.OriginalSanction)
            .All(row => row.DroppedAt is not null)).IsTrue();
        await Assert.That(after.Material.Where(row => row.SanctionId == directId)
            .All(row => row.DroppedAt is null && row.SubjectId == fresh && row.ParentCaptureItemId is null)).IsTrue();
        await Assert.That(after.Material.Count(row => row.DroppedAt is null)).IsEqualTo(2);
    }

    [Test]
    [Arguments("reversal")]
    [Arguments("parent-drop")]
    public async Task Recapture_correction_before_append_cannot_leave_alias_material_effective(string correction)
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(fresh, scenario.Continuity.Account);
        await scenario.EnrollAsync(fresh, scenario.Alias(), provisioning: false);
        var request = scenario.Request(fresh);
        await scenario.ComputeAsync(request);
        var before = await scenario.PendingAsync(request.IntentId);
        await Assert.That(before.HasCompleteMaterial).IsTrue();
        await scenario.CorrectOriginalAsync(correction);
        var fact = await scenario.EraseAsync(request);
        var after = await scenario.PendingAsync(request.IntentId);
        await Assert.That(after.AuthoritySequence).IsEqualTo(fact.AuthoritySequence);
        await Assert.That(after.MaterialReceipt).IsEqualTo(before.MaterialReceipt);
        await Assert.That(after.Material.All(row => row.DroppedAt is not null)).IsTrue();
    }

    [Test]
    [Arguments("receipt")]
    [Arguments("binding")]
    public async Task Recapture_stale_parent_restore_cannot_authorize_purge_or_matched_continuity(string defect)
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(fresh, scenario.Continuity.Account);
        var alias = scenario.Alias();
        await scenario.EnrollAsync(fresh, alias, provisioning: false);
        var request = scenario.Request(fresh);
        var fact = await scenario.EraseAsync(request);
        var child = (await scenario.PendingAsync(request.IntentId)).Material[0];
        var next = Guid.CreateVersion7();
        await scenario.EnrollAsync(next, alias);
        await Assert.That((await scenario.ReadAsync(next)).Actions.Length).IsEqualTo(1);
        await using (var stale = scenario.Database.Context())
        {
            if (defect == "receipt")
                await stale.Set<ModerationRecognitionPending>().IgnoreAllFilters("Recapture restored parent witness")
                    .Where(row => row.IntentId == scenario.Continuity.Fixture.Request.IntentId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.MaterialCount, row => row.MaterialCount + 1));
            else
                await stale.Set<ModerationRecognitionContinuityBinding>().IgnoreAllFilters("Recapture restored binding witness")
                    .Where(row => row.Id == child.ContinuityBindingId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.FindingId, Guid.CreateVersion7()));
        }
        scenario.Continuity.Keys.Available = false;
        await Assert.That(() => scenario.ReplayAsync(fact)).Throws<InvalidOperationException>();
        var current = await scenario.ReadAsync(next);
        await Assert.That(current.ActionFailure).IsEqualTo(defect == "receipt"
            ? ModerationRecognitionContinuityFailure.SettlementRequired : ModerationRecognitionContinuityFailure.CanonicalUnavailable);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
        await using var observer = scenario.Database.Context();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.UserId == fresh)).IsFalse();
        await Assert.That(await observer.Set<UserPii>().AnyAsync(row => row.UserId == fresh)).IsFalse();
    }

    [Test]
    public async Task Recapture_native_retry_keeps_one_immutable_receipt_and_exact_original_retention()
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(fresh, scenario.Continuity.Account);
        await scenario.EnrollAsync(fresh, scenario.Alias(), provisioning: false);
        var request = scenario.Request(fresh);
        await scenario.ComputeAsync(request);
        var first = await scenario.PendingAsync(request.IntentId);
        scenario.Clock.UtcNow = Now.AddMinutes(1);
        await scenario.ComputeAsync(request);
        var retry = await scenario.PendingAsync(request.IntentId);
        await Assert.That(retry.MaterialReceipt).IsEqualTo(first.MaterialReceipt);
        await Assert.That(retry.Material.Select(row => row.Id).ToArray()).IsEquivalentTo(first.Material.Select(row => row.Id).ToArray());
        await Assert.That(retry.Material.Count).IsEqualTo(2);
        await Assert.That(retry.Material.All(row => row.RetainUntil == Now.AddDays(365))).IsTrue();
    }

    [Test]
    public async Task Recapture_unmatched_operational_recovery_is_not_a_contribution_or_capture_authorization()
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        scenario.Continuity.Keys.Available = false;
        await scenario.EnrollAsync(fresh, scenario.Alias());
        await using (var observer = scenario.Database.Context())
        {
            await Assert.That(await observer.Set<ModerationRecognitionActionRecovery>()
                .IgnoreAllFilters("Operational uncertainty is not matched continuity")
                .AnyAsync(row => row.FreshSubjectId == fresh)).IsTrue();
            await Assert.That(await observer.Set<ModerationRecognitionContinuityBinding>()
                .IgnoreAllFilters("Operational uncertainty is not matched continuity")
                .AnyAsync(row => row.FreshSubjectId == fresh)).IsFalse();
        }
        var request = scenario.Request(fresh);
        await scenario.EraseAsync(request);
        await using var verification = scenario.Database.Context();
        await Assert.That(await scenario.Continuity.Repository(verification)
            .FindAsync(request.IntentId, CancellationToken.None)).IsNull();
        await Assert.That(await verification.Set<ModerationRecognitionCaptureItem>()
            .IgnoreAllFilters("No accusation from an unmatched operational recovery")
            .AnyAsync(row => row.ErasureSubjectId == fresh)).IsFalse();
    }

    [Test]
    public async Task Recapture_ancestor_drop_invalidates_the_new_alias_terminal_read_without_keys()
    {
        await using var scenario = await RecaptureScenario.CreateAsync();
        var fresh = scenario.Continuity.FreshSubject;
        await scenario.EnrollAsync(fresh, scenario.Continuity.Account);
        var alias = scenario.Alias();
        await scenario.EnrollAsync(fresh, alias, provisioning: false);
        await scenario.EraseAsync(scenario.Request(fresh));
        var next = Guid.CreateVersion7();
        await scenario.EnrollAsync(next, alias);
        await Assert.That((await scenario.ReadAsync(next)).Actions.Length).IsEqualTo(1);
        await scenario.CorrectOriginalAsync("parent-drop");
        scenario.Continuity.Keys.Available = false;
        var current = await scenario.ReadAsync(next);
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(0);
    }

    private sealed class RecaptureScenario : IAsyncDisposable
    {
        public required ContinuityScenario Continuity { get; init; }
        public Database Database => Continuity.Fixture.Database;
        public DeploymentClock Clock => Continuity.Fixture.Clock;
        public Guid OriginalSubject => Continuity.Fixture.Subject;
        public Guid OriginalSanction => Continuity.Fixture.SanctionId;
        public ValueTask DisposeAsync() => Continuity.DisposeAsync();
        public ProviderAccountKey Alias() => new(AuthenticationProviderKind.Keycloak,
            $"https://verified-alias.example.invalid|{Guid.CreateVersion7():D}");
        public PrivacyErasureRequest Request(Guid subject) => PrivacyErasureRequest.Create(Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User, subject, PrivacyErasureReasonCode.AccountDeletion, 1);
        public static async Task<RecaptureScenario> CreateAsync() =>
            new() { Continuity = await ContinuityScenario.CreateAsync(reviewWithin: TimeSpan.FromHours(1)) };
        private ModerationRecognitionErasure Capture(ExploreDbContext context) =>
            new(Continuity.Repository(context), Continuity.Fixture.Retained.Repository, Continuity.Fixture.Retained.Repository,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

        public async Task EnrollAsync(Guid subject, ProviderAccountKey account, bool provisioning = true)
        {
            await using var context = Database.Context();
            var fence = new PrivacyIdentityFenceOperation(Continuity.Fixture.Retained.Repository,
                Continuity.Service(context), Clock, new UserExternalLoginRepository(context));
            var login = Guid.CreateVersion7();
            await fence.ExecuteEnrollmentAsync(account, token =>
                new EfCoreUnitOfWork(context).ExecuteRecognitionEnrollmentAsync(async ct =>
                {
                    if (!provisioning) await fence.EnsureSubjectMayEnrollAsync(subject, ct);
                    // The subject may already exist, but this is still a newly verified
                    // provider binding, not a sign-in through an already persisted login.
                    await fence.MatchAndBindAsync(account, subject, login, Database.Scope.Id, replacementProvisioning: true, ct);
                    if (provisioning) context.Users.Add(new User
                    {
                        Id = subject, Pii = new UserPii { UserId = subject, FirstName = "Fresh", LastName = "Alias", Email = string.Empty }
                    });
                    context.UserExternalLogins.Add(new UserExternalLogin
                    {
                        Id = login, UserId = subject, User = null!,
                        AuthenticationProviderId = (int)account.ProviderKind, AuthenticationProvider = null!, ProviderKey = account.Value
                    });
                    await context.SaveChangesAsync(ct);
                    return true;
                }, fence.EnsureCurrentAsync, token), CancellationToken.None);
        }

        public async Task ComputeAsync(PrivacyErasureRequest request)
        {
            await using var context = Database.Context();
            var repository = Continuity.Repository(context);
            await Continuity.Service(context).ExecuteWithRecognitionLockAsync(async ct =>
            {
                await repository.ReceiveAsync(request, RandomNumberGenerator.GetBytes(32), Clock.UtcNow.AddHours(2), ct);
                await repository.ComputeAsync(request, ct);
                return true;
            }, CancellationToken.None);
        }

        public async Task<PrivacyErasureIntent> EraseAsync(PrivacyErasureRequest request)
        {
            await using var context = Database.Context();
            var capture = Capture(context);
            var fact = await capture.AppendAsync(request, RandomNumberGenerator.GetBytes(32),
                Clock.UtcNow.AddHours(2), CancellationToken.None)
                ?? throw new InvalidOperationException("Native recapture did not settle.");
            await capture.ExecutePurgeAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await Continuity.Repository(context).VerifyPurgeAsync(fact, ct);
                await context.UserExternalLogins.Where(row => row.UserId == request.SubjectId).ExecuteDeleteAsync(ct);
                await context.Set<UserPii>().Where(row => row.UserId == request.SubjectId).ExecuteDeleteAsync(ct);
                var erased = await context.Users.SingleAsync(row => row.Id == request.SubjectId, ct);
                erased.IsDeleted = true;
                erased.DeletedAt = Clock.UtcNow.UtcDateTime;
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }, CancellationToken.None);
            return fact;
        }

        public async Task ReplayAsync(PrivacyErasureIntent fact)
        {
            await using var context = Database.Context();
            await Capture(context).ExecutePurgeAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await Continuity.Repository(context).VerifyPurgeAsync(fact, ct);
                await transaction.CommitAsync(ct);
                return true;
            }, CancellationToken.None);
        }

        public async Task<ModerationRecognitionPending> PendingAsync(Guid intent)
        {
            await using var context = Database.Context();
            return await Continuity.Repository(context).FindAsync(intent, CancellationToken.None)
                ?? throw new InvalidOperationException("Missing native computed recapture.");
        }

        public async Task<Explore.Application.Contracts.Moderation.ModerationRecognitionContinuityResult> ReadAsync(Guid subject)
        {
            await using var context = Database.Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            return await Continuity.Service(context).ReadCurrentInTransactionAsync(subject, CancellationToken.None);
        }

        public async Task CorrectOriginalAsync(string correction)
        {
            Clock.UtcNow = correction == "expiry" ? Now.AddDays(2) : Now.AddMinutes(3);
            if (correction == "expiry") return;
            await using var context = Database.Context();
            await Capture(context).ExecutePurgeAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                context.ModerationTrustDomainId = Database.Scope.TrustDomainId;
                if (correction == "reversal")
                {
                    var sanction = await context.ModerationSanctions.SingleAsync(row => row.Id == OriginalSanction, ct);
                    sanction.Revoke(sanction.Revision, Clock.UtcNow);
                }
                else
                {
                    var root = await context.Set<ModerationRecognitionCaptureItem>().IgnoreAllFilters("Exact ancestor drop witness")
                        .SingleAsync(row => row.IntentId == Continuity.Fixture.Request.IntentId, ct);
                    root.Drop(Clock.UtcNow);
                }
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }, CancellationToken.None);
        }

        public async Task<Guid> AddDirectSanctionAsync(Guid subjectId)
        {
            await using var context = Database.Context();
            context.ModerationTrustDomainId = Database.Scope.TrustDomainId;
            var original = await context.ModerationDecisions.SingleAsync(row => row.SubjectId == OriginalSubject);
            var subject = SubjectReference.Create(ModerationSubjectKind.Human, subjectId, original.Subject.Origin);
            var actor = SubjectReference.Create(ModerationSubjectKind.Human, Database.AdminId, original.Subject.Origin);
            var moderationCase = ModerationCase.Open(Guid.CreateVersion7(), subject, original.Scope, Guid.CreateVersion7(), Now);
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), moderationCase, moderationCase.Revision,
                moderationCase.Scope, null, null,
                original.Rule, original.Rule, original.Rule, original.Rule, actor,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), moderationCase.Id, moderationCase.Revision, Now),
                "Independent direct contribution to distinguish it from inherited recognition.", Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), moderationCase.Id, subject, actor,
                original.Subject.Origin, original.Scope, original.Rule, [ModerationCapability.Publish],
                Now, Now.AddDays(4), null, 1, Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(finding.Id, 2, Now);
            var source = await context.Set<ModerationRecognitionPrivacySource>()
                .IgnoreAllFilters("Independent direct canonical contribution fixture").SingleAsync();
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, source.PrivacyOwnerHumanId,
                original.Subject.Origin), original.Subject.Origin, 3, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
            context.ModerationCases.Add(moderationCase);
            context.ModerationFindings.Add(finding);
            context.ModerationDecisions.Add(decision);
            context.ModerationSanctions.Add(sanction);
            await context.SaveChangesAsync();
            return sanction.Id;
        }
    }
}
