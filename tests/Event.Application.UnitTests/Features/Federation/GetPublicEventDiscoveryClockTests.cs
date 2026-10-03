using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Handlers.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Federation;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Federation;

public sealed class GetPublicEventDiscoveryClockTests
{
    [Test]
    public async Task Local_and_remote_candidates_use_the_trusted_shared_home_instant()
    {
        var capturedNow = new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new DiscoveryClock(capturedNow);
        var local = Substitute.For<IQueryHandler<GetEventListRequest, PaginatedResult<EventListDto>>>();
        local.QueryAsync(Arg.Any<GetEventListRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            clock.Now = capturedNow.AddHours(1);
            return PaginatedResult<EventListDto>.Create([], 0, 1, 10);
        });
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<SettingContext>(),
            Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ResolvedSetting
            {
                Key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Value = "true",
                IsLocked = false
            }
        });
        var projection = Substitute.For<IAtprotoEventProjectionRepository>();
        var queries = new List<AtprotoEventProjectionQuery>();
        projection.GetPublicWindowAsync(Arg.Do<AtprotoEventProjectionQuery>(queries.Add),
            Arg.Any<CancellationToken>()).Returns((Array.Empty<AtprotoEventProjection>(), 0));
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.CreateVersion7());
        var lifecycle = Substitute.For<ITenantLifecycleAccessService>();
        lifecycle.IsPublicAsync(tenant.TenantId, Arg.Any<CancellationToken>()).Returns(true);
        var identities = Substitute.For<IEventDiscoveryIdentityRepository>();
        identities.GetBindingsAsync(tenant.TenantId, EventDiscoverySourceKind.LocalEvent,
            Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EventDiscoveryIdentity>());
        var handler = new GetPublicEventDiscoveryRequestHandler(
            local, projection, new AtprotoEventGovernanceResolver(settings), tenant, clock, lifecycle, identities);
        await handler.QueryAsync(new GetPublicEventDiscoveryRequest(new GetEventListRequest
        {
            PageNumber = 1, PageSize = 10, OperationNow = capturedNow
        }), CancellationToken.None);
        await Assert.That(queries.Single().Now).IsEqualTo(capturedNow);
        await Assert.That(clock.Now).IsEqualTo(capturedNow.AddHours(1));
    }

    private sealed class DiscoveryClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
