using System.Security.Claims;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Assemblers;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features.Calendar;

public sealed class GuestRegistrationCalendarAffordanceTests
{
    [Test]
    [Arguments(ModerationPublicEventCalendarOutcome.NotFound)]
    [Arguments(ModerationPublicEventCalendarOutcome.Unavailable)]
    [Arguments(ModerationPublicEventCalendarOutcome.Allowed)]
    public async Task PendingProjectionOrClaimedAllowedStatusCannotGrantCalendarLink(ModerationPublicEventCalendarOutcome outcome)
    {
        var eventId = Guid.NewGuid();
        var tenant = Substitute.For<ITenantContext>();
        tenant.TenantId.Returns(Guid.NewGuid());
        var projection = await new GetEventCalendarExportRequestHandler(new Owner(outcome), tenant,
            Substitute.For<IEventLocationDisclosureService>(), TimeProvider.System)
            .QueryAsync(new(eventId), CancellationToken.None);
        var query = Substitute.For<IQueryHandler<GetEventCalendarExportRequest, PublicEventCalendarProjection?>>();
        query.QueryAsync(Arg.Any<GetEventCalendarExportRequest>(), Arg.Any<CancellationToken>()).Returns(projection);
        var eligibility = Substitute.For<IQueryHandler<GetGuestRegistrationCancellationEligibilityQuery, bool?>>();
        eligibility.QueryAsync(Arg.Any<GetGuestRegistrationCancellationEligibilityQuery>(), Arg.Any<CancellationToken>())
            .Returns((bool?)false);
        var generator = Substitute.For<IHateoasLinkGenerator>();
        generator.GenerateLink(Arg.Any<LinkDefinition>(), Arg.Any<HttpContext>())
            .Returns(new HalLink { Href = "/public-capability" });
        var evaluator = Substitute.For<IHateoasAuthorizationEvaluator>();
        evaluator.AreLinksAllowedAsync(Arg.Any<IReadOnlyList<LinkDefinition>>(), Arg.Any<ClaimsPrincipal>(),
            Arg.Any<HttpContext>()).Returns(call =>
                (IReadOnlyList<bool>)Enumerable.Repeat(true,
                    (call.Arg<IReadOnlyList<LinkDefinition>>()
                        ?? throw new InvalidOperationException("HAL definitions are required.")).Count).ToArray());
        using var services = new ServiceCollection().AddSingleton(evaluator).BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "admin")], "test"))
        };
        var assembler = new GuestRegistrationStatusResourceAssembler(generator,
            new GuestRegistrationStatusLinkPolicy(), new GuestRegistrationStatusCollectionLinkPolicy(), query, eligibility);
        var view = await assembler.ToResource(new(eventId, Guid.NewGuid(), 1, 1, DateTime.UnixEpoch,
            null, null, DateTimeOffset.MaxValue), http);
        await Assert.That(view.Links.ContainsKey(LinkRelations.Calendar)).IsFalse();
        await Assert.That(view.Links.ContainsKey(LinkRelations.Self)).IsTrue();
    }

    private sealed class Owner(ModerationPublicEventCalendarOutcome outcome) : IModerationPublicEventCalendarRepository
    {
        public Task<T> ExecuteSerializedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
        public Task<ModerationPublicEventCalendarAcquisition> AcquireAsync(
            ModerationPublicEventCalendarRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModerationPublicEventCalendarAcquisition(outcome));
    }
}
