namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Hateoas;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

public sealed class EventReportRemedyHttpContractTests
{
    [Test]
    [Arguments(
        RouteNames.SubmitEventCorrection,
        "/api/event-reports/corrections",
        EventReportSubmissionChannel.Correction)]
    [Arguments(
        RouteNames.SubmitUnsafeExternalLinkReport,
        "/api/event-reports/unsafe-external-links",
        EventReportSubmissionChannel.UnsafeExternalLink)]
    [Arguments(
        RouteNames.SubmitLegalOrCopyrightComplaint,
        "/api/event-reports/legal-or-copyright-complaints",
        EventReportSubmissionChannel.LegalOrCopyright)]
    public async Task AuthenticatedRoute_DispatchesServerOwnedRemedyChannel(
        string routeName,
        string expectedPath,
        EventReportSubmissionChannel expectedChannel)
    {
        IMediator mediator = Substitute.For<IMediator>();
        mediator.Send(
                Arg.Any<SubmitEventReportCommand>(),
                Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Success(
                Guid.CreateVersion7(),
                "Submitted."));
        using WebApplicationFactory<Program> factory = CreateFactory(mediator);
        using HttpClient client = factory.CreateClient();
        IHateoasLinkGenerator linkGenerator = factory.Services
            .GetRequiredService<IHateoasLinkGenerator>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = factory.Services
        };
        httpContext.Request.Scheme = Uri.UriSchemeHttp;
        httpContext.Request.Host = new HostString("localhost");
        string? route = linkGenerator.GeneratePath(
            routeName,
            routeValues: null,
            httpContext);
        await Assert.That(route).IsEqualTo(expectedPath);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            route)
        {
            Content = JsonContent.Create(new SubmitEventReportDto
            {
                EventId = Guid.CreateVersion7(),
                ReasonCode = "other",
                ReporterText = "Please review this event.",
                ReportCaseUpdatesConsent = true,
                ReportFollowUpContactConsent = false
            })
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(Guid.CreateVersion7()));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        await mediator.Received(1).Send(
            Arg.Is<SubmitEventReportCommand>(command =>
                command.SubmissionChannel == expectedChannel),
            Arg.Any<CancellationToken>());
    }

    [Test]
    [Arguments("corrections")]
    [Arguments("unsafe-external-links")]
    [Arguments("legal-or-copyright-complaints")]
    public async Task UnauthenticatedRoute_ReturnsUnauthorizedWithoutDispatch(
        string route)
    {
        IMediator mediator = Substitute.For<IMediator>();
        using WebApplicationFactory<Program> factory = CreateFactory(mediator);
        using HttpClient client = factory.CreateClient();
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/event-reports/{route}")
        {
            Content = JsonContent.Create(new SubmitEventReportDto
            {
                EventId = Guid.CreateVersion7(),
                ReasonCode = "other",
                ReporterText = "Please review this event.",
                ReportCaseUpdatesConsent = true,
                ReportFollowUpContactConsent = false
            })
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode)
            .IsEqualTo(HttpStatusCode.Unauthorized);
        await mediator.DidNotReceive().Send(
            Arg.Any<SubmitEventReportCommand>(),
            Arg.Any<CancellationToken>());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory
        {
            AuthorizationProviderOverride = new StubAuthorizationProvider
            {
                AllowAll = true
            }
        };
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }
}
