using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Notification;
using Explore.Application.Features.Notifications.Requests.Commands;
using Explore.Application.Features.Notifications.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public class NotificationController : ControllerBase
{
    private const string NotificationRefreshEventType = "notification-refresh";

    private static readonly ApiNotFoundProblemDescriptor NotificationNotFoundProblem = new(
        "Notification not found",
        "Notification was not found.",
        "notification_not_found");

    private static readonly ApiValidationProblemDescriptor PreferenceValidationProblem = new(
        "notificationPreferences",
        "Notification preference validation failed",
        "Notification preference update failed.");

    private static readonly ApiValidationProblemDescriptor WebPushValidationProblem = new(
        "webPushSubscription",
        "Web Push subscription validation failed",
        "Web Push subscription update failed.");

    private static readonly ApiNotFoundProblemDescriptor WebPushSubscriptionNotFoundProblem = new(
        "Web Push subscription not found",
        "Web Push subscription was not found.",
        "web_push_subscription_not_found");

    private readonly IQueryHandler<GetUserNotificationsQuery, PaginatedResult<NotificationListDto>> _notifications;
    private readonly IQueryHandler<GetNotificationByIdQuery, NotificationDto?> _notification;
    private readonly IQueryHandler<GetUnreadCountQuery, UnreadCountDto> _unreadCount;
    private readonly IQueryHandler<GetCurrentUserNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixDto> _preferences;
    private readonly IQueryHandler<GetWebPushPublicConfigurationQuery, WebPushPublicConfiguration> _webPushConfiguration;
    private readonly IQueryHandler<GetCurrentUserWebPushSubscriptionQuery, WebPushSubscriptionDto?> _webPushSubscription;
    private readonly ICommandHandler<UpdateCurrentUserNotificationPreferenceMatrixCommand, BaseCommandResponse<Guid>> _updatePreferences;
    private readonly ICommandHandler<SetCurrentUserNotificationPreferenceMuteCommand, BaseCommandResponse<Guid>> _setMute;
    private readonly ICommandHandler<SubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>> _subscribeWebPush;
    private readonly ICommandHandler<UnsubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>> _unsubscribeWebPush;
    private readonly ICommandHandler<MarkNotificationAsReadCommand, BaseCommandResponse<Guid>> _markRead;
    private readonly ICommandHandler<MarkAllNotificationsAsReadCommand, BaseCommandResponse<Guid>> _markAllRead;
    private readonly ICommandHandler<ArchiveNotificationCommand, BaseCommandResponse<Guid>> _archive;
    private readonly ICommandHandler<SnoozeNotificationCommand, BaseCommandResponse<Guid>> _snooze;
    private readonly ICommandHandler<DeleteNotificationCommand, bool> _delete;
    private readonly INotificationRefreshStreamService _notificationRefreshStreamService;
    private readonly IResourceAssembler<NotificationPreferenceMatrixDto> _preferenceAssembler;
    private readonly IResourceAssembler<WebPushSubscriptionDto> _webPushSubscriptionAssembler;

    public NotificationController(
        IQueryHandler<GetUserNotificationsQuery, PaginatedResult<NotificationListDto>> notifications,
        IQueryHandler<GetNotificationByIdQuery, NotificationDto?> notification,
        IQueryHandler<GetUnreadCountQuery, UnreadCountDto> unreadCount,
        IQueryHandler<GetCurrentUserNotificationPreferenceMatrixQuery, NotificationPreferenceMatrixDto> preferences,
        IQueryHandler<GetWebPushPublicConfigurationQuery, WebPushPublicConfiguration> webPushConfiguration,
        IQueryHandler<GetCurrentUserWebPushSubscriptionQuery, WebPushSubscriptionDto?> webPushSubscription,
        ICommandHandler<UpdateCurrentUserNotificationPreferenceMatrixCommand, BaseCommandResponse<Guid>> updatePreferences,
        ICommandHandler<SetCurrentUserNotificationPreferenceMuteCommand, BaseCommandResponse<Guid>> setMute,
        ICommandHandler<SubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>> subscribeWebPush,
        ICommandHandler<UnsubscribeCurrentUserWebPushSubscriptionCommand, BaseCommandResponse<Guid>> unsubscribeWebPush,
        ICommandHandler<MarkNotificationAsReadCommand, BaseCommandResponse<Guid>> markRead,
        ICommandHandler<MarkAllNotificationsAsReadCommand, BaseCommandResponse<Guid>> markAllRead,
        ICommandHandler<ArchiveNotificationCommand, BaseCommandResponse<Guid>> archive,
        ICommandHandler<SnoozeNotificationCommand, BaseCommandResponse<Guid>> snooze,
        ICommandHandler<DeleteNotificationCommand, bool> delete,
        INotificationRefreshStreamService notificationRefreshStreamService,
        IResourceAssembler<NotificationPreferenceMatrixDto> preferenceAssembler,
        IResourceAssembler<WebPushSubscriptionDto> webPushSubscriptionAssembler)
    {
        _notifications = notifications;
        _notification = notification;
        _unreadCount = unreadCount;
        _preferences = preferences;
        _webPushConfiguration = webPushConfiguration;
        _webPushSubscription = webPushSubscription;
        _updatePreferences = updatePreferences;
        _setMute = setMute;
        _subscribeWebPush = subscribeWebPush;
        _unsubscribeWebPush = unsubscribeWebPush;
        _markRead = markRead;
        _markAllRead = markAllRead;
        _archive = archive;
        _snooze = snooze;
        _delete = delete;
        _notificationRefreshStreamService = notificationRefreshStreamService;
        _preferenceAssembler = preferenceAssembler;
        _webPushSubscriptionAssembler = webPushSubscriptionAssembler;
    }

    // GET: api/notification
    [HttpGet(Name = Hateoas.RouteNames.GetNotifications)]
    [EndpointSummary("Get User Notifications")]
    [EndpointDescription("Retrieve a paginated list of notifications for the authenticated user. Supports filtering by read status, notification type, scope, reason, archive state, and snooze state.")]
    [ProducesResponseType(typeof(PaginatedResult<NotificationListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PaginatedResult<NotificationListDto>>> GetAll(
        [FromQuery] NotificationListQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var result = await _notifications.QueryAsync(new GetUserNotificationsQuery
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            IsRead = query.IsRead,
            NotificationTypeId = query.NotificationTypeId,
            NotificationScopeId = query.NotificationScopeId,
            NotificationReasonId = query.NotificationReasonId,
            IsArchived = query.IsArchived,
            IsSnoozed = query.IsSnoozed
        }, cancellationToken);

        return Ok(result);
    }

    // GET: api/notification/{id}
    [HttpGet("{id}", Name = Hateoas.RouteNames.GetNotificationById)]
    [EndpointSummary("Get Notification by ID")]
    [EndpointDescription("Retrieve a specific notification. Returns 404 if not found or doesn't belong to the authenticated user.")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await _notification.QueryAsync(new GetNotificationByIdQuery(id), cancellationToken);

        return Ok(notification);
    }

    // GET: api/notification/unread-count
    [HttpGet("unread-count", Name = Hateoas.RouteNames.GetUnreadNotificationCount)]
    [EndpointSummary("Get Unread Notification Count")]
    [EndpointDescription("Returns the number of unread notifications for the authenticated user. Supports optional scope filter (ActorType: User=1, Organization=2, Group=4, System=5). Optimized with partial index.")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount(
        [FromQuery] int? notificationScopeId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _unreadCount.QueryAsync(new GetUnreadCountQuery { NotificationScopeId = notificationScopeId }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("preferences/me", Name = Hateoas.RouteNames.GetCurrentUserNotificationPreferences)]
    [EndpointSummary("Get Current User Notification Preferences")]
    [EndpointDescription("Returns the authenticated user's effective notification preference matrix with HAL links for allowed actions.")]
    [ProducesResponseType(typeof(HalResource<NotificationPreferenceMatrixDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalResource<NotificationPreferenceMatrixDto>>> GetCurrentUserPreferences(
        CancellationToken cancellationToken = default)
    {
        var matrix = await _preferences.QueryAsync(new GetCurrentUserNotificationPreferenceMatrixQuery(), cancellationToken);
        var resource = await _preferenceAssembler.ToResource(matrix, HttpContext);
        return Ok(resource);
    }

    [HttpPatch("preferences/me", Name = Hateoas.RouteNames.UpdateCurrentUserNotificationPreferences)]
    [EndpointSummary("Update Current User Notification Preferences")]
    [EndpointDescription("Patches supplied notification preference cells for the authenticated user. Omitted cells are preserved; required or locked cells are rejected atomically.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateCurrentUserPreferences(
        [FromBody] UpdateNotificationPreferenceMatrixDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _updatePreferences.ExecuteAsync(new UpdateCurrentUserNotificationPreferenceMatrixCommand
        {
            Cells = request.Cells
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    [HttpPut("preferences/me/mute", Name = Hateoas.RouteNames.SetCurrentUserNotificationPreferenceMute)]
    [EndpointSummary("Set Current User Notification Preference Mute")]
    [EndpointDescription("Sets the authenticated user's non-essential notification mute state without deleting channel choices.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SetCurrentUserPreferenceMute(
        [FromBody] SetNotificationPreferenceMuteDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await _setMute.ExecuteAsync(new SetCurrentUserNotificationPreferenceMuteCommand(request.IsMuted), cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, PreferenceValidationProblem);
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("web-push/config", Name = Hateoas.RouteNames.GetWebPushConfiguration)]
    [EndpointSummary("Get Web Push public configuration")]
    [EndpointDescription("Returns browser-safe Web Push configuration containing only the enabled flag and VAPID public key.")]
    [ProducesResponseType(typeof(WebPushPublicConfiguration), StatusCodes.Status200OK)]
    public async Task<ActionResult<WebPushPublicConfiguration>> GetWebPushConfiguration(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _webPushConfiguration.QueryAsync(new GetWebPushPublicConfigurationQuery(), cancellationToken);
        return Ok(configuration);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("/vapid-public-key", Name = Hateoas.RouteNames.GetVapidPublicKey)]
    [EndpointSummary("Get VAPID public key")]
    [EndpointDescription("Returns only the browser-safe VAPID public key as plain text.")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    public async Task<ActionResult<string>> GetVapidPublicKey(
        CancellationToken cancellationToken = default)
    {
        var configuration = await _webPushConfiguration.QueryAsync(new GetWebPushPublicConfigurationQuery(), cancellationToken);
        return Content(configuration.PublicKey, "text/plain");
    }

    [HttpGet("web-push/subscription", Name = Hateoas.RouteNames.GetCurrentUserWebPushSubscription)]
    [EndpointSummary("Get current user's Web Push subscription")]
    [EndpointDescription("Returns the authenticated user's active Web Push subscription status for one browser device without endpoint or key material.")]
    [ProducesResponseType(typeof(HalResource<WebPushSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<WebPushSubscriptionDto>>> GetCurrentUserWebPushSubscription(
        [FromQuery] string deviceIdentifier,
        CancellationToken cancellationToken = default)
    {
        var subscription = await _webPushSubscription.QueryAsync(new GetCurrentUserWebPushSubscriptionQuery
        {
            DeviceIdentifier = deviceIdentifier
        }, cancellationToken);

        if (subscription is null)
        {
            return this.ToNotFoundProblem(WebPushSubscriptionNotFoundProblem);
        }

        var resource = await _webPushSubscriptionAssembler.ToResource(subscription, HttpContext);
        return Ok(resource);
    }

    [HttpPost("web-push/subscriptions", Name = Hateoas.RouteNames.SubscribeCurrentUserWebPushSubscription)]
    [EndpointSummary("Subscribe current user to Web Push")]
    [EndpointDescription("Registers or refreshes the authenticated user's browser Web Push subscription for one device.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SubscribeCurrentUserWebPushSubscription(
        [FromBody] SubscribeCurrentUserWebPushSubscriptionCommand request,
        CancellationToken cancellationToken = default)
    {
        var response = await _subscribeWebPush.ExecuteAsync(request, cancellationToken);
        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, WebPushValidationProblem);
        }

        return Ok(response);
    }

    [HttpDelete("web-push/subscriptions/{subscriptionId:guid}", Name = Hateoas.RouteNames.UnsubscribeCurrentUserWebPushSubscription)]
    [EndpointSummary("Unsubscribe current user's Web Push subscription")]
    [EndpointDescription("Deactivates one Web Push subscription only when it belongs to the authenticated tenant/user.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UnsubscribeCurrentUserWebPushSubscription(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var response = await _unsubscribeWebPush.ExecuteAsync(new UnsubscribeCurrentUserWebPushSubscriptionCommand(subscriptionId), cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, WebPushValidationProblem);
        }

        return Ok(response);
    }

    // GET: api/notification/stream
    [HttpGet("stream", Name = Hateoas.RouteNames.GetNotificationRefreshStream)]
    [EndpointSummary("Stream Notification Refresh Hints")]
    [EndpointDescription("Streams minimal server-sent notification refresh hints for the authenticated user. The stream is a hint only; persisted notifications and existing APIs remain the source of truth.")]
    [Produces("text/event-stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [DisableRequestTimeout]
    public IResult StreamRefreshHints(CancellationToken cancellationToken = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        return TypedResults.ServerSentEvents(ToSseItems(
            _notificationRefreshStreamService.StreamAsync(cancellationToken),
            cancellationToken));
    }

    // PATCH: api/notification/{id}/read
    [HttpPatch("{id}/read", Name = Hateoas.RouteNames.MarkNotificationAsRead)]
    [EndpointSummary("Mark Notification as Read")]
    [EndpointDescription("Marks a single notification as read. Idempotent — succeeds silently if already read.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> MarkAsRead(Guid id, CancellationToken cancellationToken = default)
    {
        var response = await _markRead.ExecuteAsync(new MarkNotificationAsReadCommand(id), cancellationToken);

        return Ok(response);
    }

    // POST: api/notification/read-all
    [HttpPost("read-all", Name = Hateoas.RouteNames.MarkAllNotificationsAsRead)]
    [EndpointSummary("Mark All Notifications as Read")]
    [EndpointDescription("Bulk marks all unread notifications as read (YouTube-style). Uses timestamp cutoff to prevent race conditions.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> MarkAllAsRead(CancellationToken cancellationToken = default)
    {
        var response = await _markAllRead.ExecuteAsync(new MarkAllNotificationsAsReadCommand(), cancellationToken);
        return Ok(response);
    }

    // PATCH: api/notification/{id}/archive
    [HttpPatch("{id}/archive", Name = Hateoas.RouteNames.ArchiveNotification)]
    [EndpointSummary("Archive Notification")]
    [EndpointDescription("Archives or unarchives a notification. Pass archive=true (default) to archive, archive=false to unarchive. Idempotent.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Archive(
        Guid id, [FromQuery] bool archive = true, CancellationToken cancellationToken = default)
    {
        var response = await _archive.ExecuteAsync(new ArchiveNotificationCommand(id, archive), cancellationToken);
        if (!response.IsSuccess)
            return this.ToNotFoundProblem(NotificationNotFoundProblem, response.Message);

        return Ok(response);
    }

    // PATCH: api/notification/{id}/snooze
    [HttpPatch("{id}/snooze", Name = Hateoas.RouteNames.SnoozeNotification)]
    [EndpointSummary("Snooze Notification")]
    [EndpointDescription("Snoozes a notification until the specified time. Pass snoozedUntil as ISO 8601 datetime. Omit to unsnooze.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Snooze(
        Guid id, [FromQuery] DateTime? snoozedUntil = null, CancellationToken cancellationToken = default)
    {
        var response = await _snooze.ExecuteAsync(new SnoozeNotificationCommand { Id = id, SnoozedUntil = snoozedUntil }, cancellationToken);
        if (!response.IsSuccess)
            return this.ToNotFoundProblem(NotificationNotFoundProblem, response.Message);

        return Ok(response);
    }

    // DELETE: api/notification/{id}
    [HttpDelete("{id}", Name = Hateoas.RouteNames.DeleteNotification)]
    [EndpointSummary("Delete Notification")]
    [EndpointDescription("Soft-deletes a notification for the authenticated user.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        await _delete.ExecuteAsync(new DeleteNotificationCommand(id), cancellationToken);

        return NoContent();
    }

    private static async IAsyncEnumerable<SseItem<NotificationRefreshHintDto>> ToSseItems(
        IAsyncEnumerable<NotificationRefreshHintDto> hints,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var hint in hints.WithCancellation(cancellationToken))
        {
            yield return new SseItem<NotificationRefreshHintDto>(hint, NotificationRefreshEventType)
            {
                EventId = hint.GeneratedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture),
                ReconnectionInterval = TimeSpan.FromSeconds(5)
            };
        }
    }
}
