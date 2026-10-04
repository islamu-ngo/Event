using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Exceptions;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.OpenGraph;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.PublicExperience.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

public sealed class EventOrdinaryResponseClockTests
{
    [Test]
    [Arguments("detail")]
    [Arguments("public-code")]
    [Arguments("home")]
    [Arguments("source")]
    [Arguments("conditional-image")]
    public async Task Clock_transition_during_complete_preparation_emits_no_payload_or_redirect(string surface)
    {
        using var fixture = new Fixture();
        fixture.AfterPreparation = () => fixture.Clock.Now = fixture.Boundary;
        await Assert.ThrowsAsync<EventDiscoveryCursorExpiredException>(() => fixture.ReadAsync(surface));
        await Assert.That(fixture.Context.Response.Body.Length).IsEqualTo(0);
        await Assert.That(fixture.Context.Response.Headers.ContainsKey("Location")).IsFalse();
        await Assert.That(fixture.Context.Response.Headers.ContainsKey("ETag")).IsFalse();
    }

    [Test]
    public async Task Source_revoked_during_resolution_cannot_return_a_redirect()
    {
        using var fixture = new Fixture();
        fixture.AfterPreparation = () => fixture.Current.DisclosureEpoch++;
        await Assert.ThrowsAsync<EventDiscoveryRestartRequiredException>(() => fixture.ReadAsync("source"));
        await Assert.That(fixture.Context.Response.Headers.ContainsKey("Location")).IsFalse();
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2040, 6, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture : IDisposable
    {
        internal Clock Clock { get; } = new();
        internal DateTimeOffset Boundary { get; }
        internal EventDiscoveryRevision Current { get; }
        internal DefaultHttpContext Context { get; } = new();
        internal Action AfterPreparation { get; set; } = () => { };
        private readonly EventController _events;
        private readonly PublicExperienceController _home;

        internal Fixture()
        {
            Guid tenantId = Guid.CreateVersion7();
            Guid eventId = Guid.CreateVersion7();
            Boundary = Clock.Now.AddSeconds(1);
            Current = new EventDiscoveryRevision { Id = Guid.CreateVersion7(), TenantId = tenantId };
            var tenant = Substitute.For<ITenantContext>();
            tenant.TenantId.Returns(tenantId);
            var identities = Substitute.For<IEventDiscoveryIdentityRepository>();
            identities.GetRevisionAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Current);
            var disclosure = Substitute.For<IEventDiscoveryDisclosureRepository>();
            disclosure.AcquireCurrentAsync(tenantId, Arg.Any<CancellationToken>()).Returns(Current);
            var boundaries = Substitute.For<IEventDiscoveryResponseBoundaryRepository>();
            boundaries.GetNextAsync(tenantId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(Boundary);
            var unit = Substitute.For<IUnitOfWork>();
            unit.ExecuteReadCommittedAsync(Arg.Any<Func<CancellationToken, Task<bool>>>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var operation = call.Arg<Func<CancellationToken, Task<bool>>>();
                    ArgumentNullException.ThrowIfNull(operation);
                    return operation(call.Arg<CancellationToken>());
                });
            var authority = new EventDiscoveryResponseAuthority(unit, disclosure, identities, tenant, Clock, boundaries);
            var detail = new EventDto
            {
                Id = eventId, TenantId = tenantId, Title = "Prepared event",
                ActorDisplayName = "Publisher", ActorTypeFullName = "User",
                EventStatusFullName = "Published", EventStatusMasterCode = "Published",
                VisibilityTypeFullName = "Public", VisibilityTypeMasterCode = "Public",
                EventFormatFullName = "Local", EventFormatMasterCode = "Local"
            };
            var details = Substitute.For<IQueryHandler<GetEventDetailsRequest, EventDto?>>();
            details.QueryAsync(Arg.Any<GetEventDetailsRequest>(), Arg.Any<CancellationToken>()).Returns(detail);
            var publicDetails = Substitute.For<IQueryHandler<GetPublicEventDetailsRequest, EventDto?>>();
            publicDetails.QueryAsync(Arg.Any<GetPublicEventDetailsRequest>(), Arg.Any<CancellationToken>()).Returns(detail);
            var detailHal = Substitute.For<IResourceAssembler<EventDto, EventListDto>>();
            detailHal.ToResource(Arg.Any<EventDto>(), Arg.Any<HttpContext>()).Returns(_ =>
            {
                AfterPreparation();
                return new HalResource<EventDto>(detail);
            });
            var source = Substitute.For<IQueryHandler<GetAtprotoEventSourceQuery, string?>>();
            source.QueryAsync(Arg.Any<GetAtprotoEventSourceQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                AfterPreparation();
                return "https://example.test/current-source";
            });
            var image = Substitute.For<IQueryHandler<GetPublicEventOpenGraphImageRequest, EventOpenGraphImageRenderResult?>>();
            image.QueryAsync(Arg.Any<GetPublicEventOpenGraphImageRequest>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                AfterPreparation();
                return new EventOpenGraphImageRenderResult(new byte[] { 1 }, "\"clock-image\"");
            });
            var items = Substitute.For<IResourceAssembler<EventDiscoveryItemDto>>();
            var links = Substitute.For<IHateoasLinkGenerator>();
            _events = new(
                Substitute.For<IQueryHandler<GetMyEventsRequest, PaginatedResult<EventListDto>>>(),
                details, publicDetails, image,
                Substitute.For<IQueryHandler<GetEventDiscoveryTraversalQuery, EventDiscoveryTraversalDto>>(),
                source, detailHal, items, new EventDiscoveryTraversalResourceAssembler(items, links), authority);
            var home = Substitute.For<IQueryHandler<GetHomeDiscoveryQuery, HomeDiscoveryDto>>();
            home.QueryAsync(Arg.Any<GetHomeDiscoveryQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
            {
                AfterPreparation();
                return new HomeDiscoveryDto();
            });
            _home = new(
                Substitute.For<IQueryHandler<GetPublicExperienceSettingsQuery, PublicExperienceSettingsDto>>(),
                Substitute.For<IQueryHandler<GetPublicExperienceShellQuery, PublicExperienceShellDto>>(),
                home, Substitute.For<ILinkPolicy<EventDiscoveryItemDto>>(), links, authority);
            Context.Response.Body = new MemoryStream();
            Context.Request.Headers.IfNoneMatch = "\"clock-image\"";
            _events.ControllerContext = new ControllerContext { HttpContext = Context };
            _home.ControllerContext = new ControllerContext { HttpContext = Context };
        }

        internal async Task ReadAsync(string surface)
        {
            switch (surface)
            {
                case "detail": await _events.GetById(Guid.CreateVersion7()); break;
                case "public-code": await _events.GetByPublicCode("event-code"); break;
                case "home": await _home.GetHomeDiscovery(); break;
                case "source": await _events.GetAtprotoEventSource(Guid.CreateVersion7()); break;
                case "conditional-image": await _events.GetOpenGraphImage("event-code"); break;
                default: throw new ArgumentOutOfRangeException(nameof(surface));
            }
        }

        public void Dispose() => Context.Response.Body.Dispose();
    }
}
