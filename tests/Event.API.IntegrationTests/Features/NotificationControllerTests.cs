using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

public sealed class NotificationControllerTests
{
    [Test]
    [Arguments("organization", ResourceKinds.Organization)]
    [Arguments("group", ResourceKinds.Group)]
    public async Task ScopedPreferenceMutationLinksRequireUpdatePermission(
        string scope,
        string expectedResourceKind)
    {
        var resourceId = Guid.NewGuid();
        var policy = new NotificationPreferenceMatrixLinkPolicy();
        var matrix = new NotificationPreferenceMatrixDto
        {
            Scope = scope,
            OrganizationId = scope == "organization" ? resourceId : null,
            GroupId = scope == "group" ? resourceId : null
        };

        var links = policy.GetLinks(matrix, user: null).ToArray();

        foreach (var link in links.Where(link => link.Rel is "save" or "set-mute"))
        {
            await Assert.That(link.PermissionResourceKind).IsEqualTo(expectedResourceKind);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(link.PermissionResourceId).IsEqualTo(resourceId.ToString());
        }
    }

    [Test]
    public async Task Archive_WhenNotificationMissing_ReturnsNotFoundProblemDetails()
    {
        var notificationId = Guid.NewGuid();
        using var mediator = new NotificationMediatorStub(_ => NotFoundFailure("Notification was not found."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Patch,
            $"/api/notification/{notificationId}/archive");

        var response = await client.SendAsync(request);

        using var document = await AssertNotificationNotFoundProblemAsync(response);
        await Assert.That(document.RootElement.GetProperty("detail").GetString()).IsEqualTo("Notification was not found.");

        var command = mediator.LastRequest as ArchiveNotificationCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Id).IsEqualTo(notificationId);
        await Assert.That(command.Archive).IsTrue();
    }

    [Test]
    public async Task Snooze_WhenNotificationMissing_ReturnsNotFoundProblemDetails()
    {
        var notificationId = Guid.NewGuid();
        var snoozedUntil = DateTimeOffset.UtcNow.AddHours(2);
        using var mediator = new NotificationMediatorStub(_ => NotFoundFailure("Notification cannot be snoozed because it was not found."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(
            HttpMethod.Patch,
            $"/api/notification/{notificationId}/snooze?snoozedUntil={Uri.EscapeDataString(snoozedUntil.ToString("O"))}");

        var response = await client.SendAsync(request);

        using var document = await AssertNotificationNotFoundProblemAsync(response);
        await Assert.That(document.RootElement.GetProperty("detail").GetString()).IsEqualTo("Notification cannot be snoozed because it was not found.");

        var command = mediator.LastRequest as SnoozeNotificationCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Id).IsEqualTo(notificationId);
        await Assert.That(command.SnoozedUntil).IsNotNull();
    }

    [Test]
    public async Task GetCurrentUserPreferences_ReturnsHalAffordances()
    {
        using var mediator = new NotificationMediatorStub(_ => new NotificationPreferenceMatrixDto
        {
            TenantId = Guid.Parse("018f0000-0000-7000-8000-000000000010"),
            UserId = Guid.Parse("018f0000-0000-7000-8000-000000000011"),
            Categories =
            [
                new NotificationPreferenceCategoryDto
                {
                    Code = "marketing",
                    Name = "Marketing",
                    IsRequired = false,
                    SortOrder = 90
                }
            ],
            Channels =
            [
                new NotificationPreferenceChannelDto
                {
                    Code = "email",
                    Name = "Email",
                    SortOrder = 10
                }
            ],
            Cells =
            [
                new NotificationPreferenceCellDto
                {
                    CategoryCode = "marketing",
                    ChannelCode = "email",
                    IsEnabled = false,
                    IsEditable = true,
                    EffectiveSourceScope = "Default"
                }
            ]
        });
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/notification/preferences/me");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsNotEmpty();
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("categories").GetArrayLength()).IsEqualTo(1);
        var links = root.GetProperty("_links");
        await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("save", out _)).IsTrue();
        await Assert.That(links.GetProperty("save").GetProperty("method").GetString()).IsEqualTo(HttpMethods.Patch);
        await Assert.That(links.TryGetProperty("set-mute", out _)).IsTrue();
        await Assert.That(links.TryGetProperty("subscribe-web-push", out _)).IsTrue();

