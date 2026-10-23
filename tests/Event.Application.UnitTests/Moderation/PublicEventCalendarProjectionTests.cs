using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Moderation;

public sealed class PublicEventCalendarProjectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task MappingAndDeliveryRetainTheOriginalLeaseAndConsumeItOnce()
    {
        var owner = new Owner();
        await using var projection = await Prepare(owner);
        await Assert.That(owner.Request).IsNull();
        var delivered = "";
        var outcome = await projection!.DiscloseAsync(
            (view, _) =>
            {
                if (owner.Lease.Disposed) throw new InvalidOperationException("Mapping escaped native custody.");
                return Task.FromResult(view.Title);
            },
            (title, _) =>
            {
                if (owner.Lease.Disposed) throw new InvalidOperationException("Delivery escaped native custody.");
                delivered = title;
                return Task.CompletedTask;
            });
        await Assert.That(outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.Allowed);
        await Assert.That(delivered).IsEqualTo("Reviewed Event");
        await Assert.That(owner.Request!.TenantId).IsEqualTo(owner.Lease.Event.TenantId);
        await Assert.That(owner.Lease.Disposed).IsTrue();
        await Assert.That(await projection.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException("Consumed projection cannot prepare again."),
            (_, _) => throw new InvalidOperationException("Consumed projection cannot deliver again.")))
            .IsEqualTo(ModerationPublicEventCalendarOutcome.NotFound);
    }

    [Test]
    [Arguments(ModerationPublicEventCalendarOutcome.NotFound)]
    [Arguments(ModerationPublicEventCalendarOutcome.Unavailable)]
    public async Task FailedNativeAcquisitionExposesNoPayload(ModerationPublicEventCalendarOutcome failure)
    {
        var owner = new Owner { Outcome = failure };
        await using var projection = await Prepare(owner);
        await Assert.That(await projection!.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException("Failed acquisition cannot prepare."),
            (_, _) => throw new InvalidOperationException("Failed acquisition cannot deliver."))).IsEqualTo(failure);
    }

    [Test]
    public async Task AllowedStatusWithoutOwnedLeaseIsUnavailable()
    {
        var owner = new Owner { OmitLease = true };
        await using var projection = await Prepare(owner);
        await Assert.That(await projection!.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            .IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
    }

    [Test]
    [Arguments("event")]
    [Arguments("tenant")]
    [Arguments("session-event")]
    [Arguments("session-tenant")]
    public async Task AcquiredEntitySubstitutionCannotPrepare(string defect)
    {
        var owner = new Owner();
        await using var projection = await Prepare(owner);
        if (defect == "event") owner.Lease.Event.Id = Guid.NewGuid();
        if (defect == "tenant") owner.Lease.Event.TenantId = Guid.NewGuid();
        if (defect == "session-event") owner.Lease.PrimarySession.EventId = Guid.NewGuid();
        if (defect == "session-tenant") owner.Lease.PrimarySession.TenantId = Guid.NewGuid();
        await Assert.That(await projection!.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException("Substituted entities cannot prepare."),
            (_, _) => throw new InvalidOperationException())).IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PreparedRepresentationCannotEscapeAChangedOrExactlyExpiredSource(bool expire)
    {
        var owner = new Owner();
        var clock = new Clock();
        await using var projection = await Prepare(owner, clock: clock);
        var outcome = await projection!.DiscloseAsync((view, _) =>
        {
            if (expire) clock.UtcNow = owner.Lease.ValidUntil;
            else owner.Lease.Current = false;
            return Task.FromResult(view.Title);
        }, (_, _) => throw new InvalidOperationException("Stale prepared bytes cannot be delivered."));
        await Assert.That(outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task PublicLocationUsesAnonymousPurposeAndOnlyQualifiedPublicValues()
    {
        var owner = new Owner();
        owner.Lease.PrimarySession.AssignEventLocation(EventLocation.CreateToBeAnnounced(
            owner.Lease.Event.TenantId, owner.Lease.Event.Id, Guid.NewGuid(), Now.UtcDateTime));
        var locations = Substitute.For<IEventLocationDisclosureService>();
        EventLocationDisclosureRequest? observed = null;
        locations.ResolveManyAsync(Arg.Any<IReadOnlyCollection<EventLocationDisclosureRequest>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed = (call.Arg<IReadOnlyCollection<EventLocationDisclosureRequest>>()
                    ?? throw new InvalidOperationException("Public location intent is required.")).Single();
                IReadOnlyDictionary<Guid, EventLocationDisclosureResult> values =
                    new Dictionary<Guid, EventLocationDisclosureResult>
                    {
                        [observed.EventLocationId] = EventLocationDisclosureResult.Public(observed.EventLocationId,
                            EventLocationDisclosureState.Available, new(VenueName: "Reviewed public venue"))
                    };
                return values;
            });
        await using var projection = await Prepare(owner, locations);
        string? location = null;
        await projection!.DiscloseAsync((view, _) =>
        {
            location = view.Location;
            return Task.FromResult(view.Title);
        }, (_, _) => Task.CompletedTask);
        await Assert.That(observed!.RequesterUserId).IsNull();
        await Assert.That(observed.Purpose).IsEqualTo(EventLocationDisclosurePurpose.Public);
        await Assert.That(location).IsEqualTo("Reviewed public venue");
    }

    [Test]
    public async Task LeaseExpiryBetweenRecheckAndPayloadAccessCannotBecomeAnUnhandledExport()
    {
        var owner = new Owner();
        var clock = new Clock();
        await using var projection = await Prepare(owner, clock: clock);
        owner.Lease.OnRevalidate = () =>
        {
            clock.UtcNow = owner.Lease.ValidUntil;
            owner.Lease.ThrowOnPayloadRead = true;
        };
        await Assert.That(await projection!.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException("Expired payload cannot prepare."),
            (_, _) => throw new InvalidOperationException("Expired payload cannot deliver.")))
            .IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task RetriedSerializedCallbackCannotRepeatExternalDelivery()
    {
        var owner = new Owner { ReplayCallback = true };
        await using var projection = await Prepare(owner);
        var deliveries = 0;
        var outcome = await projection!.DiscloseAsync((view, _) => Task.FromResult(view.Title),
            (_, _) => { deliveries++; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(deliveries).IsEqualTo(1);
        await Assert.That(owner.Lease.Disposed).IsTrue();
    }

    [Test]
    public async Task DisposedProjectionCannotAcquireFreshAuthority()
    {
        var owner = new Owner();
        var projection = await Prepare(owner);
        await projection!.DisposeAsync();
        await Assert.That(await projection.DiscloseAsync<string>(
            (_, _) => throw new InvalidOperationException(), (_, _) => throw new InvalidOperationException()))
            .IsEqualTo(ModerationPublicEventCalendarOutcome.NotFound);
        await Assert.That(owner.Request).IsNull();
    }

    private static async Task<PublicEventCalendarProjection?> Prepare(
        Owner owner, IEventLocationDisclosureService? locations = null, Clock? clock = null)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(owner.Lease.Event.TenantId);
        return await new GetEventCalendarExportRequestHandler(owner, tenant,
            locations ?? Substitute.For<IEventLocationDisclosureService>(), clock ?? new Clock())
            .QueryAsync(new(owner.Lease.Event.Id), CancellationToken.None);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = Now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class Owner : IModerationPublicEventCalendarRepository
    {
        public Lease Lease { get; } = new();
        public ModerationPublicEventCalendarRequest? Request { get; private set; }
        public ModerationPublicEventCalendarOutcome Outcome { get; init; } = ModerationPublicEventCalendarOutcome.Allowed;
        public bool OmitLease { get; init; }
        public bool ReplayCallback { get; init; }
        public async Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            var result = await operation(cancellationToken);
            return ReplayCallback ? await operation(cancellationToken) : result;
        }
        public Task<ModerationPublicEventCalendarAcquisition> AcquireAsync(
            ModerationPublicEventCalendarRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new ModerationPublicEventCalendarAcquisition(Outcome,
                Outcome == ModerationPublicEventCalendarOutcome.Allowed && !OmitLease ? Lease : null));
        }
    }

    private sealed class Lease : IModerationPublicEventCalendarLease
    {
        private readonly Explore.Domain.Event value = new(EventStatusEnum.Published)
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            Title = "Reviewed Event",
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            Actor = null!,
            Tenant = null!,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormat = null!
        };
        public Explore.Domain.Event Event => ThrowOnPayloadRead ? throw new ObjectDisposedException(nameof(Lease)) : value;
        public EventSession PrimarySession { get; }
        public DateTimeOffset ValidUntil => Now.AddMinutes(1);
        public CancellationToken ValidityCancellation => CancellationToken.None;
        public bool Current { get; set; } = true;
        public bool Disposed { get; private set; }
        public bool ThrowOnPayloadRead { get; set; }
        public Action? OnRevalidate { get; set; }
        public Lease() => PrimarySession = new()
        {
            Id = Guid.NewGuid(),
            EventId = Event.Id,
            Event = Event,
            TenantId = Event.TenantId,
            Tenant = null!,
            StartTime = Now.AddDays(1),
            EndTime = Now.AddDays(1).AddHours(1)
        };
        public Task<ModerationPublicEventCalendarOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            OnRevalidate?.Invoke();
            return Task.FromResult(Current && !Disposed ? ModerationPublicEventCalendarOutcome.Allowed
                : ModerationPublicEventCalendarOutcome.Unavailable);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
