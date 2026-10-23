using System.Data;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

// Source scenarios only. Programme assurance owns compilation and execution.
public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task Continuity_survives_destroyed_inputs_without_reassigning_original_sanction()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var matched = await scenario.MatchAsync();
        await Assert.That(matched.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(matched.AdmissionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(matched.AdmissionExcluded).IsFalse();
        await Assert.That(matched.Actions.Length).IsEqualTo(1);
        await Assert.That(matched.Actions[0].FreshSubjectId).IsEqualTo(scenario.FreshSubject);
        await Assert.That(matched.Actions[0].Capabilities.ToArray()).IsEquivalentTo(new[] { ModerationCapability.Publish });
        scenario.Keys.Available = false;
        // Terminal reads require no identifier/key access or identity-gate lock after resource locks.
        var current = await scenario.ReadAsync();
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await using var verification = scenario.Fixture.Database.Context();
        verification.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        var sanction = await verification.ModerationSanctions.SingleAsync(row => row.Id == scenario.Fixture.SanctionId);
        await Assert.That(sanction.SubjectId).IsEqualTo(scenario.Fixture.Subject);
        await Assert.That(sanction.Revision).IsEqualTo(1L);
        await Assert.That(await verification.UserExternalLogins.AnyAsync(row => row.UserId == scenario.Fixture.Subject)).IsFalse();
        await Assert.That(await verification.Set<UserPii>().AnyAsync(row => row.UserId == scenario.Fixture.Subject)).IsFalse();
        await Assert.That(await verification.PlatformUserRoles.AnyAsync(row => row.UserId == scenario.FreshSubject)).IsFalse();
    }

    [Test]
    public async Task Continuity_review_deadline_does_not_silently_unban_a_still_effective_canonical_sanction()
    {
        await using var scenario = await ContinuityScenario.CreateAsync(reviewWithin: TimeSpan.FromHours(1));
        await Assert.That((await scenario.MatchAsync()).Actions.Length).IsEqualTo(1);
        scenario.Fixture.Clock.UtcNow = Now.AddHours(1);
        scenario.Keys.Available = false;
        var current = await scenario.ReadAsync();
        await Assert.That(current.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(current.Actions.Length).IsEqualTo(1);
        await Assert.That(current.Actions.Single().ReviewAt).IsEqualTo(Now.AddHours(1));
        await Assert.That(current.Actions.Single().EndsAt).IsEqualTo(Now.AddDays(2));
        await Assert.That(current.CurrentUntil > scenario.Fixture.Clock.UtcNow).IsTrue();
        await using var verification = scenario.Fixture.Database.Context();
        verification.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
        var sanction = await verification.ModerationSanctions.SingleAsync(row => row.Id == scenario.Fixture.SanctionId);
        await Assert.That(sanction.IsEffectiveAt(scenario.Fixture.Clock.UtcNow)).IsTrue();
    }

    [Test]
    public async Task Continuity_retry_is_idempotent_and_rollback_cannot_publish_a_binding()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var rolledBack = await scenario.MatchAsync(commit: false);
        await Assert.That(rolledBack.Actions.Length).IsEqualTo(1);
        await using (var observer = scenario.Fixture.Database.Context())
            await Assert.That(await observer.Set<ModerationRecognitionContinuityBinding>()
                .IgnoreAllFilters("Continuity rollback witness").AnyAsync()).IsFalse();
        var first = await scenario.MatchAsync();
        var retry = await scenario.MatchAsync();
        await Assert.That(retry.Actions.Single().BindingId).IsEqualTo(first.Actions.Single().BindingId);
        await using var verification = scenario.Fixture.Database.Context();
        await Assert.That(await verification.Set<ModerationRecognitionContinuityBinding>()
            .IgnoreAllFilters("Continuity idempotency witness").CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("issuer-case")]
    [Arguments("unrelated-key")]
    [Arguments("unrelated-provider")]
    [Arguments("local-uuid")]
    public async Task Continuity_never_infers_a_person_from_an_unrelated_qualified_account(string variation)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        var account = variation switch
        {
            "issuer-case" => new ProviderAccountKey(scenario.Account.ProviderKind, scenario.Account.Value.ToUpperInvariant()),
            "unrelated-key" => new ProviderAccountKey(scenario.Account.ProviderKind, $"https://other.example.invalid|{Guid.CreateVersion7():D}"),
            "unrelated-provider" => new ProviderAccountKey(AuthenticationProviderKind.Google, scenario.Account.Value),
            _ => new ProviderAccountKey(AuthenticationProviderKind.Local, Guid.CreateVersion7().ToString("D"))
        };
        var matched = await scenario.MatchAsync(account);
        await Assert.That(matched.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(matched.AdmissionExcluded).IsFalse();
        await Assert.That(matched.Actions.Length).IsEqualTo(0);
    }

    [Test]
    [Arguments("overlap")]
    [Arguments("missing-original")]
    [Arguments("replaced-original")]
    [Arguments("unavailable")]
    public async Task Continuity_uses_actual_readable_original_key_material_with_purpose_distinction(string rotation)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.RotateAsync();
        if (rotation == "missing-original") scenario.Keys.IncludeOriginal = false;
        if (rotation == "replaced-original") scenario.Keys.ReplaceOriginal();
        if (rotation == "unavailable") scenario.Keys.Available = false;
        var matched = await scenario.MatchAsync();
        await Assert.That(matched.ActionFailure).IsEqualTo(rotation == "overlap"
            ? ModerationRecognitionContinuityFailure.None : ModerationRecognitionContinuityFailure.KeyUnavailable);
        await Assert.That(matched.AdmissionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(matched.AdmissionExcluded).IsFalse();
        await Assert.That(matched.Actions.Length).IsEqualTo(rotation == "overlap" ? 1 : 0);
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("revoke")]
    [Arguments("source-withdrawal")]
    public async Task Continuity_terminal_read_invalidates_only_original_current_contribution(string correction)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        var before = await scenario.ReadAsync();
        if (correction == "expiry") scenario.Fixture.Clock.UtcNow = Now.AddDays(2);
        else if (correction == "source-withdrawal") await scenario.Fixture.WithdrawPrivacySourceAsync();
        else
        {
            await using var writer = scenario.Fixture.Database.Context();
            writer.ModerationTrustDomainId = scenario.Fixture.Database.Scope.TrustDomainId;
            var sanction = await writer.ModerationSanctions.SingleAsync(row => row.Id == scenario.Fixture.SanctionId);
            sanction.Revoke(sanction.Revision, Now);
            await writer.SaveChangesAsync();
        }
        var after = await scenario.ReadAsync();
        await Assert.That(before.Actions.Length).IsEqualTo(1);
        await Assert.That(after.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.None);
        await Assert.That(after.Actions.Length).IsEqualTo(0);
        await Assert.That(after.Generation == before.Generation).IsFalse();
    }

    [Test]
    [Arguments("source-receipt")]
    [Arguments("adoption-receipt")]
    [Arguments("computed-item")]
    public async Task Continuity_missing_original_receipts_never_fall_back_to_binding_authority(string missing)
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await scenario.MatchAsync();
        await using (var corrupt = scenario.Fixture.Database.Context())
        {
            var item = await corrupt.Set<ModerationRecognitionCaptureItem>()
                .IgnoreAllFilters("Continuity missing receipt witness").SingleAsync();
            if (missing == "computed-item")
            {
                // Corrupt the receipt's item count without deleting a restrict-linked row.
                await corrupt.Set<ModerationRecognitionPending>().IgnoreAllFilters("Continuity missing material witness")
                    .Where(row => row.IntentId == item.IntentId).ExecuteUpdateAsync(
                        setters => setters.SetProperty(row => row.MaterialCount, row => row.MaterialCount + 1));
            }
            else
            {
                var id = missing == "source-receipt" ? item.SourceId : item.AdoptionId;
                await corrupt.Set<ModerationQualificationMutationReceipt>()
                    .IgnoreAllFilters("Continuity missing source receipt witness")
                    .Where(row => row.SourceId == id).ExecuteDeleteAsync();
            }
        }
        var result = await scenario.ReadAsync();
        await Assert.That(result.ActionFailure).IsEqualTo(missing == "computed-item"
            ? ModerationRecognitionContinuityFailure.SettlementRequired : ModerationRecognitionContinuityFailure.SourceUnavailable);
        await Assert.That(result.Actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Continuity_received_unsettled_guard_also_closes_existing_Local_subject()
    {
        await using var scenario = await ContinuityScenario.CreateAsync(settle: false);
        await using var context = scenario.Fixture.Database.Context();
        var service = scenario.Service(context);
        var failure = await scenario.Fixture.Retained.Repository.ExecuteSerializedAsync(async ct =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            return await service.GuardExistingSubjectAsync(scenario.Fixture.Subject, ct);
        }, CancellationToken.None);
        await Assert.That(failure).IsEqualTo(ModerationRecognitionContinuityFailure.SettlementRequired);
        var external = await scenario.MatchAsync();
        await Assert.That(external.Actions.Length).IsEqualTo(0);
        await Assert.That(external.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.SettlementRequired);
    }

    [Test]
    public async Task Continuity_matching_and_current_read_reject_missing_native_transaction()
    {
        await using var scenario = await ContinuityScenario.CreateAsync();
        await using var context = scenario.Fixture.Database.Context();
        var service = scenario.Service(context);
        var result = await service.MatchAndBindAsync(new(scenario.Account, scenario.FreshSubject,
            scenario.Fixture.Database.Scope.Id), CancellationToken.None);
        await Assert.That(result.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
        var read = await service.ReadCurrentInTransactionAsync(scenario.FreshSubject, CancellationToken.None);
        await Assert.That(read.ActionFailure).IsEqualTo(ModerationRecognitionContinuityFailure.NativeBoundaryRequired);
    }

    private sealed class ContinuityScenario : IAsyncDisposable
    {
        public required RecognitionErasureFixture Fixture { get; init; }
        public required ContinuityKeys Keys { get; init; }
        public required ProviderAccountKey Account { get; init; }
        public Guid FreshSubject { get; } = Guid.CreateVersion7();
        public ModerationRecognitionErasureRepository Repository(ExploreDbContext context)
        {
            var composition = Fixture.LifecycleFixture.Bind(context, Fixture.Retained.Repository, Fixture.Retained.Repository);
            return new(context, composition.Provider, Fixture.Clock,
                Fixture.Retained.Repository, Fixture.Retained.Repository, composition.Repository);
        }
        public ModerationRecognitionContinuity Service(ExploreDbContext context) =>
            new(Repository(context), new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        public ValueTask DisposeAsync() => Fixture.DisposeAsync();

        public async Task RotateAsync()
        {
            await using var context = Fixture.Database.Context();
            var composition = Fixture.LifecycleFixture.Bind(context, Fixture.Retained.Repository, Fixture.Retained.Repository);
            var repository = composition.Repository;
            var service = composition.Lifecycle;
            var current = (await repository.FindAsync(Fixture.Database.Scope.TrustDomainId, CancellationToken.None))!;
            Keys.CandidateOnly = true;
            var staged = await service.StageAsync(current.Id, current.Revision, CancellationToken.None);
            await service.AcknowledgeAsync(current.Id, staged.Revision, CancellationToken.None);
            Keys.CandidateOnly = false;
            Keys.Rotated = true;
            await service.SwitchAsync(current.Id, "replacement", staged.Revision, CancellationToken.None);
        }

        public async Task<ModerationRecognitionContinuityResult> MatchAsync(ProviderAccountKey? account = null, bool commit = true)
        {
            await using var context = Fixture.Database.Context();
            var service = Service(context);
            return await service.ExecuteWithRecognitionLockAsync(ct =>
                Fixture.Retained.Repository.ExecuteSerializedAsync(async inner =>
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, inner);
                    if (!await context.Users.AnyAsync(row => row.Id == FreshSubject, inner))
                        context.Users.Add(new User { Id = FreshSubject, Pii = new UserPii
                        { UserId = FreshSubject, Email = $"{FreshSubject:N}@example.invalid", FirstName = "Fresh", LastName = "Subject" } });
                    var result = await service.MatchAndBindAsync(new(account ?? Account, FreshSubject, Fixture.Database.Scope.Id), inner);
                    if (commit && !result.AdmissionExcluded && result.ActionFailure == ModerationRecognitionContinuityFailure.None
                        && result.AdmissionFailure == ModerationRecognitionContinuityFailure.None)
                    {
                        await context.SaveChangesAsync(inner);
                        await transaction.CommitAsync(inner);
                    }
                    return result;
                }, ct), CancellationToken.None);
        }

        public async Task<ModerationRecognitionContinuityResult> ReadAsync()
        {
            await using var context = Fixture.Database.Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            return await Service(context).ReadCurrentInTransactionAsync(FreshSubject, CancellationToken.None);
        }

        public static async Task<ContinuityScenario> CreateAsync(bool settle = true, TimeSpan? reviewWithin = null,
            Func<AuthorityScope, AuthorityScope>? actionScope = null,
            ImmutableArray<ModerationCapability> capabilities = default, TimeSpan? sanctionDuration = null)
        {
            var fixture = await RecognitionErasureFixture.CreateAsync(reviewWithin: reviewWithin,
                actionScope: actionScope, capabilities: capabilities, sanctionDuration: sanctionDuration);
            await using var context = fixture.Database.Context();
            var login = await context.UserExternalLogins.SingleAsync(row => row.UserId == fixture.Subject);
            var keys = new ContinuityKeys(fixture.Clock, fixture.Keys.CopyOriginalMaterial());
            fixture.SelectedSecrets.ResolveAsync(ModerationRecognitionKeyRing.SecretSettingKey, null, Arg.Any<CancellationToken>())
                .Returns(_ => keys.Available
                    ? SecretResolutionResult.Resolved(keys.Selection(fixture.Database.Scope.TrustDomainId))
                    : SecretResolutionResult.Unavailable);
            var scenario = new ContinuityScenario
            {
                Fixture = fixture, Keys = keys,
                Account = new((AuthenticationProviderKind)login.AuthenticationProviderId, login.ProviderKey)
            };
            context.ChangeTracker.Clear();
            var repository = scenario.Repository(context);
            if (!settle)
            {
                await repository.ReceiveAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None);
                return scenario;
            }
            var capture = new ModerationRecognitionErasure(repository, fixture.Retained.Repository, fixture.Retained.Repository,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
            var fact = await capture.AppendAsync(fixture.Request, fixture.ReceiptHash, Now.AddHours(2), CancellationToken.None)
                ?? throw new InvalidOperationException("Native original capture did not settle.");
            await capture.ExecutePurgeAsync(async ct =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                await repository.VerifyPurgeAsync(fact, ct);
                await context.UserExternalLogins.Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync(ct);
                await context.Set<UserPii>().Where(row => row.UserId == fixture.Subject).ExecuteDeleteAsync(ct);
                var old = await context.Users.SingleAsync(row => row.Id == fixture.Subject, ct);
                old.IsDeleted = true;
                old.DeletedAt = Now.UtcDateTime;
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return true;
            }, CancellationToken.None);
            return scenario;
        }
    }

    private sealed class ContinuityKeys(DeploymentClock clock, byte[] originalMaterial)
        : IModerationRecognitionKeyRingProvider, IModerationRecognitionKeyMaterialSource
    {
        private byte[] original = originalMaterial;
        private readonly byte[] replacement = RandomNumberGenerator.GetBytes(32);
        public bool Available { get; set; } = true;
        public bool Rotated { get; set; }
        public bool CandidateOnly { get; set; }
        public bool IncludeOriginal { get; set; } = true;
        public void ReplaceOriginal() => original = RandomNumberGenerator.GetBytes(32);
        public Task<ModerationRecognitionKeyRing> ObserveAsync(Guid trust, CancellationToken token) => ResolveAsync(trust, token);
        public Task<ModerationRecognitionKeyRing> ResolveAsync(Guid trust, CancellationToken token) =>
            Task.FromResult(ModerationRecognitionKeyRing.Parse(Selection(trust), trust, clock));
        public ResolvedSecret Selection(Guid trust)
        {
            if (!Available) throw new InvalidOperationException("moderation_recognition_ring_unavailable");
            var entries = new List<object>();
            if (IncludeOriginal) entries.Add(new { kid = "original", generation = 1,
                k = Encode(original), status = Rotated ? "readable" : "active" });
            if (Rotated || CandidateOnly) entries.Add(new { kid = "replacement", generation = 2,
                k = Encode(replacement), status = CandidateOnly ? "readable" : "active" });
            var json = JsonSerializer.Serialize(new
            {
                version = 1, trustDomainId = trust.ToString("D"), ringGeneration = Rotated ? 3 : CandidateOnly ? 2 : 1,
                issuedAt = clock.UtcNow.ToString("O"), validUntil = clock.UtcNow.AddMinutes(5).ToString("O"), keys = entries
            });
            return new ResolvedSecret(
                ModerationRecognitionKeyRing.SecretSettingKey, json, SecretSourceType.EnvironmentVariable,
                SecretScope.Instance, null, clock.UtcNow, "original-binding");
        }
        private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
