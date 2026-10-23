using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationConsumerInstallationObservationTests
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
    public async Task CurrentInstallation_RequiresOriginalReceiptArtifactAndFencedInstaller(int change)
    {
        var (database, _) = await ModerationResourceTargetAdoptionWriterTests.CreateAsync();
        await using var owner = database;
        var runtime = new ModerationConsumerInstallationWriterTests.ControlledRuntime(database.Inventory.Fingerprint);
        database.ConsumerRuntimeSources = [runtime];
        var installed = new InstallModerationConsumerSource
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 5,
            InstallationId = Guid.CreateVersion7(),
            ConsumerId = "resource.payload",
            Mode = ModerationNativeMode.Embedded,
            ExpectedRuntimeGeneration = runtime.Generation,
            ExpectedInventoryFingerprint = database.Inventory.Fingerprint,
            ValidUntil = Now.AddMinutes(5)
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, installed)).IsSuccess).IsTrue();
        ModerationDecision original;
        ModerationPolicyPublication publication;
        await using (var reader = database.Context())
        {
            reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
            original = await reader.ModerationDecisions.SingleAsync();
            publication = await reader.ModerationPolicyPublications.SingleAsync();
        }
        if (change == 1)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, new WithdrawModerationConsumerSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = database.Scope,
                ExpectedQualificationRevision = 6,
                InstallationId = installed.InstallationId,
                ExpectedInstallationRevision = 1
            })).IsSuccess).IsTrue();
        if (change == 2) runtime.Generation = new string('e', 64);
        if (change is 3 or 4)
        {
            await using var writer = database.Context();
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            if (change == 3)
                writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                    .SingleAsync(row => row.Id == installed.OperationId));
            else
                writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.UrgencyAssessorId));
            await writer.SaveChangesAsync();
        }
        await using var context = database.Context();
        var clock = new ObservationClock { UtcNow = change == 5 ? Now.AddMinutes(5) : Now };
        var privacyAuthority = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, original.Subject,
            database.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), original.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacyAuthority, ModerationLedgerPersistenceTests.Capture(context, privacyAuthority, participant, clock));
        ModerationCapabilitySourceObservation? observed = null;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            var resolution = await participant.ResolveAsync(session, operation, token);
            await Assert.That(resolution.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
            var binding = new ModerationActivationBinding(publication.PolicyId, publication.Revision, publication.Id,
                publication.Jurisdiction, original.Rule, original.Capabilities.Single(), ModerationNativeMode.Embedded);
            observed = await participant.AcquireConsumerInstallationAsync(
                change == 6 ? Guid.CreateVersion7() : context.Database.CurrentTransaction!.TransactionId,
                binding, "resource.payload", clock.UtcNow, token);
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        if (change == 0)
        {
            await Assert.That(observed).IsNotNull();
            await Assert.That(observed!.SourceId).IsEqualTo(installed.InstallationId);
            await Assert.That(observed.Revision).IsEqualTo(1L);
            await Assert.That(observed.WriterGeneration).IsEqualTo(runtime.Generation);
            await Assert.That(observed.ObservedAt).IsEqualTo(Now);
            await Assert.That(observed.ValidUntil).IsEqualTo(Now.AddMinutes(5));
        }
        else await Assert.That(observed).IsNull();
    }

    private sealed class ObservationClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
