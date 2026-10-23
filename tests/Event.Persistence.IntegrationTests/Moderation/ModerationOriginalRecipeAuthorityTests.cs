using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    public async Task Replaced_selection_allows_only_exact_original_bound_measurement_preparation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        var replacement = await ReplaceSelectedPublicationAsync(fixture);
        preparation = preparation with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        var ordinary = preparation with { OriginalEffect = null, OperationId = Guid.CreateVersion7() };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, ordinary)).IsSuccess).IsFalse();
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, preparation)).IsSuccess).IsTrue();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var challenge = await reader.ModerationConsumerMeasurementChallenges.SingleAsync();
        await Assert.That(challenge.PublicationId).IsEqualTo(preparation.Binding.PublicationId);
        await Assert.That(challenge.ValidUntil).IsEqualTo(Now.AddSeconds(30));
        await Assert.That(challenge.OriginalEffectId).IsEqualTo(preparation.OriginalEffect!.EffectId);
        await Assert.That(challenge.OriginalSnapshotHash).IsEqualTo(preparation.OriginalEffect.SnapshotHash);
        await Assert.That(challenge.OriginalEffectBindingHash).IsEqualTo(preparation.OriginalEffect.EffectBindingHash);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).LatestPublicationId).IsEqualTo(replacement);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    [Arguments("hash")]
    [Arguments("effect")]
    [Arguments("binding")]
    [Arguments("tenant")]
    [Arguments("expired")]
    [Arguments("cleanup")]
    [Arguments("publisher")]
    [Arguments("erasure")]
    [Arguments("extended")]
    public async Task Original_preparation_requires_exact_custody_and_current_humans(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        preparation = preparation with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        if (change == "hash") preparation = preparation with
        {
            OriginalEffect = preparation.OriginalEffect! with { SnapshotHash = new string('e', 64) }
        };
        if (change == "effect") preparation = preparation with
        {
            OriginalEffect = preparation.OriginalEffect! with { EffectId = Guid.CreateVersion7() }
        };
        if (change == "binding") preparation = preparation with
        {
            Binding = preparation.Binding with { PublicationId = Guid.CreateVersion7() }
        };
        if (change == "tenant") preparation = preparation with
        {
            Target = AuthorityScope.Tenant(fixture.Database.Instance, fixture.OtherTenantId)
        };
        if (change == "extended") preparation = preparation with { ValidUntil = fixture.PurposeDeadline.AddTicks(1) };
        if (change == "expired") fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        if (change == "erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        if (change is "cleanup" or "publisher")
        {
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (change == "cleanup")
                await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
            else
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Database.AdministratorId).ExecuteDeleteAsync();
        }
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.UrgencyAssessorId, preparation)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementChallenges.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Exact_signed_original_receipt_survives_replacement_without_authorizing_current_activation(bool remeasure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        if (remeasure) await ReplaceSelectedPublicationAsync(fixture);
        preparation = preparation with
        {
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            OriginalEffect = remeasure ? preparation.OriginalEffect : null
        };
        using var measurement = await SignOriginalMeasurementAsync(fixture, preparation);
        if (!remeasure) await ReplaceSelectedPublicationAsync(fixture);
        var original = await ObserveOriginalMeasurementAsync(fixture, measurement.Binding, original: true);
        await Assert.That(original).IsNotNull();
        await Assert.That(original!.Receipt.ReceiptId).IsEqualTo(measurement.ReceiptId);
        await Assert.That(original.ValidUntil).IsEqualTo(Now.AddSeconds(20));
        await Assert.That(await ObserveOriginalMeasurementAsync(fixture, measurement.Binding, original: false)).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
        await Assert.That(await reader.ModerationOriginalEffectCompletionReceipts.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("expiry")]
    [Arguments("issuer")]
    [Arguments("actor")]
    [Arguments("cleanup")]
    [Arguments("erasure")]
    [Arguments("acceptance")]
    [Arguments("signature")]
    public async Task Retained_original_measurement_does_not_survive_expiry_or_withdrawal(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        preparation = preparation with { ExpectedQualificationRevision = await OriginalEpochAsync(fixture) };
        using var measurement = await SignOriginalMeasurementAsync(fixture, preparation);
        if (change == "expiry") fixture.Clock.UtcNow = Now.AddSeconds(20);
        if (change == "erasure") await EraseOriginalParticipantAsync(fixture, "representative");
        if (change == "issuer")
            await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId,
                new RevokeModerationMeasurementIssuerSource
                {
                    OperationId = Guid.CreateVersion7(),
                    Target = fixture.Database.Scope,
                    ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
                    EnrollmentId = measurement.EnrollmentId,
                    ExpectedEnrollmentRevision = 1
                })).IsSuccess).IsTrue();
        if (change is "actor" or "cleanup" or "acceptance" or "signature")
        {
            await using var writer = fixture.Context();
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            if (change == "actor")
                await writer.PlatformUserRoles.Where(row => row.UserId == fixture.Database.UrgencyAssessorId).ExecuteDeleteAsync();
            if (change == "cleanup")
                await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
            if (change == "acceptance")
                await writer.ModerationQualificationMutationReceipts.Where(row => row.SourceId == measurement.ReceiptId).ExecuteDeleteAsync();
            if (change == "signature")
                await writer.ModerationConsumerMeasurementReceipts.Where(row => row.Id == measurement.ReceiptId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.SignedToken, measurement.InvalidSignature));
        }
        await Assert.That(await ObserveOriginalMeasurementAsync(fixture, measurement.Binding, original: true)).IsNull();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationEffectIntents.SingleAsync()).CompletedAt).IsNull();
    }

    private static async Task<long> OriginalEpochAsync(Fixture fixture)
    {
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        return (await reader.ModerationQualificationEpochs.SingleAsync()).Revision;
    }

    [Test]
    public async Task Original_deadline_at_actual_writer_flush_rolls_back_challenge_and_receipt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        var epoch = await OriginalEpochAsync(fixture);
        preparation = preparation with { ExpectedQualificationRevision = epoch };
        fixture.Database.QualificationSaved = () => fixture.Database.EvaluationTime = fixture.PurposeDeadline;
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, preparation)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementChallenges.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == preparation.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(epoch);
    }

    [Test]
    public async Task Native_original_cleanup_never_reopens_measurement_or_substantive_completion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preparation = await OriginalPreparationAsync(fixture);
        await ReplaceSelectedPublicationAsync(fixture);
        fixture.Clock.UtcNow = Now.AddYears(1);
        fixture.Database.EvaluationTime = fixture.Clock.UtcNow;
        await using (var context = fixture.Context())
        {
            var identity = await fixture.AuthenticateAsync(context);
            var result = await new ModerationNoticeCleanupRepository(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                fixture.Clock).PruneAsync(null, 100);
            await Assert.That(result.Outcome).IsEqualTo(ModerationNoticeCleanupOutcome.Applied);
        }
        preparation = preparation with
        {
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            ValidUntil = fixture.Clock.UtcNow.AddSeconds(30)
        };
        await Assert.That((await fixture.Database.ExecuteAsync(fixture.Database.AdministratorId, preparation)).IsSuccess).IsFalse();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationNoticeSources.CountAsync()).IsEqualTo(0);
        var effect = await reader.ModerationEffectIntents.SingleAsync();
        await Assert.That(effect.OriginalNoticeCleanupId).IsNotNull();
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(await reader.ModerationConsumerMeasurementChallenges.CountAsync()).IsEqualTo(0);
    }

    private static async Task<PrepareModerationConsumerMeasurementSource> OriginalPreparationAsync(Fixture fixture)
    {
        var database = fixture.Database;
        var runtime = new ModerationConsumerInstallationWriterTests.ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var installation = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new InstallModerationConsumerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            InstallationId = installation,
            ConsumerId = runtime.ConsumerId,
            Mode = runtime.Mode,
            ExpectedRuntimeGeneration = runtime.Generation,
            ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
            ValidUntil = Now.AddMinutes(5)
        })).IsSuccess).IsTrue();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var source = await reader.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
        var notice = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!.Notice!;
        return new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            ChallengeId = Guid.CreateVersion7(),
            InstallationId = installation,
            ExpectedInstallationRevision = 1,
            ConsumerId = runtime.ConsumerId,
            ExpectedRuntimeGeneration = runtime.Generation,
            ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
            ValidUntil = Now.AddSeconds(30),
            OriginalEffect = new(fixture.EffectId, source.SnapshotHash, source.EffectBindingHash),
            Binding = new(notice.PolicyId, notice.PolicyRevision, notice.PublicationId, database.Scope,
                ModerationRuleReference.Create(notice.Rule.OwnerId, notice.Rule.Code, notice.Rule.Revision),
                ModerationCapability.ReadResource, ModerationNativeMode.Embedded)
        };
    }

    private static async Task<Guid> ReplaceSelectedPublicationAsync(Fixture fixture)
    {
        ModerationPolicyPublication publication;
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var selected = await reader.ModerationPolicySelections.SingleAsync();
            publication = await ModerationPolicyPublicationGraph.Include(reader.ModerationPolicyPublications.AsNoTracking())
                .SingleAsync(row => row.Id == selected.LatestPublicationId);
        }
        var original = ModerationPolicyPublicationProjection.RestoreHistory(publication)!;
        var request = fixture.Database.Publication(await OriginalEpochAsync(fixture), ModerationReferenceProfileId.CFG09);
        request = request with
        {
            ExpectedPublicationId = publication.Id,
            Proposed = original.Proposed with
            {
                ExpectedRevision = publication.Revision,
                PublicationId = Guid.CreateVersion7(),
                RequestedState = ModerationPolicyRequestedState.Draft
            }
        };
        var result = await fixture.Database.PublishAsync(fixture.Database.AdministratorId, request);
        await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return result.Publication!.Id;
    }

    private static async Task<ModerationMeasurementReceiptObservation?> ObserveOriginalMeasurementAsync(
        Fixture fixture, ModerationActivationBinding binding, bool original)
    {
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        var admin = new AdminContext(new HttpContextAccessor(), new PlatformUserRoleRepository(context),
            new TenantUserRoleGrantRepository(context), new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new UserExternalLoginRepository(context), NullLogger<AdminContext>.Instance);
        var privacy = fixture.Database.Privacy.Authority(context, fixture.Clock);
        return await privacy.ExecuteSerializedAsync(async token =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
            var authority = await new ModerationMaintenanceAuthority(context, identity.Machine, identity.Bindings,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock)
                .AcquireForEffectAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision, privacy, token);
            if (authority is null) return null;
            var source = await context.ModerationNoticeSources.AsNoTracking()
                .SingleOrDefaultAsync(row => row.EffectId == fixture.EffectId, token);
            if (source is null) return null;
            var retained = await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority, transaction.TransactionId,
                new(fixture.EffectId, source.SnapshotHash, source.EffectBindingHash), privacy, fixture.Clock, token);
            if (retained is null) return null;
            var publications = new ModerationPolicyPublicationRepository(context, admin, identity.Machine,
                fixture.Database.Inventory, null, fixture.Clock);
            using var session = new ModerationConsumerEvidenceSession(context, transaction, authority, retained.Policy,
                retained.CurrentAsync, publications, fixture.Database.Inventory, fixture.Database.ConsumerRuntimeSources,
                new ModerationMeasurementReceiptVerifier(), fixture.Clock, null, original ? retained : null);
            var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, identity.Machine,
                fixture.Database.Inventory, fixture.Clock);
            var native = await session.AcquireNativeAsync(transaction.TransactionId, fixture.Clock.UtcNow, token);
            var definition = native is null ? null : await definitions.ReadDefinitionAsync(native, fixture.Clock.UtcNow, token);
            if (original) await Assert.That(definition?.Policy.Publication.PublicationId).IsEqualTo(binding.PublicationId);
            else await Assert.That(definition).IsNull();
            return await session.AcquireMeasurementAsync(binding, "resource.payload", fixture.Clock.UtcNow, token);
        }, CancellationToken.None);
    }

    // Genuine native preparation/enrollment/acceptance and ES256 authentication. These assertions
    // deliberately claim no passing catalogue, measured terminal conformance or whole completion.
    private static async Task<OriginalSignedMeasurement> SignOriginalMeasurementAsync(
        Fixture fixture, PrepareModerationConsumerMeasurementSource preparation)
    {
        var database = fixture.Database;
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, preparation)).IsSuccess).IsTrue();
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = Guid.CreateVersion7();
        var enrollmentId = Guid.CreateVersion7();
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, new EnrollModerationMeasurementIssuerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            EnrollmentId = enrollmentId,
            KeyId = keyId,
            Issuer = "original-recipe-measurement",
            PublicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            RunnerDigest = new string('a', 64),
            CatalogueDigest = new string('b', 64),
            TestBundleDigest = new string('c', 64),
            ValidUntil = Now.AddMinutes(2)
        })).IsSuccess).IsTrue();
        await using var reader = fixture.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var challenge = await reader.ModerationConsumerMeasurementChallenges.SingleAsync(row => row.Id == preparation.ChallengeId);
        var assertion = new ModerationMeasurementReceipt
        {
            Issuer = "original-recipe-measurement",
            ReceiptId = Guid.CreateVersion7(),
            ChallengeId = challenge.Id,
            ChallengeNonce = challenge.Nonce,
            RunId = Guid.CreateVersion7(),
            HostBindingId = database.HostId,
            TrustDomainId = database.Scope.TrustDomainId,
            TenantId = database.TenantId,
            Binding = preparation.Binding,
            ConsumerId = challenge.ConsumerId,
            ConsumerContractDigest = challenge.ConsumerContractDigest.ToLowerInvariant(),
            InstallationId = challenge.InstallationId,
            InstallationRevision = challenge.InstallationRevision,
            InstallationWriterGeneration = challenge.InstallationWriterGeneration,
            RuntimeGeneration = challenge.RuntimeGeneration,
            InventoryFingerprint = challenge.InventoryFingerprint.ToLowerInvariant(),
            RunnerDigest = new string('a', 64),
            CatalogueDigest = new string('b', 64),
            TestBundleDigest = new string('c', 64),
            ProductArtifactDigest = new string('d', 64),
            ConfigurationDigest = new string('e', 64),
            PrimaryConfigurationDigest = new string('1', 64),
            TestConfigurationDigest = new string('2', 64),
            ProviderCode = "sqlite",
            ProviderVersion = "10.0",
            CaseIds = ["original.authority-boundary"],
            ExecutedCount = 1,
            AttemptCount = 1,
            ResultArtifactDigest = new string('f', 64),
            StartedAt = Now,
            CompletedAt = Now,
            IssuedAt = Now,
            ValidUntil = Now.AddSeconds(20)
        };
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var input = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            alg = "ES256",
            typ = ModerationMeasurementReceiptVerifier.RequiredType,
            kid = keyId.ToString("D")
        })) + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(assertion));
        var signed = input + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AcceptModerationConsumerMeasurementSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = await OriginalEpochAsync(fixture),
            ReceiptId = assertion.ReceiptId,
            ChallengeId = challenge.Id,
            IssuerEnrollmentId = enrollmentId,
            Binding = assertion.Binding,
            SignedToken = signed,
            OriginalEffect = preparation.OriginalEffect
        })).IsSuccess).IsTrue();
        return new(key, assertion.Binding, assertion.ReceiptId, enrollmentId, input + "." + Encode(new byte[64]));
    }

    private sealed record OriginalSignedMeasurement(ECDsa Key, ModerationActivationBinding Binding,
        Guid ReceiptId, Guid EnrollmentId, string InvalidSignature) : IDisposable
    {
        public void Dispose() => Key.Dispose();
    }
}
