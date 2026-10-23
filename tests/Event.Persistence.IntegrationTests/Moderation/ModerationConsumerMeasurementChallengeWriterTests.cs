using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationConsumerMeasurementChallengeWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task PreparedChallenge_ExactRetryRetainsNonceAndOriginalSourceWithoutActivation()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var (runtime, request) = await PrepareAsync(database);
        var result = await database.ExecuteAsync(database.AdministratorId, request);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var challenge = await reader.ModerationConsumerMeasurementChallenges.SingleAsync();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await Assert.That((await reader.ModerationConsumerMeasurementChallenges.AsNoTracking().SingleAsync()).Nonce)
            .IsEqualTo(challenge.Nonce);
        await Assert.That(challenge.RuntimeGeneration).IsEqualTo(runtime.Generation);
        await Assert.That(challenge.BindingDigest).IsEqualTo(ModerationPolicyPublicationProjection.Digest(request.Binding));
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationPolicySelections.SingleAsync()).ActivePublicationId).IsNull();
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
    public async Task ChangedCoordinatesOrAuthority_CannotCommitAChallengePrefix(int change)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var (runtime, request) = await PrepareAsync(database);
        request = change switch
        {
            0 => request with { InstallationId = Guid.CreateVersion7() },
            1 => request with { ExpectedInstallationRevision = 2 },
            2 => request with { ExpectedRuntimeGeneration = new string('f', 64) },
            3 => request with { Binding = request.Binding with { PublicationId = Guid.CreateVersion7() } },
            4 => request with { Binding = request.Binding with { PolicyRevision = 2 } },
            5 => request with { Binding = request.Binding with { Capability = ModerationCapability.Publish } },
            6 => request with { ValidUntil = Now.AddMinutes(6) },
            _ => request
        };
        if (change == 8) database.QualificationSaved = () => runtime.Generation = new string('e', 64);
        await Assert.That((await database.ExecuteAsync(change == 7 ? database.SubjectId : database.AdministratorId, request))
            .IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementChallenges.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == request.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    internal static async Task<(ModerationConsumerInstallationWriterTests.ControlledRuntime,
        PrepareModerationConsumerMeasurementSource)> PrepareAsync(ModerationQualificationWriterTests.SourceDatabase database,
            string consumerId = "resource.payload")
    {
        var runtime = new ModerationConsumerInstallationWriterTests.ControlledRuntime(database.Inventory.Fingerprint, consumerId);
        database.ConsumerRuntimeSources = [runtime];
        var installed = new InstallModerationConsumerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 1,
            InstallationId = Guid.CreateVersion7(), ConsumerId = runtime.ConsumerId, Mode = runtime.Mode,
            ExpectedRuntimeGeneration = runtime.Generation, ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
            ValidUntil = Now.AddMinutes(5)
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, installed)).IsSuccess).IsTrue();
        var publication = database.Publication(2, ModerationReferenceProfileId.CFG08);
        publication = publication with { Proposed = publication.Proposed with
            { Profiles = [publication.Proposed.Profiles[0] with { ScopeKinds = [ModerationScopeKind.Resource] }] } };
        await Assert.That((await database.PublishAsync(database.AdministratorId, publication)).Outcome)
            .IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        return (runtime, new()
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
            ChallengeId = Guid.CreateVersion7(), InstallationId = installed.InstallationId, ExpectedInstallationRevision = 1,
            ConsumerId = runtime.ConsumerId,
            Binding = new(publication.Proposed.PolicyId, 1, publication.Proposed.PublicationId, database.Scope,
                publication.Proposed.Profiles[0].Rule, ModerationCapability.ReadResource, runtime.Mode),
            ExpectedRuntimeGeneration = runtime.Generation, ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
            ValidUntil = Now.AddMinutes(1)
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WithdrawnSourceOrMissingOriginalReceipt_CannotPrepareMeasurements(bool removeReceipt)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var (_, request) = await PrepareAsync(database);
        if (removeReceipt)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.SourceId == request.InstallationId));
            await writer.SaveChangesAsync();
        }
        else
        {
            var withdrawn = new WithdrawModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 3,
                InstallationId = request.InstallationId, ExpectedInstallationRevision = 1
            };
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, withdrawn)).IsSuccess).IsTrue();
            request = request with { ExpectedQualificationRevision = 4 };
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerMeasurementChallenges.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(removeReceipt ? 3L : 4L);
    }
}
