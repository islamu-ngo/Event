using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationConsumerMeasurementReceiptWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AuthenticAssertion_ExactRetryRetainsOriginalAcceptanceWithoutPassingOrActivating()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var request = fixture.Request();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var receipt = await reader.ModerationConsumerMeasurementReceipts.SingleAsync();
        await Assert.That(receipt.Id).IsEqualTo(fixture.Receipt.ReceiptId);
        await Assert.That(receipt.SignedToken).IsEqualTo(request.SignedToken);
        await Assert.That(receipt.WriterGeneration).IsEqualTo(6L);
        await Assert.That(receipt.ValidUntil).IsEqualTo(Now.AddSeconds(30));
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(6L);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
        var changed = request with { SignedToken = fixture.Sign(fixture.Receipt with { RunId = Guid.CreateVersion7() }) };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, changed)).IsSuccess).IsFalse();
    }

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
    public async Task AuthenticButUnboundOrStaleAssertion_CannotCommitAnAcceptancePrefix(int change)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var payload = change switch
        {
            0 => fixture.Receipt with { ChallengeNonce = new string('f', 64) },
            1 => fixture.Receipt with { InstallationId = Guid.CreateVersion7() },
            2 => fixture.Receipt with { RuntimeGeneration = new string('e', 64) },
            3 => fixture.Receipt with { ConsumerContractDigest = new string('e', 64) },
            4 => fixture.Receipt with { InventoryFingerprint = new string('e', 64) },
            5 => fixture.Receipt with { StartedAt = Now.AddTicks(-1), CompletedAt = Now.AddTicks(-1) },
            _ => fixture.Receipt
        };
        var request = fixture.Request() with { SignedToken = fixture.Sign(payload) };
        if (change == 6)
        {
            using var foreignKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            request = request with { SignedToken = fixture.Sign(payload, foreignKey) };
        }
        if (change == 7)
        {
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationMeasurementIssuerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
                EnrollmentId = fixture.EnrollmentId, ExpectedEnrollmentRevision = 1
            })).IsSuccess).IsTrue();
            request = request with { ExpectedQualificationRevision = 6 };
        }
        if (change == 8) database.QualificationSaved = () => fixture.Runtime.Generation = new string('f', 64);
        await Assert.That((await database.ExecuteAsync(change == 9 ? database.SubjectId : database.AdministratorId, request))
            .IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(change == 7 ? 6L : 5L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOriginalTrustOrChallengeReceipt_CannotBeReplacedByValidSignature(bool trust)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var id = trust ? fixture.EnrollmentId : fixture.Receipt.ChallengeId;
            writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.SourceId == id));
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, fixture.Request())).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    private sealed class Fixture(ECDsa key, ModerationQualificationWriterTests.SourceDatabase database,
        Guid enrollmentId, Guid keyId, PrepareModerationConsumerMeasurementSource preparation,
        ModerationConsumerInstallationWriterTests.ControlledRuntime runtime, ModerationMeasurementReceipt receipt) : IDisposable
    {
        public Guid EnrollmentId { get; } = enrollmentId;
        public ModerationConsumerInstallationWriterTests.ControlledRuntime Runtime { get; } = runtime;
        public ModerationMeasurementReceipt Receipt { get; } = receipt;
        public AcceptModerationConsumerMeasurementSource Request() => new()
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 5,
            ReceiptId = Receipt.ReceiptId, ChallengeId = preparation.ChallengeId, IssuerEnrollmentId = EnrollmentId,
            Binding = preparation.Binding, SignedToken = Sign(Receipt)
        };
        public string Sign(ModerationMeasurementReceipt value, ECDsa? signingKey = null)
        {
            static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var header = JsonSerializer.SerializeToUtf8Bytes(new
            {
                alg = "ES256", typ = "islamu-moderation-conformance+jwt", kid = keyId.ToString("D")
            });
            var input = Encode(header) + "." + Encode(JsonSerializer.SerializeToUtf8Bytes(value));
            return input + "." + Encode((signingKey ?? key).SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        }
        public static async Task<Fixture> CreateAsync(ModerationQualificationWriterTests.SourceDatabase database)
        {
            var (runtime, preparation) = await ModerationConsumerMeasurementChallengeWriterTests.PrepareAsync(database);
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, preparation)).IsSuccess).IsTrue();
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var enrollmentId = Guid.CreateVersion7();
            var keyId = Guid.CreateVersion7();
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new EnrollModerationMeasurementIssuerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 4,
                EnrollmentId = enrollmentId, KeyId = keyId, Issuer = "native-test-runner",
                PublicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
                RunnerDigest = new string('a', 64), CatalogueDigest = new string('b', 64),
                TestBundleDigest = new string('c', 64), ValidUntil = Now.AddMinutes(2)
            })).IsSuccess).IsTrue();
            await using var reader = database.Context();
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var challenge = await reader.ModerationConsumerMeasurementChallenges.SingleAsync();
            var host = await reader.ModerationHostBindings.SingleAsync();
            var receipt = new ModerationMeasurementReceipt
            {
                Issuer = "native-test-runner", ReceiptId = Guid.CreateVersion7(), ChallengeId = challenge.Id,
                ChallengeNonce = challenge.Nonce, RunId = Guid.CreateVersion7(), HostBindingId = host.Id,
                TrustDomainId = database.Scope.TrustDomainId, TenantId = database.TenantId, Binding = preparation.Binding,
                ConsumerId = challenge.ConsumerId, ConsumerContractDigest = challenge.ConsumerContractDigest.ToLowerInvariant(),
                InstallationId = challenge.InstallationId, InstallationRevision = challenge.InstallationRevision,
                InstallationWriterGeneration = challenge.InstallationWriterGeneration, RuntimeGeneration = challenge.RuntimeGeneration,
                InventoryFingerprint = challenge.InventoryFingerprint.ToLowerInvariant(), RunnerDigest = new string('a', 64),
                CatalogueDigest = new string('b', 64), TestBundleDigest = new string('c', 64),
                ProductArtifactDigest = new string('d', 64), ConfigurationDigest = new string('e', 64),
                PrimaryConfigurationDigest = new string('1', 64), TestConfigurationDigest = new string('2', 64),
                ProviderCode = "sqlite", ProviderVersion = "10.0", CaseIds = ImmutableArray.Create("case.fixture"),
                ExecutedCount = 1, AttemptCount = 1, ResultArtifactDigest = new string('f', 64),
                StartedAt = Now, CompletedAt = Now, IssuedAt = Now, ValidUntil = Now.AddSeconds(30)
            };
            return new(key, database, enrollmentId, keyId, preparation, runtime, receipt);
        }
        public void Dispose() => key.Dispose();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalAcceptance_CannotBeReplayedAfterIssuerRetirementOrMeasurementExpiry(bool expire)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var request = fixture.Request();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        if (expire) database.EvaluationTime = Now.AddSeconds(30);
        else
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationMeasurementIssuerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 6,
                EnrollmentId = fixture.EnrollmentId, ExpectedEnrollmentRevision = 1
            })).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(expire ? 6L : 7L);
    }

    [Test]
    public async Task MissingRetainedAssertion_CannotBeReplacedByOriginalMutationReceipt()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var request = fixture.Request();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationConsumerMeasurementReceipts.Remove(await writer.ModerationConsumerMeasurementReceipts.SingleAsync());
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(6L);
    }

    [Test]
    public async Task AnotherOperation_CannotReplaceAnAlreadyAcceptedChallengeOrRun()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var fixture = await Fixture.CreateAsync(database);
        var first = fixture.Request();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, first)).IsSuccess).IsTrue();
        var another = first with { OperationId = Guid.CreateVersion7(), ExpectedQualificationRevision = 6 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, another)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(6L);
    }
}
