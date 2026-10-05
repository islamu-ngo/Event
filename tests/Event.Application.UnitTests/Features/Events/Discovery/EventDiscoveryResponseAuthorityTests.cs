using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Discovery;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Events.Discovery;

public sealed class EventDiscoveryResponseAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2040, 6, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Capture_uses_source_boundary_and_one_preparation_instant()
    {
        var fixture = new Fixture();
        DateTimeOffset boundary = Now.AddSeconds(1);
        fixture.Boundaries.GetNextAsync(fixture.TenantId, Now, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture.Clock.Now = boundary;
                return boundary;
            });
        var stamp = await fixture.Authority.CaptureAsync(CancellationToken.None);
        await Assert.That(stamp.ValidUntilUtc).IsEqualTo(boundary);
        await Assert.ThrowsAsync<EventDiscoveryCursorExpiredException>(
            () => fixture.Authority.ValidateAsync(stamp, CancellationToken.None));
    }

    [Test]
    public async Task Final_native_fence_wait_cannot_cross_clock_boundary_and_release()
    {
        var fixture = new Fixture();
        DateTimeOffset boundary = Now.AddSeconds(1);
        fixture.Disclosure.AcquireCurrentAsync(fixture.TenantId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture.Clock.Now = boundary;
                return fixture.Revision;
            });
        var stamp = new EventDiscoveryReadStamp(fixture.TenantId, 0, 0, boundary);
        await Assert.ThrowsAsync<EventDiscoveryCursorExpiredException>(
            () => fixture.Authority.ValidateAsync(stamp, CancellationToken.None));
    }

    [Test]
    public async Task Empty_home_still_expires_when_its_utc_date_criteria_change()
    {
        var fixture = new Fixture();
        fixture.Clock.Now = new DateTimeOffset(2040, 6, 1, 23, 59, 59, TimeSpan.Zero);
        var stamp = await fixture.Authority.CaptureAsync(CancellationToken.None, includeUtcDateBoundary: true);
        await Assert.That(stamp.ValidUntilUtc).IsEqualTo(new DateTimeOffset(2040, 6, 2, 0, 0, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task Timeless_source_requires_no_arbitrary_cache_lifetime()
    {
        var fixture = new Fixture();
        var stamp = await fixture.Authority.CaptureAsync(CancellationToken.None);
        await Assert.That(stamp.ValidUntilUtc).IsEqualTo(DateTimeOffset.MaxValue);
        await fixture.Authority.ValidateAsync(stamp, CancellationToken.None);
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = EventDiscoveryResponseAuthorityTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture
    {
        internal Guid TenantId { get; } = Guid.CreateVersion7();
        internal Clock Clock { get; } = new();
        internal IEventDiscoveryResponseBoundaryRepository Boundaries { get; } =
            Substitute.For<IEventDiscoveryResponseBoundaryRepository>();
        internal IEventDiscoveryDisclosureRepository Disclosure { get; } =
            Substitute.For<IEventDiscoveryDisclosureRepository>();
        internal EventDiscoveryRevision Revision { get; }
        internal EventDiscoveryResponseAuthority Authority { get; }

        internal Fixture()
        {
            Revision = new EventDiscoveryRevision { TenantId = TenantId, Id = Guid.CreateVersion7() };
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(TenantId);
            var identities = Substitute.For<IEventDiscoveryIdentityRepository>();
            identities.GetRevisionAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Revision);
            Disclosure.AcquireCurrentAsync(TenantId, Arg.Any<CancellationToken>()).Returns(Revision);
            var unit = Substitute.For<IUnitOfWork>();
            unit.ExecuteReadCommittedAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var operation = call.Arg<Func<CancellationToken, Task<bool>>>();
                    ArgumentNullException.ThrowIfNull(operation);
                    return operation(call.Arg<CancellationToken>());
                });
            Authority = new(unit, Disclosure, identities, tenant, Clock, Boundaries);
        }
    }
}
