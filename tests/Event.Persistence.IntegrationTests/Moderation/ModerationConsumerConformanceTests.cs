using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Application;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationConsumerMeasurementReceiptObservationTests
{
    [Test]
    [Arguments("native.operations", false)]
    [Arguments("native.operations", true)]
    [Arguments("resource.redirect", false)]
    [Arguments("resource.redirect", true)]
    [Arguments("queued.webhook", false)]
    [Arguments("queued.webhook", true)]
    public async Task NonPayloadConsumer_RequiresItsOwnNativeSignedMeasurement(
        string consumerId, bool removeMeasurementSource)
    {
        await using var database = await SourceDatabase.CreateAsync();
        using var catalogue = await DeployedCatalogue.CreateAsync(database, "exact", consumerId);
        using var fixture = await Fixture.CreateAsync(database, catalogue.Artifact, consumerId: consumerId);
        var clock = new ObservationClock();
        await using var context = database.Context();
        var privacy = database.Privacy.Authority(context, clock);
        var participant = database.Participant(context, database.SubjectId, clock);
        var operation = ModerationOperation.Transition(ModerationOperationKind.RecordResponse, fixture.Decision.Subject,
            database.Scope, Guid.CreateVersion7(), Guid.CreateVersion7(), fixture.Decision.Id, 1, 1, Guid.Empty);
        var store = new ModerationStore(context, participant, clock, privacy,
            ModerationLedgerPersistenceTests.Capture(context, privacy, participant, clock));
        var entered = false;
        await store.ExecuteAsync(operation, async (session, token) =>
        {
            entered = true;
            await Assert.That((await participant.ResolveAsync(session, operation, token)).Outcome)
                .IsEqualTo(ModerationOperationOutcome.Known);
            using var evidence = await BorrowPublicationEvidenceAsync(database, context, fixture, clock, token);
            await RecordActualPrimaryDatabaseAsync(evidence, token);
            var measurement = await evidence.AcquireMeasurementAsync(fixture.Receipt.Binding, consumerId, Now, token);
            await Assert.That(measurement).IsNotNull();
            await Assert.That(measurement!.Receipt.ConsumerId).IsEqualTo(consumerId);
            await Assert.That(await evidence.AcquireMeasurementAsync(
                fixture.Receipt.Binding, "resource.payload", Now, token)).IsNull();
            if (removeMeasurementSource)
            {
                context.ModerationQualificationMutationReceipts.Remove(
                    await context.ModerationQualificationMutationReceipts.SingleAsync(
                        row => row.SourceId == fixture.Receipt.ReceiptId, token));
                await context.SaveChangesAsync(token);
            }
            var registration = await new ModerationConsumerConformanceReader(database.Inventory,
                new ModerationMeasurementCatalogueSource(), clock).ReadAsync(evidence, fixture.Runtime,
                context.Database.CurrentTransaction!.TransactionId, fixture.Receipt.Binding, Now, token);
            if (removeMeasurementSource) await Assert.That(registration).IsNull();
            else
            {
                await Assert.That(registration).IsNotNull();
                await Assert.That(registration!.Contract).IsEqualTo(database.Inventory.Consumers[consumerId].Contract);
                await Assert.That(registration.SourceId).IsEqualTo(fixture.Receipt.InstallationId);
                await Assert.That(registration.Conformance).IsEqualTo(ModerationNativeConformance.Passed);
            }
            return ModerationOperationResult.Unavailable();
        }, CancellationToken.None);
        await Assert.That(entered).IsTrue();
        await AssertOriginalCustodyAsync(database, fixture);
    }
}
