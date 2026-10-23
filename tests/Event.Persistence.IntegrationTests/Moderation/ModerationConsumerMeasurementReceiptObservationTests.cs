using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationConsumerMeasurementReceiptObservationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(8)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    [Arguments(12)]
    [Arguments(13)]
    [Arguments(14)]
    [Arguments(15)]
    [Arguments(16)]
    [Arguments(17)]
    [Arguments(18)]
    [Arguments(19)]
    public async Task CurrentAssertion_RequiresExactNativeSourcesAndCurrentAccountableHumans(int change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        if (change == 1) fixture.Runtime.Generation = new string('e', 64);
        if (change is 2 or 12)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new WithdrawModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 8,
                InstallationId = fixture.Receipt.InstallationId, ExpectedInstallationRevision = 1
            })).IsSuccess).IsTrue();
        if (change == 3)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationMeasurementIssuerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 8,
                EnrollmentId = fixture.EnrollmentId, ExpectedEnrollmentRevision = 1
            })).IsSuccess).IsTrue();
        if (change == 12)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new InstallModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 9,
                InstallationId = Guid.CreateVersion7(), ConsumerId = fixture.Runtime.ConsumerId, Mode = fixture.Runtime.Mode,
                ExpectedRuntimeGeneration = fixture.Runtime.Generation,
                ExpectedInventoryFingerprint = database.Inventory.Fingerprint, ValidUntil = Now.AddMinutes(5)
            })).IsSuccess).IsTrue();
        if (change is >= 4 and <= 11)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change is >= 4 and <= 7)
            {
                var id = change switch
                {
                    4 => fixture.Receipt.InstallationId, 5 => fixture.Receipt.ChallengeId,
                    6 => fixture.EnrollmentId, _ => fixture.Receipt.ReceiptId
                };
                writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.SourceId == id));
            }
            else if (change is >= 8 and <= 10)
            {
                var human = change switch
                {
                    8 => database.AdministratorId, 9 => database.UrgencyAssessorId, _ => database.ReviewerId
                };
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == human));
                if (change == 10)
                {
                    var membership = await writer.TenantUsers.SingleAsync(row => row.UserId == human);
                    writer.TenantUserRoleGrants.Add(new TenantUserRoleGrant
                    {
                        Id = Guid.CreateVersion7(), TenantId = database.TenantId, Tenant = null!,
                        TenantUserId = membership.Id, TenantUser = membership,
                        RoleId = (int)RoleEnum.TenantAdmin, RoleScopeId = (int)RoleScopeEnum.Tenant, Role = null!
                    });
                }
            }
            else
                writer.ModerationConsumerMeasurementReceipts.Remove(await writer.ModerationConsumerMeasurementReceipts.SingleAsync());
            await writer.SaveChangesAsync();
        }
        var clock = new ObservationClock { UtcNow = change == 13 ? Now.AddSeconds(30) : Now };
        var binding = change == 14
            ? fixture.Receipt.Binding with { PublicationId = Guid.CreateVersion7() } : fixture.Receipt.Binding;
        var observed = await ObserveAsync(database, fixture, clock, binding, foreignTransaction: change == 15,
            afterVerification: change == 16 ? () => clock.UtcNow = Now.AddSeconds(30)
                : change == 17 ? () => fixture.Runtime.Generation = new string('e', 64)
                : change == 19 ? () => fixture.Runtime.ProductArtifactDigest = new string('e', 64) : null,
            missingVerifier: change == 18);
        if (change == 0)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.Receipt.ReceiptId).IsEqualTo(fixture.Receipt.ReceiptId);
            await Assert.That(observed.Receipt.ChallengeNonce).IsEqualTo(fixture.Receipt.ChallengeNonce);
            await Assert.That(observed.Receipt.RunId).IsEqualTo(fixture.Receipt.RunId);
            await Assert.That(observed.Receipt.CaseIds.SequenceEqual(fixture.Receipt.CaseIds)).IsTrue();
            await Assert.That(observed.IssuerEnrollmentId).IsEqualTo(fixture.EnrollmentId);
            await Assert.That(observed.TrustKey.KeyId).IsEqualTo(fixture.KeyId);
            await Assert.That(observed.WriterGeneration).IsEqualTo(6L);
            await Assert.That(observed.AcceptedByHumanId).IsEqualTo(database.AdministratorId);
            await Assert.That(observed.PreparedByHumanId).IsEqualTo(database.UrgencyAssessorId);
            await Assert.That(observed.ApprovedByHumanId).IsEqualTo(database.ReviewerId);
            await Assert.That(observed.ValidUntil).IsEqualTo(Now.AddSeconds(30));
            await Assert.That(observed.Runtime.RuntimeGeneration).IsEqualTo(fixture.Receipt.RuntimeGeneration);
            await Assert.That(observed.Runtime.ProductArtifactDigest).IsNull();
        }
        else await Assert.That(observed).IsNull();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(change == 11 ? 0 : 1);
        if (change != 11)
        {
            var retained = await reader.ModerationConsumerMeasurementReceipts.SingleAsync();
            await Assert.That(retained.SignedToken).IsEqualTo(fixture.Token);
            await Assert.That(retained.WriterGeneration).IsEqualTo(6L);
        }
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
    }

    [Test]
    [Arguments(16, 0)]
    [Arguments(16, 1)]
    [Arguments(16, 2)]
    [Arguments(16, 3)]
    [Arguments(17, 0)]
    [Arguments(17, 1)]
    [Arguments(17, 2)]
    [Arguments(17, 3)]
    [Arguments(18, 0)]
    [Arguments(18, 1)]
    [Arguments(18, 2)]
    [Arguments(18, 3)]
    public async Task ChangedOriginalCommandPrincipalEpochOrTime_CannotSupplyCurrentSourceProvenance(int sourceKind, int change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var original = await writer.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.SourceKind == (ModerationQualificationSourceKind)sourceKind);
            writer.ModerationQualificationMutationReceipts.Remove(original);
            await writer.SaveChangesAsync();
            writer.ModerationQualificationMutationReceipts.Add(ModerationQualificationMutationReceipt.Complete(
                original.Id, change == 0 ? new string('e', 64) : original.RequestDigest,
                original.HostBindingId, original.TrustDomainId, original.TenantId, original.SourceKind, original.SourceId,
                change == 1 ? database.SubjectId : original.PrincipalId, original.ResultRevision,
                change == 2 ? original.QualificationRevision + 1 : original.QualificationRevision,
                change == 3 ? original.CompletedAt.AddTicks(-1) : original.CompletedAt));
            await writer.SaveChangesAsync();
        }
        await Assert.That(await ObserveAsync(database, fixture, new(), fixture.Receipt.Binding)).IsNull();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationConsumerMeasurementReceipts.SingleAsync()).SignedToken).IsEqualTo(fixture.Token);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ReplacedRetainedToken_CannotSubstituteSignedCoordinatesOrOriginalAcceptance(int change)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var assertion = change switch
        {
            0 => fixture.Receipt with { ChallengeNonce = new string('e', 64) },
            1 => fixture.Receipt with { InstallationId = Guid.CreateVersion7() },
            2 => fixture.Receipt with { RunId = Guid.CreateVersion7() },
            3 => fixture.Receipt with { IssuedAt = Now.AddTicks(1) },
            _ => fixture.Receipt
        };
        using var foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string token = fixture.Sign(assertion, change == 4 ? foreignKey : null);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationConsumerMeasurementReceipts.Remove(await writer.ModerationConsumerMeasurementReceipts.SingleAsync());
            await writer.SaveChangesAsync();
            writer.ModerationConsumerMeasurementReceipts.Add(ModerationConsumerMeasurementReceipt.Accept(
                fixture.Receipt.ReceiptId, fixture.Receipt.HostBindingId, fixture.Receipt.TrustDomainId,
                database.TenantId, fixture.Receipt.ChallengeId, fixture.EnrollmentId, fixture.KeyId,
                fixture.Receipt.RunId, token, Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token))),
                database.AdministratorId, 6, Now, Now.AddSeconds(30)));
            await writer.SaveChangesAsync();
        }
        await Assert.That(await ObserveAsync(database, fixture, new(), fixture.Receipt.Binding)).IsNull();
    }

    private static async Task<ModerationMeasurementReceiptObservation?> ObserveAsync(
        SourceDatabase database, Fixture fixture, ObservationClock clock, ModerationActivationBinding binding,
        bool foreignTransaction = false, Action? afterVerification = null, bool missingVerifier = false)
    {
        await using var context = database.Context();
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock,
            missingVerifier ? null : new ObservingVerifier(afterVerification), useMeasurementVerifier: !missingVerifier);
        var decision = fixture.Decision;
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, decision.Subject,
            database.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationMeasurementReceiptObservation? observed = null;
        bool entered = false;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            entered = true;
            await Assert.That((await participant.ResolveAsync(session, operation, token)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Known);
            observed = await participant.AcquireConsumerMeasurementReceiptAsync(
                foreignTransaction ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                binding, "resource.payload", fixture.Receipt.ReceiptId, Now, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        await Assert.That(entered).IsTrue();
        return observed;
    }

    private sealed class ObservationClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class ObservingVerifier(Action? afterVerification) : IModerationMeasurementReceiptVerifier
    {
        public ModerationMeasurementVerificationResult Verify(string? token, ModerationMeasurementTrustKey? trust,
            ModerationActivationBinding? expectedBinding, DateTimeOffset observedAt)
        {
            var verified = new ModerationMeasurementReceiptVerifier().Verify(token, trust, expectedBinding, observedAt);
            afterVerification?.Invoke();
            return verified;
        }
    }

    private sealed class Fixture(ECDsa key, Guid keyId, Guid enrollmentId,
        ModerationConsumerInstallationWriterTests.ControlledRuntime runtime,
        ModerationMeasurementReceipt receipt, string signedToken, ModerationDecision decision) : IDisposable
    {
        public Guid KeyId { get; } = keyId;
        public Guid EnrollmentId { get; } = enrollmentId;
        public ModerationConsumerInstallationWriterTests.ControlledRuntime Runtime { get; } = runtime;
        public ModerationMeasurementReceipt Receipt { get; } = receipt;
        public string Token { get; } = signedToken;
        public ModerationDecision Decision { get; } = decision;

        public string Sign(ModerationMeasurementReceipt assertion, ECDsa? signingKey = null) =>
            Sign(assertion, KeyId, signingKey ?? key);

        private static string Sign(ModerationMeasurementReceipt assertion, Guid id, ECDsa signingKey)
        {
            static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            string input = Encode(JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "ES256", typ = ModerationMeasurementReceiptVerifier.RequiredType, kid = id.ToString("D")
            })) + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(assertion));
            return input + "." + Encode(signingKey.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }

        public static async Task<Fixture> CreateAsync(SourceDatabase database,
            ModerationMeasurementCatalogueArtifact? catalogue = null,
            Func<ModerationMeasurementReceipt, ModerationMeasurementReceipt>? changeReceipt = null,
            string consumerId = "resource.payload")
        {
            var (runtime, preparation) = await ModerationConsumerMeasurementChallengeWriterTests.PrepareAsync(database, consumerId);
            if (catalogue is not null) runtime.ProductArtifactDigest = new string('d', 64);
            await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, preparation)).IsSuccess).IsTrue();
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var keyId = Guid.CreateVersion7();
            var enrollmentId = Guid.CreateVersion7();
            await Assert.That((await database.ExecuteAsync(database.ReviewerId, new EnrollModerationMeasurementIssuerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
                EnrollmentId = enrollmentId, KeyId = keyId, Issuer = "native-observation-runner",
                PublicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                RunnerDigest = new string('a', 64), CatalogueDigest = catalogue?.Digest ?? new string('b', 64),
                TestBundleDigest = new string('c', 64), ValidUntil = Now.AddMinutes(2)
            })).IsSuccess).IsTrue();
            ModerationConsumerMeasurementChallenge challenge;
            await using (var reader = database.Context())
            {
                reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
                challenge = await reader.ModerationConsumerMeasurementChallenges.SingleAsync();
            }
            var receipt = new ModerationMeasurementReceipt
            {
                Issuer = "native-observation-runner", ReceiptId = Guid.CreateVersion7(), ChallengeId = challenge.Id,
                ChallengeNonce = challenge.Nonce, RunId = Guid.CreateVersion7(), HostBindingId = database.HostId,
                TrustDomainId = database.Scope.TrustDomainId, TenantId = database.TenantId, Binding = preparation.Binding,
                ConsumerId = challenge.ConsumerId, ConsumerContractDigest = challenge.ConsumerContractDigest.ToLowerInvariant(),
                InstallationId = challenge.InstallationId, InstallationRevision = challenge.InstallationRevision,
                InstallationWriterGeneration = challenge.InstallationWriterGeneration, RuntimeGeneration = challenge.RuntimeGeneration,
                InventoryFingerprint = challenge.InventoryFingerprint.ToLowerInvariant(), RunnerDigest = new string('a', 64),
                CatalogueDigest = new string('b', 64), TestBundleDigest = new string('c', 64),
                ProductArtifactDigest = new string('d', 64), ConfigurationDigest = new string('e', 64),
                PrimaryConfigurationDigest = new string('1', 64), TestConfigurationDigest = new string('2', 64),
                ProviderCode = "sqlite", ProviderVersion = "10.0", CaseIds = ImmutableArray.Create("case.observation"),
                ExecutedCount = 1, AttemptCount = 1, ResultArtifactDigest = new string('f', 64),
                StartedAt = Now, CompletedAt = Now, IssuedAt = Now, ValidUntil = Now.AddSeconds(30)
            };
            if (catalogue is not null)
            {
                await using var primary = database.Context();
                var facts = ReadPrimaryFacts(primary);
                receipt = receipt with
                {
                    CatalogueDigest = catalogue.Digest,
                    PrimaryConfigurationDigest = facts.Digest,
                    ProviderCode = facts.Provider,
                    ProviderVersion = facts.Version,
                    CaseIds = catalogue.Catalogue.CaseIds,
                    ExecutedCount = catalogue.Catalogue.CaseIds.Length
                };
            }
            if (changeReceipt is not null) receipt = changeReceipt(receipt);
            string signedToken = Sign(receipt, keyId, key);
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new AcceptModerationConsumerMeasurementSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
                ReceiptId = receipt.ReceiptId, ChallengeId = receipt.ChallengeId, IssuerEnrollmentId = enrollmentId,
                Binding = receipt.Binding, SignedToken = signedToken
            })).IsSuccess).IsTrue();
            var opened = database.OpenCase() with { ExpectedQualificationRevision = 6 };
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, opened)).IsSuccess).IsTrue();
            await Assert.That((await database.ExecuteAsync(database.SubjectId, new RecordModerationCaseResponse
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 7,
                CaseId = opened.CaseId, ExpectedCaseRevision = 1, EvidenceId = Guid.CreateVersion7(),
                Statement = "The actual subject records the response required by this acquisition."
            })).IsSuccess).IsTrue();
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
                SubjectReference.Create(ModerationSubjectKind.Human, database.SubjectId, database.Instance),
                SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.Instance),
                database.Scope, database.Scope, receipt.Binding.Rule, [ModerationCapability.ReadResource],
                Now, Now.AddHours(1), null, 1, Now);
            await using (var writer = database.Context())
            {
                writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
                writer.ModerationDecisions.Add(decision);
                await writer.SaveChangesAsync();
            }
            return new(key, keyId, enrollmentId, runtime, receipt, signedToken, decision);
        }

        public void Dispose() => key.Dispose();
    }
}