        await Assert.That(mediator.LastRequest).IsTypeOf<GetCurrentUserNotificationPreferenceMatrixQuery>();
    }

    [Test]
    public async Task UpdateCurrentUserPreferences_WhenCommandFails_ReturnsValidationProblemDetails()
    {
        using var mediator = new NotificationMediatorStub(_ => BaseCommandResponse.Validation<Guid>(
            ["Category 'account-security' is required and cannot be disabled."],
            "Notification preference update failed."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Patch, "/api/notification/preferences/me");
        request.Content = JsonContent.Create(new UpdateNotificationPreferenceMatrixDto
        {
            Cells =
            [
                new UpdateNotificationPreferenceCellDto
                {
                    CategoryCode = "account-security",
                    ChannelCode = "email",
                    IsEnabled = false
                }
            ]
        });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.BadRequest,
            "Notification preference validation failed");
        var command = mediator.LastRequest as UpdateCurrentUserNotificationPreferenceMatrixCommand;
        await Assert.That(command).IsNotNull();
        await Assert.That(command!.Cells!.Count).IsEqualTo(1);
    }

    [Test]
    public async Task WebPushActions_PreserveAuthClassificationRouteNamesAndProblemMetadata()
    {
        var controller = typeof(Explore.API.Controllers.NotificationController);

        AssertWebPushAction(controller, nameof(Explore.API.Controllers.NotificationController.GetWebPushConfiguration), RouteNames.GetWebPushConfiguration, allowAnonymous: true, EndpointClass.Public, StatusCodes.Status200OK);
        AssertWebPushAction(controller, nameof(Explore.API.Controllers.NotificationController.GetVapidPublicKey), RouteNames.GetVapidPublicKey, allowAnonymous: true, EndpointClass.Public, StatusCodes.Status200OK);
        AssertWebPushAction(controller, nameof(Explore.API.Controllers.NotificationController.GetCurrentUserWebPushSubscription), RouteNames.GetCurrentUserWebPushSubscription, allowAnonymous: false, EndpointClass.Authenticated, StatusCodes.Status200OK);
        AssertWebPushAction(controller, nameof(Explore.API.Controllers.NotificationController.SubscribeCurrentUserWebPushSubscription), RouteNames.SubscribeCurrentUserWebPushSubscription, allowAnonymous: false, EndpointClass.Authenticated, StatusCodes.Status200OK);
        AssertWebPushAction(controller, nameof(Explore.API.Controllers.NotificationController.UnsubscribeCurrentUserWebPushSubscription), RouteNames.UnsubscribeCurrentUserWebPushSubscription, allowAnonymous: false, EndpointClass.Authenticated, StatusCodes.Status200OK);
    }

    [Test]
    public async Task GetWebPushConfiguration_ReturnsOnlyPublicConfigurationFields()
    {
        using var mediator = new NotificationMediatorStub(_ => new WebPushPublicConfiguration(true, "public-key-only"));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/notification/web-push/config");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("enabled").GetBoolean()).IsTrue();
        await Assert.That(document.RootElement.GetProperty("publicKey").GetString()).IsEqualTo("public-key-only");
        await Assert.That(document.RootElement.TryGetProperty("privateKey", out _)).IsFalse();
        await Assert.That(mediator.LastRequest).IsTypeOf<GetWebPushPublicConfigurationQuery>();
    }

    [Test]
    public async Task GetVapidPublicKey_ReturnsAnonymousPlainTextPublicKey()
    {
        using var mediator = new NotificationMediatorStub(_ => new WebPushPublicConfiguration(true, "public-key-only"));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/vapid-public-key");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/plain");
        await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("public-key-only");
        await Assert.That(mediator.LastRequest).IsTypeOf<GetWebPushPublicConfigurationQuery>();
    }

    [Test]
    public async Task GetCurrentUserWebPushSubscription_ReturnsHalResourceWithoutSecretFields()
    {
        var subscriptionId = Guid.NewGuid();
        using var mediator = new NotificationMediatorStub(_ => new WebPushSubscriptionDto
        {
            Id = subscriptionId,
            DeviceIdentifier = "device-a",
            LastSeenAt = DateTime.UtcNow
        });
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/api/notification/web-push/subscription?deviceIdentifier=device-a");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/hal+json"));

        var response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        await Assert.That(root.GetProperty("id").GetGuid()).IsEqualTo(subscriptionId);
        await Assert.That(root.TryGetProperty("endpoint", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("p256Dh", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("auth", out _)).IsFalse();
        await Assert.That(root.GetProperty("_links").TryGetProperty("unsubscribe", out _)).IsTrue();
    }

    [Test]
    public async Task SubscribeCurrentUserWebPushSubscription_WhenCommandFails_ReturnsValidationProblemDetails()
    {
        using var mediator = new NotificationMediatorStub(_ => BaseCommandResponse.Validation<Guid>(
            ["Endpoint is required."],
            "Web Push subscription validation failed."));
        using var factory = CreateFactoryWithMediator(mediator);
        using var client = factory.CreateClient();
        using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/notification/web-push/subscriptions");
        request.Content = JsonContent.Create(new SubscribeCurrentUserWebPushSubscriptionCommand
        {
            DeviceIdentifier = "device-a",
            Endpoint = " ",
            P256Dh = "p256dh",
            Auth = "auth"
        });

        var response = await client.SendAsync(request);

        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.BadRequest, "Web Push subscription validation failed");
    }

    private static WebApplicationFactory<Program> CreateFactoryWithMediator(IMediator mediator)
    {
        var factory = new AuthenticatedWebApplicationFactory();

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMediator>();
                services.AddSingleton(mediator);
            });
        });
    }

    private static HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(Guid.NewGuid()));
        return request;
    }

    private static void AssertWebPushAction(Type controller, string methodName, string routeName, bool allowAnonymous, EndpointClass endpointClass, int successStatus)
    {
        var action = controller.GetMethods().Single(method => method.Name == methodName);
        var httpAttribute = action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single();
        if (httpAttribute.Name != routeName)
            throw new InvalidOperationException($"{methodName} route name mismatch.");

        var classification = action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class
            ?? controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class;
        if (classification != endpointClass)
            throw new InvalidOperationException($"{methodName} endpoint classification mismatch.");

        if ((action.GetCustomAttribute<AllowAnonymousAttribute>() is not null) != allowAnonymous)
            throw new InvalidOperationException($"{methodName} authentication metadata mismatch.");

        var responseTypes = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        if (!responseTypes.Any(attribute => attribute.StatusCode == successStatus))
            throw new InvalidOperationException($"{methodName} success response metadata missing.");

        if (responseTypes.Any(attribute => attribute.StatusCode == StatusCodes.Status401Unauthorized) == allowAnonymous)
            throw new InvalidOperationException($"{methodName} unauthorized response metadata mismatch.");
    }

    private static async Task<System.Text.Json.JsonDocument> AssertNotificationNotFoundProblemAsync(HttpResponseMessage response)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(
            response,
            HttpStatusCode.NotFound,
            "Notification not found");

        var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("notification_not_found");
        return document;
    }

    private static BaseCommandResponse<Guid> NotFoundFailure(string message) =>
        BaseCommandResponse.Failure<Guid>("notification_not_found", message, [message]);

    private sealed class NotificationMediatorStub(Func<object, object> responseFactory) : IMediator, IDisposable
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            object response = responseFactory(request);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            LastRequest = request;
            return Task.CompletedTask;
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<object?>(responseFactory(request));
        }

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
