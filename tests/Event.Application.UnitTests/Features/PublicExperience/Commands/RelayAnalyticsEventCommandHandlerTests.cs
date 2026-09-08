using System.Text.Json;
using Explore.Application.Analytics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Analytics;
using Explore.Application.Features.PublicExperience.Handlers.Commands;
using Explore.Application.Features.PublicExperience.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.PublicExperience.Commands;

public class RelayAnalyticsEventCommandHandlerTests
{
    [Test]
    public async Task Handle_PageViewRelay_SendsGovernedPageView()
    {
        var tenantContext = Substitute.For<ITenantContext>();
        tenantContext.TenantId.Returns(Guid.Parse("018e4e5c-7f00-7000-8000-000000000301"));

        var analyticsProvider = Substitute.For<IAnalyticsProvider>();
        var analyticsConfigResolver = Substitute.For<IAnalyticsConfigResolver>();
        analyticsConfigResolver.ResolveAsync(Arg.Any<CancellationToken>()).Returns(new AnalyticsConfiguration
        {
            Provider = AnalyticsProviderEnum.Posthog,
            IsEnabled = true,
            ConsentMode = AnalyticsConsentMode.Pseudonymous,
            TransportMode = AnalyticsTransportMode.Relay
        });

        var handler = new RelayAnalyticsEventCommandHandler(
            tenantContext,
            analyticsProvider,
            analyticsConfigResolver,
            new AnalyticsGovernanceService());

        var payload = new RelayAnalyticsEventDto
        {
            EventType = "pageview",
            DistinctId = "browser-session-1",
            PagePath = "/events",
            Properties = new Dictionary<string, JsonElement>
            {
                [AnalyticsEvents.Properties.PageTitle] = JsonDocument.Parse("\"Events\"").RootElement.Clone(),
                ["email"] = JsonDocument.Parse("\"sensitive@example.com\"").RootElement.Clone()
            }
        };

        var result = await handler.Handle(new RelayAnalyticsEventCommand { Payload = payload }, CancellationToken.None);

        await Assert.That(result).IsTrue();
        await analyticsProvider.Received(1).PageViewAsync(
            Arg.Is<string>(x => x.StartsWith("pseudo-")),
            "/events",
            Arg.Is<IDictionary<string, object>?>(props => props != null && props.ContainsKey(AnalyticsEvents.Properties.PageTitle) && !props.ContainsKey("email")),
            Arg.Any<CancellationToken>());
    }
}
