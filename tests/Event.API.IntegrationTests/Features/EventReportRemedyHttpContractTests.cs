namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Policies;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

public sealed class EventReportRemedyHttpContractTests
{
    [Test]
    [Arguments(
        RouteNames.SubmitEventCorrection,
        "/api/event-reports/corrections",
        EventReportReasonCodePolicy.EventCorrectionSuggestionSubcategory)]
    [Arguments(
        RouteNames.SubmitUnsafeExternalLinkReport,
        "/api/event-reports/unsafe-external-links",
        EventReportReasonCodePolicy.UnsafeExternalLinkSubcategory)]
    [Arguments(
        RouteNames.SubmitLegalOrCopyrightComplaint,
        "/api/event-reports/legal-or-copyright-complaints",
        EventReportReasonCodePolicy.LegalOrCopyrightComplaintSubcategory)]
    public async Task AuthenticatedRoute_DispatchesServerOwnedRemedyChannel(
        string routeName,
        string expectedPath,
        string expectedSubcategory)
    {
        var eventRepository = Substitute.For<IEventRepository>();
        using WebApplicationFactory<Program> factory = CreateFactory(eventRepository);
        using HttpClient client = factory.CreateClient();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var reporter = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var @event = new EventBuilder()
            .WithTenantId(reporter.TenantId)
            .WithActorId(reporter.ActorId)
            .WithStatus(EventStatusEnum.Published)
            .Build();
        db.Events.Add(@event);
        await db.SaveChangesAsync();
        eventRepository.GetById(@event.Id).Returns(@event);
        eventRepository.IsPubliclyEligibleAsync(reporter.TenantId, @event.Id, Arg.Any<CancellationToken>())
            .Returns(true);
        IHateoasLinkGenerator linkGenerator = scope.ServiceProvider
            .GetRequiredService<IHateoasLinkGenerator>();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
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
                EventId = @event.Id,
                ReasonCode = "other",
                ReporterText = "Please review this event.",
                ReportCaseUpdatesConsent = true,
                ReportFollowUpContactConsent = false
            })
        };
        request.Headers.Add(
            TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(reporter.UserId));

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var report = await db.EventReports.AsNoTracking().SingleAsync();
        await Assert.That(report.EventId).IsEqualTo(@event.Id);
        await Assert.That(report.ReporterUserId).IsEqualTo(reporter.UserId);
        await Assert.That(report.SubcategoryCode).IsEqualTo(expectedSubcategory);
    }

    [Test]
    [Arguments("corrections")]
    [Arguments("unsafe-external-links")]
    [Arguments("legal-or-copyright-complaints")]
    public async Task UnauthenticatedRoute_ReturnsUnauthorizedWithoutDispatch(
        string route)
    {
        var eventRepository = Substitute.For<IEventRepository>();
        using WebApplicationFactory<Program> factory = CreateFactory(eventRepository);
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
        await eventRepository.DidNotReceive().GetById(Arg.Any<Guid>());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IEventRepository eventRepository)
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
                services.RemoveAll<IEventRepository>();
                services.AddSingleton(eventRepository);
                services.RemoveAll<ISettingMutationLock>();
                services.AddSingleton<ISettingMutationLock, ImmediateSettingMutationLock>();
                services.RemoveAll<IRecipientNotificationMaterializer>();
                services.AddSingleton(Substitute.For<IRecipientNotificationMaterializer>());
            });
        });
    }

    // In-memory persistence has no relational locks; execute every protected handler callback.
    private sealed class ImmediateSettingMutationLock : ISettingMutationLock
    {
        public Task<T> ExecuteAsync<T>(string canonicalSettingKey,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);

        public Task<T> ExecuteManyAsync<T>(IEnumerable<string> canonicalSettingKeys,
            Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            operation(cancellationToken);
    }
}
