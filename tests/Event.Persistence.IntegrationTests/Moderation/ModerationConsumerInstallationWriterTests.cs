using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationConsumerInstallationWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeInstallation_ExactRetryWithdrawalAndReplacementPreserveOriginalArtifacts()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var runtime = new ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var source = Install(database, runtime);
        var accepted = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(accepted.IsSuccess).IsTrue();
        var replay = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(replay.QualificationRevision).IsEqualTo(accepted.QualificationRevision);
        var withdrawal = new WithdrawModerationConsumerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            InstallationId = source.InstallationId, ExpectedInstallationRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, withdrawal)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, withdrawal)).IsSuccess).IsTrue();
        runtime.Generation = new string('c', 64);
        var replacement = Install(database, runtime) with { ExpectedQualificationRevision = 3 };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, replacement)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var original = await reader.ModerationConsumerInstallations.SingleAsync(row => row.Id == source.InstallationId);
        await Assert.That(original.RuntimeGeneration).IsEqualTo(source.ExpectedRuntimeGeneration);
        await Assert.That(original.Revision).IsEqualTo(2L);
        await Assert.That(await reader.ModerationConsumerInstallations.CountAsync()).IsEqualTo(2);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationEffectIntents.CountAsync()).IsEqualTo(0);
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
    public async Task UnqualifiedInstallation_NeverCommitsSourceReceiptOrEpochPrefix(int change)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var runtime = new ControlledRuntime(change == 8 ? new string('e', 64) : database.Inventory.Fingerprint,
            change == 4 ? "unknown.consumer" : "resource.payload");
        if (change != 0) database.ConsumerRuntimeSources = [runtime];
        var source = Install(database, runtime);
        source = change switch
        {
            1 => source with { ExpectedRuntimeGeneration = new string('d', 64) },
            2 => source with { ExpectedInventoryFingerprint = new string('e', 64) },
            3 => source with { Mode = ModerationNativeMode.RemoteStrict },
            4 => source with { ConsumerId = "unknown.consumer" },
            5 => source with { ValidUntil = Now },
            8 => source with { ExpectedInventoryFingerprint = new string('e', 64) },
            _ => source
        };
        if (change == 7) database.ExpireGovernanceAfterSave = true;
        await Assert.That((await database.ExecuteAsync(
            change == 6 ? database.SubjectId : database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerInstallations.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == source.OperationId)).IsFalse();
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task RuntimeReplacement_CannotReplayTheEarlierInstallationOrOverwriteALiveSource()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var runtime = new ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var source = Install(database, runtime);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        runtime.Generation = new string('f', 64);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId,
            Install(database, runtime) with { ExpectedQualificationRevision = 2 })).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerInstallations.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    private static InstallModerationConsumerSource Install(
        ModerationQualificationWriterTests.SourceDatabase database, ControlledRuntime runtime) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 1,
        InstallationId = Guid.CreateVersion7(), ConsumerId = runtime.ConsumerId, Mode = ModerationNativeMode.Embedded,
        ExpectedRuntimeGeneration = runtime.Generation, ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
        ValidUntil = Now.AddMinutes(5)
    };

    [Test]
    public async Task ChangedRuntimeAfterFlush_RollsBackSourceReceiptAndEpoch()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var runtime = new ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var source = Install(database, runtime);
        database.QualificationSaved = () => runtime.Generation = new string('d', 64);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerInstallations.CountAsync()).IsEqualTo(0);
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOriginalReceipt_PreventsWithdrawalOrReplacementWithoutRewritingHistory(bool replace)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        var runtime = new ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var source = Install(database, runtime);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.Id == source.OperationId));
            await writer.SaveChangesAsync();
        }
        ModerationQualificationMutation mutation;
        if (replace)
        {
            database.EvaluationTime = Now.AddMinutes(6);
            mutation = Install(database, runtime) with { ExpectedQualificationRevision = 2, ValidUntil = Now.AddMinutes(10) };
        }
        else mutation = new WithdrawModerationConsumerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            InstallationId = source.InstallationId, ExpectedInstallationRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, mutation)).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationConsumerInstallations.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationConsumerInstallations.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    /// <summary>A controlled native host observation; this fixture supplies no conformance verdict.</summary>
    internal sealed class ControlledRuntime(string inventoryFingerprint, string consumerId = "resource.payload")
        : IModerationConsumerRuntimeSource
    {
        public string ConsumerId => consumerId;
        public ModerationNativeMode Mode => ModerationNativeMode.Embedded;
        public string Generation { get; set; } = new('a', 64);
        public string? ProductArtifactDigest { get; set; }
        public ModerationConsumerRuntimeObservation? ReadCurrent() => new(ConsumerId, Mode, Generation, inventoryFingerprint, ProductArtifactDigest);
    }
}
