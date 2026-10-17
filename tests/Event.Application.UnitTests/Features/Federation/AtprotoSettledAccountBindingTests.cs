using Event.Application.UnitTests.Common;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class AtprotoSettledAccountBindingTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private const string OldDid = "did:plc:previous-owner";
    private const string CurrentDid = "did:plc:current-owner";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PublicationCannotReuseSettledRecordFromAnotherDid(bool tombstoned)
    {
        var fixture = Fixture(tombstoned);
        var result = await fixture.Planner.PlanEventAsync(new(
            fixture.Source.TenantId, fixture.UserId, fixture.Source.Id,
            fixture.Source.ConcurrencyStamp,
            tombstoned ? PdsSyncOperation.Create : PdsSyncOperation.Update,
            Guid.CreateVersion7(), Now, RestoreOnly: tombstoned),
            CancellationToken.None);

        await Assert.That(result.Enqueued).IsFalse();
        await Assert.That(result.ReasonCode).IsEqualTo("session_binding_changed");
    }

    [Test]
    public async Task DeliveryRejectsPreviouslyQueuedUpdateWhoseRecordBelongsToAnotherDid()
    {
        var fixture = Fixture(false);
        var outbox = new PdsSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.Source.TenantId,
            UserId = fixture.UserId,
            SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
            SourceEntityId = fixture.Source.Id,
            SourceVersion = fixture.Source.ConcurrencyStamp,
            Did = CurrentDid,
            Collection = AtprotoEventPublicationPlanner.EventCollection,
            RecordKey = fixture.Record.RecordKey,
            AtprotoRecordId = fixture.Record.Id,
            ExpectedCid = fixture.Record.Cid,
            Operation = PdsSyncOperation.Update,
            IdempotencyKey = Guid.CreateVersion7().ToString("N"),
            PdsHost = "https://pds.example/",
            Payload = "{}",
            PayloadHash = "account-switch-payload",
            Status = PdsSyncStatus.Pending,
            CreatedAt = Now
        };
        var result = await fixture.Planner.CheckDeliveryAsync(
            outbox, new DateTimeOffset(Now), CancellationToken.None);

        await Assert.That(result.Allowed).IsFalse();
        await Assert.That(result.ReasonCode).IsEqualTo("ownership_invalid");
    }

    private static (AtprotoEventPublicationPlanner Planner, Explore.Domain.Event Source,
        Guid UserId, AtprotoRecord Record) Fixture(bool tombstoned)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid userId = Guid.CreateVersion7();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.ExternalUnclassified,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Event actor" },
            CreatedAt = Now
        };
        actor.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse(CurrentDid))
        {
            Id = Guid.CreateVersion7(),
            ActorId = actor.Id,
            Actor = actor,
            PdsHost = "https://pds.example/",
            IsActive = true,
            CreatedAt = Now
        });
        var source = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = actor,
            Title = "Public event",
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = Now
        };
        var graph = new AtprotoEventPublicationEntityGraph(
            source, [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);
        var events = Substitute.For<IEventRepository>();
        events.IsPubliclyEligibleAsync(tenantId, source.Id, Arg.Any<CancellationToken>()).Returns(true);
        events.GetAtprotoLifecycleStateAsync(tenantId, source.Id, Arg.Any<CancellationToken>()).Returns(source);
        events.GetAtprotoPublicationGraphAsync(tenantId, source.Id, Arg.Any<CancellationToken>()).Returns(graph);
        var records = Substitute.For<IAtprotoRecordRepository>();
        var payloads = Substitute.For<IAtprotoPublicationPayloadBuilder>();
        payloads.BuildEventAsync(graph, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(AtprotoPublicationPayloadBuildResult.Valid(new("{}", "account-switch-payload")));
        var planner = AtprotoPublicationPlannerTestFactory.ExistingEventDelete(
            tenantId, source.Id, userId, Substitute.For<IPdsSyncOutboxRepository>(),
            events, records, CurrentDid, payloads);
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = OldDid,
            Collection = AtprotoEventPublicationPlanner.EventCollection,
            RecordKey = "previous-account-record",
            Cid = "previous-account-cid",
            Uri = $"at://{OldDid}/{AtprotoEventPublicationPlanner.EventCollection}/previous-account-record",
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            UpdatedAt = Now,
            TombstonedAt = tombstoned ? Now : null
        };
        records.GetOwnedRecordForSourceAsync(
            tenantId, AtprotoEventPublicationPlanner.EventSourceType, source.Id, Arg.Any<CancellationToken>())
            .Returns(new AtprotoOutboundRecordOwnership
            {
                AtprotoRecordId = record.Id,
                AtprotoRecord = record,
                TenantId = tenantId,
                UserId = userId,
                SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
                SourceEntityId = source.Id,
                SourceVersion = source.ConcurrencyStamp
            });
        return (planner, source, userId, record);
    }
}
