using System.Data;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    [Arguments("surviving")]
    [Arguments("reversed")]
    [Arguments("expired")]
    public async Task ModerationErasureWorstBreak_ReconcilesAfterAllProviderInputsAreDestroyed(string correction)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using (var capture = fixture.Database.Context())
        {
            var repository = fixture.Repository(capture);
            var received = await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
            await Assert.That(received!.HasCompleteMaterial).IsFalse();
            await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        }
        // A fresh context must independently read every complete computed row before authority append.
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var before = fixture.Database.Context();
            await using var lease = await fixture.Repository(before).AcquireAppendAsync(fixture.Request, CancellationToken.None);
            await Assert.That(lease.CanAppend).IsTrue();
            var appended = await fixture.Retained.Repository.AppendAsync(fixture.Request, ct);
            await lease.BindAsync(appended, CancellationToken.None);
            await lease.CommitAsync(CancellationToken.None);
            return true;
        }, CancellationToken.None);
        // Simulate the committed purge and loss of every original input, not just a closed reader.
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var purge = fixture.Database.Context();
            await using var transaction = await purge.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await fixture.Repository(purge).VerifyPurgeAsync(fixture.Fact(), CancellationToken.None);
            await purge.UserExternalLogins.Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync();
            await purge.Set<UserPii>().Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync();
            var erased = await purge.Users.SingleAsync(row => row.Id == fixture.Subject);
            erased.IsDeleted = true;
            erased.DeletedAt = Now.UtcDateTime;
            await purge.SaveChangesAsync();
            if (correction == "reversed")
            {
                purge.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
                var sanction = await purge.ModerationSanctions.SingleAsync(row => row.Id == fixture.SanctionId);
                sanction.Revoke(sanction.Revision, Now.AddMinutes(1));
                await purge.SaveChangesAsync();
                fixture.Clock.UtcNow = Now.AddMinutes(1);
            }
            if (correction == "expired") fixture.Clock.UtcNow = Now.AddDays(2);
            await transaction.CommitAsync();
            return true;
        }, CancellationToken.None);
        // Recreate all scoped services after the crash with neither original inputs nor keys.
        fixture.Keys.Available = false;
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var restored = fixture.Database.Context();
            await using var replay = await restored.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await fixture.Repository(restored).VerifyPurgeAsync(fixture.Fact(), CancellationToken.None);
            var pending = await fixture.Repository(restored).FindAsync(fixture.Request.IntentId, CancellationToken.None);
            await Assert.That(pending!.HasCompleteMaterial).IsTrue();
            await Assert.That(pending.AuthoritySequence).IsEqualTo(1L);
            await Assert.That(pending.Material.Count(item => item.DroppedAt is null)).IsEqualTo(correction == "surviving" ? 1 : 0);
            await Assert.That(pending.Material.Single().EndsAt).IsEqualTo(Now.AddDays(2));
            await Assert.That(await restored.UserExternalLogins.AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
            await Assert.That(await restored.Set<UserPii>().AnyAsync(row => row.UserId == fixture.Subject)).IsFalse();
            return true;
        }, CancellationToken.None);
    }

    [Test]
    [Arguments("key")]
    [Arguments("missing-row")]
    [Arguments("changed-tag")]
    [Arguments("changed-generation")]
    public async Task ModerationErasureWorstBreak_IncompleteOrChangedOriginalMaterialCannotAppend(string defect)
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        if (defect == "key") fixture.Keys.Available = false;
        await repository.ComputeAsync(fixture.Request, CancellationToken.None);
        if (defect == "missing-row")
            await context.Set<ModerationRecognitionCaptureItem>().IgnoreAllFilters("Capture corruption witness")
                .Where(row => row.IntentId == fixture.Request.IntentId).ExecuteDeleteAsync();
        if (defect == "changed-tag") fixture.Keys.ReplaceMaterial();
        if (defect == "changed-generation") fixture.Keys.Generation++;
        await fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var lease = await repository.AcquireAppendAsync(fixture.Request, ct);
            await Assert.That(lease.CanAppend).IsFalse();
            await lease.CommitAsync(ct);
            return true;
        }, CancellationToken.None);
        await Assert.That((await repository.FindAsync(fixture.Request.IntentId, CancellationToken.None))!.AuthoritySequence).IsNull();
    }

    [Test]
    public async Task ModerationErasureWorstBreak_OrdinaryErasureNeedsNoRingOrCaptureRows()
    {
        await using var database = await Database.CreateAsync();
        var clock = new DeploymentClock();
        var keys = new RecognitionKeys(clock) { Available = false };
        await using var context = database.Context();
        await using var retained = await RecognitionAuthorityDatabase.CreateAsync(clock);
        using var replica = new ModerationRecognitionKeyLifecycleReplicaOwner();
        var repository = new ModerationRecognitionErasureRepository(context, keys, clock, retained.Repository, retained.Repository,
            new ModerationRecognitionKeyLifecycleRepository(context, retained.Repository, retained.Repository, replica, clock));
        var request = PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            database.AdminId, PrivacyErasureReasonCode.AccountDeletion, 1);
        await Assert.That(await repository.ReceiveAsync(request, RandomNumberGenerator.GetBytes(32), Now.AddHours(1), CancellationToken.None)).IsNull();
        await retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var lease = await repository.AcquireAppendAsync(request, ct);
            await Assert.That(lease.CanAppend).IsTrue();
            return true;
        }, CancellationToken.None);
        await Assert.That(await context.Set<ModerationRecognitionPending>().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task ModerationErasureWorstBreak_OperationConflictDoesNotReplaceOriginalDeadlineOrReceipt()
    {
        await using var fixture = await RecognitionErasureFixture.CreateAsync();
        await using var context = fixture.Database.Context();
        var repository = fixture.Repository(context);
        var original = await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
        var retry = await repository.ReceiveAsync(fixture.Request, RandomNumberGenerator.GetBytes(32), Now.AddDays(5), CancellationToken.None);
        await Assert.That(retry!.SettleBy).IsEqualTo(original!.SettleBy);
        await Assert.That(retry.ReceiptExpiresAt).IsEqualTo(original.ReceiptExpiresAt);
        var conflicting = PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
            fixture.Subject, PrivacyErasureReasonCode.AccountDeletion, 1);
        await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(
            () => repository.ReceiveAsync(conflicting, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None));
    }

    private sealed class RecognitionKeys(DeploymentClock clock)
        : IModerationRecognitionKeyRingProvider, IModerationRecognitionKeyMaterialSource
    {
        private byte[] material = RandomNumberGenerator.GetBytes(32);
        public byte[] CopyOriginalMaterial() => material.ToArray();
        public bool Available { get; set; } = true;
        public long Generation { get; set; } = 1;
        public void ReplaceMaterial() => material = RandomNumberGenerator.GetBytes(32);
        public Task<ModerationRecognitionKeyRing> ObserveAsync(Guid trust, CancellationToken token) => ResolveAsync(trust, token);
        public Task<ModerationRecognitionKeyRing> ResolveAsync(Guid trust, CancellationToken token) =>
            Task.FromResult(ModerationRecognitionKeyRing.Parse(Selection(trust), trust, clock));
        public ResolvedSecret Selection(Guid trust)
        {
            if (!Available) throw new InvalidOperationException("moderation_recognition_ring_unavailable");
            var json = JsonSerializer.Serialize(new
            {
                version = 1,
                trustDomainId = trust.ToString("D"),
                ringGeneration = Generation,
                issuedAt = clock.UtcNow.ToString("O"),
                validUntil = clock.UtcNow.AddMinutes(5).ToString("O"),
                keys = new[] { new { kid = "original", generation = Generation,
                    k = Convert.ToBase64String(material).TrimEnd('=').Replace('+', '-').Replace('/', '_'), status = "active" } }
            });
            return new ResolvedSecret(
                ModerationRecognitionKeyRing.SecretSettingKey, json, SecretSourceType.EnvironmentVariable,
                SecretScope.Instance, null, clock.UtcNow, "original-binding");
        }
    }

    private sealed class RecognitionErasureFixture : IAsyncDisposable
    {
        public required Database Database { get; init; }
        public required DeploymentClock Clock { get; init; }
        public required RecognitionKeys Keys { get; init; }
        public required Guid Subject { get; init; }
        public required Guid SanctionId { get; init; }
        public required Guid AdoptionId { get; init; }
        public required PrivacyErasureRequest Request { get; init; }
        public required RecognitionAuthorityDatabase Retained { get; init; }
        public required ModerationRecognitionKeyLifecycleFixture LifecycleFixture { get; init; }
        public required ISecretResolver SelectedSecrets { get; init; }
        public byte[] ReceiptHash { get; } = RandomNumberGenerator.GetBytes(32);
        public ModerationRecognitionErasureRepository Repository(ExploreDbContext context)
        {
            var composition = LifecycleFixture.Bind(context, Retained.Repository, Retained.Repository);
            return new(context, composition.Provider, Clock,
                Retained.Repository, Retained.Repository, composition.Repository);
        }
        public ModerationRecognitionKeyLifecycleRepository KeyLifecycle(ExploreDbContext context) =>
            LifecycleFixture.Bind(context, Retained.Repository, Retained.Repository).Repository;
        public PrivacyErasureIntent Fact() => PrivacyErasureIntent.Record(Request.IntentId, 1,
            PrivacyErasureSubjectKind.User, Subject, Request.ReasonCode, 1,
            Now.UtcDateTime, Now.UtcDateTime, Now.AddDays(30).UtcDateTime);
        public async ValueTask DisposeAsync()
        {
            LifecycleFixture.Dispose();
            await Retained.DisposeAsync();
            await Database.DisposeAsync();
        }
        public async Task WithdrawPrivacySourceAsync()
        {
            await using var context = Database.Context();
            var identity = DeploymentIdentity(context, Database.AdminId);
            var publications = new ModerationPolicyPublicationRepository(context,
                identity.Admin, identity.Machine, Database.Inventory, null, Clock);
            var writer = new ModerationQualificationRepository(context, identity.Admin, identity.Machine, publications,
                new ModerationNativeActivationQualification(context, Database.Inventory, null, null, Clock),
                Clock, Retained.Repository, deploymentInventory: Database.Inventory);
            var result = await writer.ExecuteAsync(new WithdrawModerationRecognitionPrivacyAdoption
            {
                OperationId = Guid.CreateVersion7(),
                Target = Database.Scope,
                ExpectedQualificationRevision = 4,
                AdoptionId = AdoptionId,
                ExpectedAdoptionRevision = 1
            });
            if (result.Outcome != ModerationQualificationWriteOutcome.Known)
                throw new InvalidOperationException("Native privacy adoption withdrawal failed.");
        }

        public static async Task<RecognitionErasureFixture> CreateAsync(TimeSpan? lawfulDeadline = null,
            TimeSpan? sanctionDuration = null, TimeSpan? reviewWithin = null,
            Func<AuthorityScope, AuthorityScope>? actionScope = null,
            ImmutableArray<ModerationCapability> capabilities = default)
        {
            var database = await Database.CreateAsync(physicalFile: true);
            var scope = actionScope?.Invoke(database.Scope) ?? database.Scope;
            var selectedCapabilities = capabilities.IsDefault ? [ModerationCapability.Publish] : capabilities;
            var reference = ModerationReferenceCatalogue.Profiles.Values
                .Where(profile => profile.ScopeKinds.Contains(scope.Kind)
                    && profile.SubjectKinds.Contains(ModerationSubjectKind.Human)
                    && selectedCapabilities.All(capability => profile.Effects.Any(effect => effect.Capability == capability)))
                .OrderBy(profile => profile.Id).First();
            var clock = new DeploymentClock();
            var retained = await RecognitionAuthorityDatabase.CreateAsync(clock);
            await using var context = database.Context();
            var request = database.Request(capabilities.IsDefault && actionScope is null
                ? ModerationReferenceProfileId.CFG05 : reference.Id);
            request = request with
            {
                Proposed = request.Proposed with
                {
                    RequestedState = ModerationPolicyRequestedState.Active,
                    Profiles = [request.Proposed.Profiles.Single() with
                { Recognition = ModerationReferenceRecognition.SeparatelyQualifiedFinalAction, ScopeKinds = [scope.Kind] }]
                }
            };
            var (admin, machine) = DeploymentIdentity(context, database.AdminId);
            var publications = new ModerationPolicyPublicationRepository(context, admin, machine, database.Inventory, null, clock);
            var prepared = await publications.PrepareAsync(request, Now.AddDays(10));
            if (prepared.Outcome != ModerationPolicyPublicationOutcome.Known)
                throw new InvalidOperationException("Native recognition policy preparation failed.");
            var privacy = Guid.CreateVersion7();
            var recovery = Guid.CreateVersion7();
            var subject = Guid.CreateVersion7();
            var role = await context.Roles.SingleAsync(row =>
                row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin");
            foreach (var id in new[] { privacy, recovery, subject })
            {
                var user = new User
                {
                    Id = id,
                    Pii = new UserPii
                    { UserId = id, Email = $"{id:N}@example.invalid", FirstName = "Capture", LastName = "Witness" }
                };
                context.Users.Add(user);
                if (id != subject) context.PlatformUserRoles.Add(new PlatformUserRole
                { Id = Guid.CreateVersion7(), UserId = id, User = user, RoleId = role.Id, Role = role });
            }
            context.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = subject,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = $"https://issuer.example.invalid|{Guid.CreateVersion7():D}"
            });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            ModerationQualificationRepository Writer(Guid human)
            {
                var identity = DeploymentIdentity(context, human);
                return new(context, identity.Admin, identity.Machine, publications,
                    new ModerationNativeActivationQualification(context, database.Inventory, null, null, clock),
                    clock, retained.Repository, deploymentInventory: database.Inventory);
            }
            var source = new RecordModerationRecognitionPrivacySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 2,
                SourceId = Guid.CreateVersion7(),
                PolicyId = request.Proposed.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = request.Proposed.PublicationId,
                ProfileId = request.Proposed.Profiles.Single().Id,
                Mode = ModerationNativeMode.Embedded,
                ValidUntil = Now.AddDays(8),
                Terms = lawfulDeadline is { } deadline
                    ? RecognitionPrivacyTerms() with { LawfulRequestDeadline = deadline, EscalationAfter = TimeSpan.FromMinutes(5) }
                    : RecognitionPrivacyTerms(),
                Ownership = new(privacy, privacy, recovery, recovery, database.AdminId,
                    "Independently reviewed distinct canonical controllers.",
                    "Appointed privacy owner of purpose-limited recognition and lawful erasure.",
                    "Appointed accountable recovery owner for bounded catastrophic disposition."),
                Retention = [
                    new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                    new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
                        ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero)]
            };
            if ((await Writer(database.AdminId).ExecuteAsync(source)).Outcome != ModerationQualificationWriteOutcome.Known)
                throw new InvalidOperationException("Native recognition privacy source failed.");
            var adoptionId = Guid.CreateVersion7();
            if ((await Writer(privacy).ExecuteAsync(new AdoptModerationRecognitionPrivacySource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 3,
                SourceId = source.SourceId,
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = 3,
                AdoptionId = adoptionId,
                ValidUntil = Now.AddDays(7)
            })).Outcome != ModerationQualificationWriteOutcome.Known)
                throw new InvalidOperationException("Native recognition privacy adoption failed.");
            var instance = database.Scope.Parent!;
            var subjectReference = SubjectReference.Create(ModerationSubjectKind.Human, subject, instance);
            var moderationCase = ModerationCase.Open(Guid.CreateVersion7(), subjectReference,
                scope, Guid.CreateVersion7(), Now);
            var rule = request.Proposed.Profiles.Single().Rule;
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), moderationCase, moderationCase.Revision,
                moderationCase.Scope, null, null,
                rule, rule, rule, rule, SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, instance),
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), moderationCase.Id, moderationCase.Revision, Now),
                "Native canonical finding fixture for the erasure boundary.", Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), moderationCase.Id,
                subjectReference,
                SubjectReference.Create(ModerationSubjectKind.Human, database.AdminId, instance),
                instance, scope, request.Proposed.Profiles.Single().Rule,
                selectedCapabilities, Now, Now.Add(sanctionDuration ?? TimeSpan.FromDays(2)),
                reviewWithin is { } review ? Now.Add(review) : null, 1, Now);
            decision.RecordResponse(1, Now);
            decision.AcceptFinding(finding.Id, 2, Now);
            decision.Approve(SubjectReference.Create(ModerationSubjectKind.Human, privacy, instance), instance, 3, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), 4, Now);
            context.ModerationCases.Add(moderationCase);
            context.ModerationFindings.Add(finding);
            context.ModerationDecisions.Add(decision);
            context.ModerationSanctions.Add(sanction);
            await context.SaveChangesAsync();
            var keys = new RecognitionKeys(clock);
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(ModerationRecognitionKeyRing.SecretSettingKey, null, Arg.Any<CancellationToken>())
                .Returns(_ => keys.Available
                    ? SecretResolutionResult.Resolved(keys.Selection(database.Scope.TrustDomainId))
                    : SecretResolutionResult.Unavailable);
            var lifecycleFixture = new ModerationRecognitionKeyLifecycleFixture(secrets, clock);
            await lifecycleFixture.Bind(context, retained.Repository, retained.Repository).Lifecycle
                .InitializeAsync(database.Scope.TrustDomainId, CancellationToken.None);
            return new()
            {
                Database = database,
                Clock = clock,
                Keys = keys,
                Subject = subject,
                SanctionId = sanction.Id,
                AdoptionId = adoptionId,
                Retained = retained,
                LifecycleFixture = lifecycleFixture,
                SelectedSecrets = secrets,
                Request = PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                    subject, PrivacyErasureReasonCode.AccountDeletion, 1)
            };
        }
    }

}
