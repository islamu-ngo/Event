using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Helpers;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NotificationControllerTests
{
    [Test]
    public async Task ControllersConsumeOnlyClosedNativePortsForTheNotificationCohort()
    {
        var parameters = new[] { typeof(NotificationController), typeof(GroupController), typeof(OrganizationController) }
            .SelectMany(type => type.GetConstructors().Single().GetParameters()).Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(parameters.Any(type => type.Namespace == "MediatR")).IsFalse();
        var ports = parameters.Where(type => type.IsGenericType
            && (type.GetGenericTypeDefinition() == typeof(IQueryHandler<,>) || type.GetGenericTypeDefinition() == typeof(ICommandHandler<,>))
            && type.GetGenericArguments()[0].Namespace!.StartsWith("Explore.Application.Features.Notifications.", StringComparison.Ordinal)).ToArray();
        await Assert.That(ports.Length).IsEqualTo(21);
    }

    [Test]
    [Arguments("organization", ResourceKinds.Organization)]
    [Arguments("group", ResourceKinds.Group)]
    public async Task ScopedPreferenceMutationLinksRequireUpdatePermission(string scope, string expectedResourceKind)
    {
        var resourceId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var parentOrganizationId = Guid.CreateVersion7();
        var links = new NotificationPreferenceMatrixLinkPolicy().GetLinks(new NotificationPreferenceMatrixDto
        {
            TenantId = tenantId,
            Scope = scope,
            OrganizationId = scope == "organization" ? resourceId : parentOrganizationId,
            GroupId = scope == "group" ? resourceId : null
        }, user: null).Where(link => link.Rel is "save" or "set-mute").ToArray();
        await Assert.That(links.Length).IsEqualTo(2);
        foreach (var link in links)
        {
            await Assert.That(link.PermissionResourceKind).IsEqualTo(expectedResourceKind);
            await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.Update);
            await Assert.That(link.PermissionResourceId).IsEqualTo(resourceId.ToString());
            if (scope == "group")
                await Assert.That(link.PermissionFacts).IsEqualTo(new GroupAuthorizationFacts(tenantId, resourceId, parentOrganizationId));
        }
    }

    [Test]
    public async Task RecipientTenantPagingAndReadArchiveSnoozeDeleteTransitionsRemainIsolated()
    {
        await using var factory = await NotificationHttpFixture.CreateAsync();
        using var owner = factory.Client(factory.UserId);
        using var stranger = factory.Client(factory.StrangerId);
        Guid id = await factory.SeedNotificationAsync(factory.UserId);
        Guid organizationNotification = await factory.SeedNotificationAsync(factory.UserId, scopeId: (int)ActorTypeEnum.Organization);
        Guid strangersNotification = await factory.SeedNotificationAsync(factory.StrangerId);
        await factory.SeedNotificationAsync(factory.UserId, factory.OtherTenantId);
        var list = (await owner.GetFromJsonAsync<PaginatedResult<NotificationListDto>>("/api/notification?pageSize=1"))!;
        await Assert.That(list.TotalCount).IsEqualTo(2);
        await Assert.That(list.Items.Count).IsEqualTo(1);
        await Assert.That((await owner.GetFromJsonAsync<UnreadCountDto>("/api/notification/unread-count"))!.UnreadCount).IsEqualTo(2);
        await Assert.That((await owner.GetFromJsonAsync<UnreadCountDto>("/api/notification/unread-count?notificationScopeId=2"))!.UnreadCount).IsEqualTo(1);
        var detail = (await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}"))!;
        await Assert.That(detail.Body).IsNull();
        await Assert.That(detail.ReadAt).IsNull();
        using (var hidden = await stranger.GetAsync($"/api/notification/{id}"))
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using (var denied = await stranger.PatchAsync($"/api/notification/{id}/archive", null))
            await AssertNotificationNotFoundProblemAsync(denied);
        using (var denied = await stranger.PatchAsync($"/api/notification/{id}/snooze", null))
            await AssertNotificationNotFoundProblemAsync(denied);
        using (var read = await owner.PatchAsync($"/api/notification/{id}/read", null))
            await Assert.That((await read.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.IsSuccess).IsTrue();
        var readAt = (await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}"))!.ReadAt;
        using (var readAgain = await owner.PatchAsync($"/api/notification/{id}/read", null))
            await Assert.That(readAgain.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}"))!.ReadAt).IsEqualTo(readAt);
        using (var archived = await owner.PatchAsync($"/api/notification/{id}/archive", null))
            await Assert.That(archived.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<PaginatedResult<NotificationListDto>>("/api/notification?isArchived=true"))!.Items.Single().Id).IsEqualTo(id);
        using (var unarchived = await owner.PatchAsync($"/api/notification/{id}/archive?archive=false", null))
            await Assert.That(unarchived.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}"))!.ArchivedAt).IsNull();
        using (var snoozed = await owner.PatchAsync($"/api/notification/{id}/snooze?snoozedUntil=2099-01-01T00:00:00Z", null))
            await Assert.That(snoozed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<PaginatedResult<NotificationListDto>>("/api/notification?isSnoozed=true"))!.Items.Single().Id).IsEqualTo(id);
        using (var unsnoozed = await owner.PatchAsync($"/api/notification/{id}/snooze", null))
            await Assert.That(unsnoozed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}"))!.SnoozedUntil).IsNull();
        using (var foreignDelete = await stranger.DeleteAsync($"/api/notification/{id}"))
            await Assert.That(foreignDelete.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{id}")).IsNotNull();
        using (var deleted = await owner.DeleteAsync($"/api/notification/{id}"))
            await Assert.That(deleted.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using (var hidden = await owner.GetAsync($"/api/notification/{id}"))
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        using (var readAll = await owner.PostAsync("/api/notification/read-all", null))
            await Assert.That(readAll.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await owner.GetFromJsonAsync<NotificationDto>($"/api/notification/{organizationNotification}"))!.IsRead).IsTrue();
        await Assert.That((await stranger.GetFromJsonAsync<NotificationDto>($"/api/notification/{strangersNotification}"))!.IsRead).IsFalse();
        await Assert.That((await owner.GetFromJsonAsync<UnreadCountDto>("/api/notification/unread-count"))!.UnreadCount).IsEqualTo(0);
    }

    [Test]
    public async Task ReadAllCutoffDoesNotConsumeFutureArrivals()
    {
        await using var factory = await NotificationHttpFixture.CreateAsync();
        using var client = factory.Client(factory.UserId);
        Guid old = await factory.SeedNotificationAsync(factory.UserId);
        Guid future = await factory.SeedNotificationAsync(factory.UserId);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await db.Notifications.Where(row => row.Id == old).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, new DateTime(2000, 1, 1)));
            await db.Notifications.Where(row => row.Id == future).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, new DateTime(2099, 1, 1)));
        }
        using var response = await client.PostAsync("/api/notification/read-all", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await client.GetFromJsonAsync<NotificationDto>($"/api/notification/{old}"))!.IsRead).IsTrue();
        await Assert.That((await client.GetFromJsonAsync<NotificationDto>($"/api/notification/{future}"))!.IsRead).IsFalse();
    }

    [Test]
    public async Task CurrentUserPreferencesEnforceAtomicValidationPreserveChoicesAndMuteOnlyNonessentialCells()
    {
        await using var factory = await NotificationHttpFixture.CreateAsync();
        using var client = factory.Client(factory.UserId);
        const string path = "/api/notification/preferences/me";
        using (var initial = await client.GetAsync(path))
        {
            using var json = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
            var links = json.RootElement.GetProperty("_links");
            await Assert.That(links.GetProperty("save").GetProperty("method").GetString()).IsEqualTo(HttpMethods.Patch);
            await Assert.That(links.TryGetProperty("self", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("set-mute", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("subscribe-web-push", out _)).IsTrue();
        }
        await PatchAsync(client, path, HttpStatusCode.OK, Cell("marketing", "email", true));
        await PatchAsync(client, path, HttpStatusCode.BadRequest,
            Cell("marketing", "email", false), Cell("account-security", "email", false));
        await PatchAsync(client, path, HttpStatusCode.BadRequest, Cell("unknown", "email", true));
        await PatchAsync(client, path, HttpStatusCode.BadRequest, Cell("marketing", "unknown", true));
        var matrix = (await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!;
        await Assert.That(FindCell(matrix, "marketing", "email").IsEnabled).IsTrue();
        await Assert.That(FindCell(matrix, "marketing", NotificationPreferenceChannelCodes.InApp).IsEnabled).IsFalse();
        await PatchAsync(client, path, HttpStatusCode.OK, Cell("marketing", "email", true));
        using (var muted = await client.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true }))
            await Assert.That(muted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        matrix = (await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!;
        await Assert.That(FindCell(matrix, "marketing", "email").IsEnabled).IsFalse();
        await Assert.That(FindCell(matrix, "marketing", "email").IsMuted).IsTrue();
        await Assert.That(FindCell(matrix, "account-security", "email").IsEnabled).IsTrue();
        using (var unmuted = await client.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = false }))
            await Assert.That(unmuted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(FindCell((await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!, "marketing", "email").IsEnabled).IsTrue();
        using var stranger = factory.Client(factory.StrangerId);
        await Assert.That(FindCell((await stranger.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!, "marketing", "email").IsEnabled).IsFalse();
    }

    [Test]
    [Arguments("organization", false)]
    [Arguments("organization", true)]
    [Arguments("group", false)]
    [Arguments("group", true)]
    public async Task ScopedPreferencesUsePersistedAdminFactsAndRejectForeignTenantAndNonmemberWrites(string scopeName, bool useCerbos)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: useCerbos);
        using var admin = factory.Client(factory.UserId);
        using var stranger = factory.Client(factory.StrangerId);
        using var anonymous = factory.Client();
        Guid id = scopeName == "organization" ? factory.OrganizationId : factory.GroupId;
        Guid foreignId = scopeName == "organization" ? factory.ForeignOrganizationId : factory.ForeignGroupId;
        string path = $"/api/{scopeName}/{id}/notification-preferences";
        using (var anonymousRead = await anonymous.GetAsync(path))
            await Assert.That(anonymousRead.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await PatchAsync(stranger, path, HttpStatusCode.Forbidden, Cell("marketing", "email", true));
        await PatchAsync(admin, $"/api/{scopeName}/{foreignId}/notification-preferences", HttpStatusCode.Forbidden, Cell("marketing", "email", true));
        using (var foreignMute = await admin.PutAsJsonAsync($"/api/{scopeName}/{foreignId}/notification-preferences/mute", new SetNotificationPreferenceMuteDto { IsMuted = true }))
            await Assert.That(foreignMute.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using (var detail = await admin.GetAsync(path))
        {
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var document = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
            var links = document.RootElement.GetProperty("_links");
            await Assert.That(links.TryGetProperty("save", out _)).IsTrue();
            await Assert.That(links.TryGetProperty("set-mute", out _)).IsTrue();
        }
        await PatchAsync(admin, path, HttpStatusCode.OK, Cell("marketing", "email", true));
        await PatchAsync(admin, path, HttpStatusCode.BadRequest, Cell("marketing", "email", false), Cell("account-security", "email", false));
        var matrix = (await admin.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!;
        await Assert.That(matrix.Scope).IsEqualTo(scopeName);
        await Assert.That(matrix.OrganizationId).IsEqualTo(factory.OrganizationId);
        await Assert.That(FindCell(matrix, "marketing", "email").IsEnabled).IsTrue();
        using (var mute = await admin.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true }))
            await Assert.That(mute.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(FindCell((await admin.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!, "marketing", "email").IsMuted).IsTrue();
        using (var unmute = await admin.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = false }))
            await Assert.That(unmute.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(FindCell((await admin.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!, "marketing", "email").IsEnabled).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task GroupAdminWithoutParentOrganizationAuthorityCannotManagePreferences(bool useCerbos)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: useCerbos);
        using var client = factory.Client(factory.GroupAdminOnlyId);
        string path = $"/api/group/{factory.GroupId}/notification-preferences";
        await PatchAsync(client, path, HttpStatusCode.Forbidden, Cell("marketing", "email", true));
        using var mute = await client.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true });
        await Assert.That(mute.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(FindCell((await client.GetFromJsonAsync<NotificationPreferenceMatrixDto>(path))!, "marketing", "email").IsEnabled).IsFalse();
        using var detail = await client.GetAsync(path);
        using var document = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        var links = document.RootElement.GetProperty("_links");
        await Assert.That(links.TryGetProperty("save", out _)).IsFalse();
        await Assert.That(links.TryGetProperty("set-mute", out _)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrForeignGroupParticipationDeniesReadsAndMutations(bool useCerbos)
    {
        await using var factory = await NotificationHttpFixture.CreateAsync(useCerbos: useCerbos);
        using var client = factory.Client(factory.UserId);
        foreach (Guid groupId in new[] { factory.ForeignGroupId, Guid.CreateVersion7() })
        {
            string path = $"/api/group/{groupId}/notification-preferences";
            using var detail = await client.GetAsync(path);
            await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
            await PatchAsync(client, path, HttpStatusCode.Forbidden, Cell("marketing", "email", true));
            using var mute = await client.PutAsJsonAsync(path + "/mute", new SetNotificationPreferenceMuteDto { IsMuted = true });
            await Assert.That(mute.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        }
    }

    [Test]
    public async Task WebPushPublicConfigurationIsAnonymousAndSubscriptionBindsTenantUserDeviceWithoutDisclosingSecrets()
    {
        await using var factory = await NotificationHttpFixture.CreateAsync();
        using var anonymous = factory.Client();
        using (var response = await anonymous.GetAsync("/api/notification/web-push/config"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name)).IsEquivalentTo(new[] { "enabled", "publicKey" });
            await Assert.That(json.RootElement.GetProperty("enabled").GetBoolean()).IsTrue();
            await Assert.That(json.RootElement.GetProperty("publicKey").GetString()).IsEqualTo(factory.PublicKey);
        }
        using (var response = await anonymous.GetAsync("/vapid-public-key"))
        {
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("text/plain");
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo(factory.PublicKey);
        }
        using (var protectedRead = await anonymous.GetAsync("/api/notification/web-push/subscription?deviceIdentifier=browser"))
            await Assert.That(protectedRead.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using var owner = factory.Client(factory.UserId);
        using var stranger = factory.Client(factory.StrangerId);
        var input = new SubscribeCurrentUserWebPushSubscriptionCommand
        {
            DeviceIdentifier = "browser",
            Endpoint = "https://push.example.test/" + Guid.CreateVersion7(),
            P256Dh = Base64Url(65),
            Auth = Base64Url(16)
        };
        var first = await SubscribeAsync(owner, input, HttpStatusCode.OK);
        var replay = await SubscribeAsync(owner, input, HttpStatusCode.OK);
        await Assert.That(replay!.Id).IsEqualTo(first!.Id);
        await SubscribeAsync(stranger, input, HttpStatusCode.BadRequest);
        await SubscribeAsync(owner, input with { DeviceIdentifier = "other-browser" }, HttpStatusCode.BadRequest);
        await SubscribeAsync(owner, input with { Endpoint = "http://push.example.test/insecure" }, HttpStatusCode.BadRequest);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(factory.OtherTenantId);
            var repository = scope.ServiceProvider.GetRequiredService<Explore.Application.Contracts.Persistence.IWebPushSubscriptionRepository>();
            await Assert.That(await repository.GetActiveForDeviceAsync(factory.OtherTenantId, factory.UserId, "browser")).IsNull();
            await Assert.That(await repository.UnsubscribeAsync(factory.OtherTenantId, factory.UserId, first.Id, DateTime.UtcNow)).IsFalse();
        }
        using (var response = await owner.GetAsync("/api/notification/web-push/subscription?deviceIdentifier=browser"))
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            await Assert.That(root.GetProperty("id").GetGuid()).IsEqualTo(first.Id);
            await Assert.That(root.TryGetProperty("endpoint", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("p256Dh", out _)).IsFalse();
            await Assert.That(root.TryGetProperty("auth", out _)).IsFalse();
            await Assert.That(root.GetProperty("_links").TryGetProperty("unsubscribe", out _)).IsTrue();
        }
        using (var hidden = await stranger.GetAsync("/api/notification/web-push/subscription?deviceIdentifier=browser"))
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var denied = await stranger.DeleteAsync($"/api/notification/web-push/subscriptions/{first.Id}"))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        using (var removed = await owner.DeleteAsync($"/api/notification/web-push/subscriptions/{first.Id}"))
            await Assert.That(removed.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var hidden = await owner.GetAsync("/api/notification/web-push/subscription?deviceIdentifier=browser"))
            await Assert.That(hidden.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using (var repeated = await owner.DeleteAsync($"/api/notification/web-push/subscriptions/{first.Id}"))
            await Assert.That(repeated.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That((await SubscribeAsync(owner, input, HttpStatusCode.OK))!.Id).IsNotEqualTo(first.Id);
    }

    [Test]
    public async Task WebPushActionsPreserveAuthClassificationRouteNamesAndProblemMetadata()
    {
        AssertWebPushAction(nameof(NotificationController.GetWebPushConfiguration), RouteNames.GetWebPushConfiguration, true, EndpointClass.Public);
        AssertWebPushAction(nameof(NotificationController.GetVapidPublicKey), RouteNames.GetVapidPublicKey, true, EndpointClass.Public);
        AssertWebPushAction(nameof(NotificationController.GetCurrentUserWebPushSubscription), RouteNames.GetCurrentUserWebPushSubscription, false, EndpointClass.Authenticated);
        AssertWebPushAction(nameof(NotificationController.SubscribeCurrentUserWebPushSubscription), RouteNames.SubscribeCurrentUserWebPushSubscription, false, EndpointClass.Authenticated);
        AssertWebPushAction(nameof(NotificationController.UnsubscribeCurrentUserWebPushSubscription), RouteNames.UnsubscribeCurrentUserWebPushSubscription, false, EndpointClass.Authenticated);
        await Task.CompletedTask;
    }

    private static void AssertWebPushAction(string methodName, string routeName, bool anonymous, EndpointClass endpointClass)
    {
        var controller = typeof(NotificationController);
        var action = controller.GetMethod(methodName)!;
        if (action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single().Name != routeName
            || (action.GetCustomAttribute<EndpointClassificationAttribute>()?.Class ?? controller.GetCustomAttribute<EndpointClassificationAttribute>()?.Class) != endpointClass
            || (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null) != anonymous)
            throw new InvalidOperationException($"{methodName} endpoint metadata changed.");
        var responses = action.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        if (!responses.Any(attribute => attribute.StatusCode == 200)
            || responses.Any(attribute => attribute.StatusCode == 401) == anonymous)
            throw new InvalidOperationException($"{methodName} response metadata changed.");
    }

    private static async Task AssertNotificationNotFoundProblemAsync(HttpResponseMessage response)
    {
        await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, HttpStatusCode.NotFound, "Notification not found");
        using var document = await ProblemDetailsAssertions.ReadAsJsonAsync(response);
        await Assert.That(document.RootElement.GetProperty("code").GetString()).IsEqualTo("notification_not_found");
    }

    private static UpdateNotificationPreferenceCellDto Cell(string category, string channel, bool enabled) => new()
    {
        CategoryCode = category,
        ChannelCode = channel,
        IsEnabled = enabled
    };

    private static NotificationPreferenceCellDto FindCell(NotificationPreferenceMatrixDto matrix, string category, string channel) =>
        matrix.Cells.Single(cell => cell.CategoryCode == category && cell.ChannelCode == channel);

    private static async Task PatchAsync(HttpClient client, string path, HttpStatusCode status, params UpdateNotificationPreferenceCellDto[] cells)
    {
        using var response = await client.PatchAsJsonAsync(path, new UpdateNotificationPreferenceMatrixDto { Cells = cells });
        await Assert.That(response.StatusCode).IsEqualTo(status);
        if (status == HttpStatusCode.BadRequest)
        {
            string mediaType = path.StartsWith("/api/notification/", StringComparison.Ordinal)
                ? "application/problem+json" : "application/json";
            await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo(mediaType);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(400);
            await Assert.That(problem.RootElement.TryGetProperty("traceId", out _)).IsTrue();
            await Assert.That(problem.RootElement.TryGetProperty("timestamp", out _)).IsTrue();
            await Assert.That(problem.RootElement.GetProperty("errors").EnumerateObject().Any()).IsTrue();
        }
    }

    private static async Task<BaseCommandResponse<Guid>?> SubscribeAsync(HttpClient client, SubscribeCurrentUserWebPushSubscriptionCommand command, HttpStatusCode status)
    {
        using var response = await client.PostAsJsonAsync("/api/notification/web-push/subscriptions", command);
        await Assert.That(response.StatusCode).IsEqualTo(status);
        if (status != HttpStatusCode.OK)
        {
            await ProblemDetailsAssertions.AssertProblemDetailsAsync(response, status, "Web Push subscription validation failed");
            return null;
        }
        return await response.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>();
    }

    private static string Base64Url(int count) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(count)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
