using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventAgendaItem;
using Explore.Application.DTOs.EventCustomProperty;
using Explore.Application.DTOs.EventDay;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventRoleAssignment;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.EventSessionGroup;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.DTOs.Location;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Exceptions;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Application.Features.EventAgendaItems.Requests.Queries;
using Explore.Application.Features.EventCustomProperties.Requests.Queries;
using Explore.Application.Features.EventDays.Requests.Queries;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.EventRoleAssignments.Requests.Queries;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessionGroups.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.EventSessionTemplates.Requests.Queries;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Features.EventTemplates.Requests.Queries;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using static Explore.API.Mcp.EventMcpBounds;
using static Explore.API.Mcp.EventMcpDescriptorMappers;
using EventSessionTemplateDiffDto = Explore.Application.DTOs.EventSessionTemplateSync.TemplateDiffDto;
using EventSessionTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventSessionTemplateSync.EventSessionTemplateSyncHistoryItemDto;
using EventTemplateDiffDto = Explore.Application.DTOs.EventTemplateSync.TemplateDiffDto;
using EventTemplateSyncHistoryItemDto = Explore.Application.DTOs.EventTemplateSync.EventTemplateSyncHistoryItemDto;

namespace Explore.API.Mcp;

[McpServerToolType]
public sealed class EventManagementMcpTools(
    IMediator mediator,
    IUserContext userContext,
    ITenantContext tenantContext,
    IResourceAssembler<EventDto, EventListDto> eventResourceAssembler,
    IHttpContextAccessor httpContextAccessor,
    EventMcpLocationDisclosureGuard locationDisclosureGuard)
{

    [McpServerTool(
        Name = "search_public_events",
        Title = "Search public events",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [AllowAnonymous]
    [Description("Search published public ISLAMU events with bounded pagination. Draft, archived, private, and members-only events are not returned.")]
    public async Task<string> SearchPublicEventsAsync(
        [Description("Optional free-text search across public event titles and descriptions. Values longer than 120 characters are truncated.")]
        string? searchTerm = null,
        [Description("One-based page number. Values below 1 are treated as 1.")]
        int pageNumber = 1,
        [Description("Requested page size. Values are capped at 25 for MCP public discovery.")]
        int pageSize = DefaultPublicEventPageSize,
        [Description("Optional sort field: date, title, views, or createdAt. Unknown values use the API default.")]
        string? sortBy = null,
        [Description("True sorts descending; false sorts ascending.")]
        bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall("search_public_events", projected: false);

        try
        {
            var normalizedPageNumber = Math.Max(1, pageNumber);
            var normalizedPageSize = pageSize <= 0
                ? DefaultPublicEventPageSize
                : Math.Clamp(pageSize, 1, MaxPublicEventPageSize);
            var pageSizeWasClamped = normalizedPageSize != pageSize;

            var result = await mediator.Send(
                new GetEventListRequest
                {
                    PageNumber = normalizedPageNumber,
                    PageSize = normalizedPageSize,
                    SearchTerm = NormalizeSearchTerm(searchTerm),
                    SortBy = NormalizeSortBy(sortBy),
                    SortDescending = sortDescending
                },
                cancellationToken);

            var descriptor = new EventMcpSearchResultDescriptor(
                result.PageNumber,
                result.PageSize,
                pageSizeWasClamped,
                result.TotalCount,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage,
                result.Items.Select(MapSummary).ToArray());

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "search_public_events",
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpSearchResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "search_public_events",
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "search_public_events",
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "get_public_event",
        Title = "Get public event",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [AllowAnonymous]
    [Description("Read bounded public details for a published public event. Hidden, draft, archived, private, or missing events return a safe not-found descriptor.")]
    public async Task<string> GetPublicEventAsync(
        [Description("Event identifier to read.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall("get_public_event", projected: false);

        try
        {
            var eventDto = await GetPublicEventOrNullAsync(eventId, cancellationToken);
            var descriptor = eventDto is null
                ? EventMcpEventResultDescriptor.NotFound(eventId)
                : new EventMcpEventResultDescriptor(
                    Found: true,
                    EventId: eventDto.Id,
                    FailureCode: null,
                    Event: MapDetail(eventDto));

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "get_public_event",
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpEventResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "get_public_event",
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                "get_public_event",
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "get_public_event_program_summary",
        Title = "Get public event program summary",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [AllowAnonymous]
    [Description("Read a bounded public program summary for a published public event. Hidden, draft, archived, private, or missing events return a safe not-found descriptor.")]
    public async Task<string> GetPublicEventProgramSummaryAsync(
        [Description("Event identifier whose public program summary should be read.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        const string toolName = "get_public_event_program_summary";
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var publicEvent = await GetPublicEventOrNullAsync(eventId, cancellationToken);
            var summary = publicEvent is null
                ? null
                : await mediator.Send(new GetEventProgramSummaryRequest(eventId), cancellationToken);
            EventMcpProgramResultDescriptor descriptor;
            if (summary is null)
            {
                descriptor = EventMcpProgramResultDescriptor.NotFound(eventId);
            }
            else
            {
                locationDisclosureGuard.EnforcePublicProgramLocationDisclosureCeiling(summary);
                descriptor = new EventMcpProgramResultDescriptor(
                    Found: true,
                    EventId: summary.EventId,
                    FailureCode: null,
                    Program: MapProgram(summary));
            }

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpProgramResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "list_public_event_sessions",
        Title = "List public event sessions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [AllowAnonymous]
    [Description("List bounded public sessions for a published public event. Hidden, draft, archived, private, or missing events return a safe not-found descriptor.")]
    public async Task<string> ListPublicEventSessionsAsync(
        [Description("Event identifier whose public sessions should be listed.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        const string toolName = "list_public_event_sessions";
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var publicEvent = await GetPublicEventOrNullAsync(eventId, cancellationToken);
            var sessions = publicEvent is null
                ? null
                : await mediator.Send(new GetSessionsByEventRequest { EventId = eventId }, cancellationToken);
            EventMcpSessionListResultDescriptor descriptor;
            if (sessions is null)
            {
                descriptor = EventMcpSessionListResultDescriptor.NotFound(eventId);
            }
            else
            {
                locationDisclosureGuard.EnforcePublicSessionLocationDisclosureCeiling(sessions);
                descriptor = MapSessions(eventId, sessions);
            }

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpSessionListResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "list_my_events",
        Title = "List my events",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("List bounded event summaries managed by the authenticated principal. Requires MCP read authorization and never accepts a caller-supplied user id.")]
    public async Task<string> ListMyEventsAsync(
        [Description("One-based page number. Values below 1 are treated as 1.")]
        int pageNumber = 1,
        [Description("Requested page size. Values are capped at 25 for MCP management reads.")]
        int pageSize = DefaultMyEventsPageSize,
        CancellationToken cancellationToken = default)
    {
        const string toolName = "list_my_events";
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var userId = userContext.GetRequiredUserId();
            var normalizedPageNumber = Math.Max(1, pageNumber);
            var normalizedPageSize = pageSize <= 0
                ? DefaultMyEventsPageSize
                : Math.Clamp(pageSize, 1, MaxMyEventsPageSize);
            var pageSizeWasClamped = normalizedPageSize != pageSize;

            var result = await mediator.Send(
                new GetMyEventsRequest
                {
                    UserId = userId.ToString(),
                    PageNumber = normalizedPageNumber,
                    PageSize = normalizedPageSize
                },
                cancellationToken);

            var descriptor = new EventMcpMyEventsResultDescriptor(
                result.PageNumber,
                result.PageSize,
                pageSizeWasClamped,
                result.TotalCount,
                result.TotalPages,
                result.HasPreviousPage,
                result.HasNextPage,
                result.Items.Select(MapSummary).ToArray());

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpMyEventsResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            McpAdapterTelemetry.MarkFailure(activity, "unauthorized");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unauthorized");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "get_event_creation_context",
        Title = "Get event creation context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read the authenticated principal's tenant event-creation policy and server-issued publisher options. Requires MCP read authorization and does not inspect roles or claims in MCP.")]
    public async Task<string> GetEventCreationContextAsync(CancellationToken cancellationToken = default)
    {
        const string toolName = "get_event_creation_context";
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var context = await mediator.Send(new GetEventCreationContextRequest(), cancellationToken);
            var descriptor = MapCreationContext(context);

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpCreationContextDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            McpAdapterTelemetry.MarkFailure(activity, "unauthorized");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unauthorized");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "get_event_publish_readiness",
        Title = "Get event publish readiness",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded publish-readiness errors for a managed draft event. Requires MCP read authorization and the REST HAL publish-readiness affordance.")]
    public async Task<string> GetEventPublishReadinessAsync(
        [Description("Event identifier whose publish readiness should be read.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        const string toolName = "get_event_publish_readiness";
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var descriptor = await BuildPublishReadinessResultAsync(eventId, cancellationToken);

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(
                descriptor,
                EventManagementMcpJsonContext.Default.EventMcpPublishReadinessResultDescriptor);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            McpAdapterTelemetry.MarkFailure(activity, "unauthorized");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unauthorized");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    [McpServerTool(
        Name = "get_event_program_management_context",
        Title = "Get event program management context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded organizer program context for sessions, program sections, days, and agenda items. Requires MCP read authorization and the REST HAL edit affordance for the event.")]
    public Task<string> GetEventProgramManagementContextAsync(
        [Description("Event identifier whose organizer program context should be read.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_program_management_context",
            token => BuildProgramManagementContextAsync(eventId, token),
            EventManagementMcpJsonContext.Default.EventMcpProgramManagementResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_custom_properties_context",
        Title = "Get event custom properties context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-local custom property definitions and values. Requires MCP read authorization and the REST HAL edit affordance for the event.")]
    public Task<string> GetEventCustomPropertiesContextAsync(
        [Description("Event identifier whose custom property context should be read.")]
        Guid eventId,
        [Description("One-based definition page number. Values below 1 are treated as 1.")]
        int pageNumber = 1,
        [Description("Requested definition page size. Values are capped at 25 for MCP management reads.")]
        int pageSize = DefaultManagementPageSize,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_custom_properties_context",
            token => BuildCustomPropertiesContextAsync(eventId, pageNumber, pageSize, token),
            EventManagementMcpJsonContext.Default.EventMcpCustomPropertiesResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_registrations_context",
        Title = "Get event registrations context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read a bounded event-scoped registration-order management page. Requires MCP read authorization and the REST HAL order affordance for the event.")]
    public Task<string> GetEventRegistrationsContextAsync(
        [Description("Event identifier whose registration context should be read.")]
        Guid eventId,
        [Description("One-based registration page number. Values below 1 are treated as 1.")]
        int pageNumber = 1,
        [Description("Requested registration page size. Values are capped at 100 for MCP management reads.")]
        int pageSize = DefaultManagementPageSize,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_registrations_context",
            token => BuildRegistrationsContextAsync(eventId, pageNumber, pageSize, token),
            EventManagementMcpJsonContext.Default.EventMcpRegistrationsContextResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_team_context",
        Title = "Get event team context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-team members, current caller permissions, and assignable role presets. Requires MCP read authorization and the REST HAL team affordance for the event.")]
    public Task<string> GetEventTeamContextAsync(
        [Description("Event identifier whose team context should be read.")]
        Guid eventId,
        [Description("True includes inactive assignments when the underlying team query authorizes them.")]
        bool includeInactive = false,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_team_context",
            token => BuildTeamContextAsync(eventId, includeInactive, token),
            EventManagementMcpJsonContext.Default.EventMcpTeamContextResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_template_catalog_context",
        Title = "Get event template catalog context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-template catalog entries and, optionally, session templates for a selected event template. Requires MCP read authorization and the REST HAL edit affordance for the event.")]
    public Task<string> GetEventTemplateCatalogContextAsync(
        [Description("Event identifier used to gate template catalog discovery through the event's REST HAL edit affordance.")]
        Guid eventId,
        [Description("Optional event-type lookup id. When omitted, the event's own EventTypeId is used if present.")]
        int? eventTypeId = null,
        [Description("Optional parent event template id whose session templates should be listed.")]
        Guid? eventTemplateId = null,
        [Description("One-based template page number. Values below 1 are treated as 1.")]
        int pageNumber = 1,
        [Description("Requested template page size. Values are capped at 25 for MCP management reads.")]
        int pageSize = DefaultManagementPageSize,
        [Description("One-based session-template page number when eventTemplateId is supplied.")]
        int sessionTemplatePageNumber = 1,
        [Description("Requested session-template page size. Values are capped at 25 for MCP management reads.")]
        int sessionTemplatePageSize = DefaultManagementPageSize,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_template_catalog_context",
            token => BuildTemplateCatalogContextAsync(
                eventId,
                eventTypeId,
                eventTemplateId,
                pageNumber,
                pageSize,
                sessionTemplatePageNumber,
                sessionTemplatePageSize,
                token),
            EventManagementMcpJsonContext.Default.EventMcpTemplateCatalogResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_template_sync_context",
        Title = "Get event template sync context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-template sync diff and history context. Requires MCP read authorization and the REST HAL edit affordance for the event.")]
    public Task<string> GetEventTemplateSyncContextAsync(
        [Description("Event identifier whose template sync context should be read.")]
        Guid eventId,
        [Description("Optional target template version for diff computation. Omit to read history only.")]
        int? targetTemplateVersion = null,
        [Description("One-based sync history page number. Values below 1 are treated as 1.")]
        int historyPageNumber = 1,
        [Description("Requested sync history page size. Values are capped at 25 for MCP management reads.")]
        int historyPageSize = DefaultManagementPageSize,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_template_sync_context",
            token => BuildEventTemplateSyncContextAsync(
                eventId,
                targetTemplateVersion,
                historyPageNumber,
                historyPageSize,
                token),
            EventManagementMcpJsonContext.Default.EventMcpTemplateSyncContextResultDescriptor,
            cancellationToken);

    [McpServerTool(
        Name = "get_event_session_template_sync_context",
        Title = "Get event session template sync context",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false)]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-session template sync diff and history context. Requires MCP read authorization, the parent event id, and the REST HAL edit affordance for that event.")]
    public Task<string> GetEventSessionTemplateSyncContextAsync(
        [Description("Parent event identifier used to gate the session sync read through the event's REST HAL edit affordance.")]
        Guid eventId,
        [Description("Event session identifier whose template sync context should be read.")]
        Guid sessionId,
        [Description("Optional target session-template version for diff computation. Omit to read history only.")]
        int? targetTemplateVersion = null,
        [Description("One-based sync history page number. Values below 1 are treated as 1.")]
        int historyPageNumber = 1,
        [Description("Requested sync history page size. Values are capped at 25 for MCP management reads.")]
        int historyPageSize = DefaultManagementPageSize,
        CancellationToken cancellationToken = default)
        => ExecuteManagementReadToolAsync(
            "get_event_session_template_sync_context",
            token => BuildEventSessionTemplateSyncContextAsync(
                eventId,
                sessionId,
                targetTemplateVersion,
                historyPageNumber,
                historyPageSize,
                token),
            EventManagementMcpJsonContext.Default.EventMcpSessionTemplateSyncContextResultDescriptor,
            cancellationToken);

    private async Task<EventMcpPublishReadinessResultDescriptor> BuildPublishReadinessResultAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var eventDto = await mediator.Send(new GetEventManagementDetailsRequest { Id = eventId }, cancellationToken);
        if (eventDto is null)
        {
            return EventMcpPublishReadinessResultDescriptor.NotFound(eventId);
        }

        var links = await GetEventHalLinksAsync(eventDto);
        if (!links.ContainsKey(LinkRelations.PublishReadiness))
        {
            return EventMcpPublishReadinessResultDescriptor.Unavailable(eventDto.Id);
        }

        var readiness = await mediator.Send(
            new GetEventPublishReadinessRequest { Id = eventDto.Id },
            cancellationToken);
        if (readiness is null)
        {
            return EventMcpPublishReadinessResultDescriptor.NotFound(eventDto.Id);
        }

        var truncatedFields = new List<string>();
        var readinessDescriptor = EventManagementMcpReadinessMapper.Map(
            readiness,
            MaxPublishReadinessErrors,
            MaxShortTextLength,
            truncatedFields);

        return new EventMcpPublishReadinessResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            PublishReadiness: readinessDescriptor,
            TruncatedFields: truncatedFields);
    }

    private async Task<string> ExecuteManagementReadToolAsync<TDescriptor>(
        string toolName,
        Func<CancellationToken, Task<TDescriptor>> buildDescriptor,
        JsonTypeInfo<TDescriptor> jsonTypeInfo,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = McpAdapterTelemetry.StartToolCall(toolName, projected: false);

        try
        {
            var descriptor = await buildDescriptor(cancellationToken);

            McpAdapterTelemetry.MarkSuccess(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "succeeded");

            return JsonSerializer.Serialize(descriptor, jsonTypeInfo);
        }
        catch (OperationCanceledException)
        {
            McpAdapterTelemetry.MarkCancelled(activity);
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "cancelled",
                failureCode: "cancelled");
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            McpAdapterTelemetry.MarkFailure(activity, "unauthorized");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unauthorized");
            throw;
        }
        catch
        {
            McpAdapterTelemetry.MarkFailure(activity, "unknown");
            McpAdapterTelemetry.RecordToolCall(
                Stopwatch.GetElapsedTime(startedAt),
                toolName,
                projected: false,
                outcome: "failed",
                failureCode: "unknown");
            throw;
        }
    }

    private async Task<EventMcpProgramManagementResultDescriptor> BuildProgramManagementContextAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpProgramManagementResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpProgramManagementResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var sessions = await mediator.Send(new GetManagedSessionsByEventRequest { EventId = eventDto.Id }, cancellationToken);
        var sessionGroups = await mediator.Send(
            new GetManagedEventSessionGroupsByEventRequest { EventId = eventDto.Id },
            cancellationToken);
        var days = await mediator.Send(
            new GetManagedEventDaysByEventRequest { EventId = eventDto.Id },
            cancellationToken);
        var agendaItems = await mediator.Send(
            new GetManagedEventAgendaItemsByEventRequest { EventId = eventDto.Id },
            cancellationToken);

        locationDisclosureGuard.EnforceManagedProgramLocationDisclosureCeiling(sessions, sessionGroups);

        return new EventMcpProgramManagementResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: MapProgramManagementContext(eventDto, sessions, sessionGroups, days, agendaItems));
    }

    private async Task<EventMcpCustomPropertiesResultDescriptor> BuildCustomPropertiesContextAsync(
        Guid eventId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpCustomPropertiesResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpCustomPropertiesResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var (normalizedPageNumber, normalizedPageSize, pageSizeWasClamped) =
            NormalizeManagementPage(pageNumber, pageSize, MaxCustomPropertyDefinitions);

        var definitions = await mediator.Send(
            new GetEventCustomPropertyDefinitionListRequest
            {
                EventId = eventDto.Id,
                PageNumber = normalizedPageNumber,
                PageSize = normalizedPageSize
            },
            cancellationToken);
        var values = await mediator.Send(new GetEventCustomPropertyValuesRequest { EventId = eventDto.Id }, cancellationToken);

        var truncatedFields = new List<string>();
        var returnedValues = values
            .Take(MaxCustomPropertyValues)
            .Select(value => MapCustomPropertyValue(value, truncatedFields))
            .ToArray();
        if (values.Count > returnedValues.Length)
        {
            truncatedFields.Add("Values");
        }

        var context = new EventMcpCustomPropertiesContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            definitions.PageNumber,
            definitions.PageSize,
            pageSizeWasClamped,
            definitions.TotalCount,
            definitions.TotalPages,
            definitions.HasPreviousPage,
            definitions.HasNextPage,
            definitions.Items.Count,
            definitions.Items.Select(definition => MapCustomPropertyDefinition(definition, truncatedFields)).ToArray(),
            values.Count,
            returnedValues.Length,
            values.Count > returnedValues.Length,
            returnedValues,
            truncatedFields);

        return new EventMcpCustomPropertiesResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpTeamContextResultDescriptor> BuildTeamContextAsync(
        Guid eventId,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Team, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpTeamContextResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpTeamContextResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var userId = userContext.GetRequiredUserId();
        var teamMembers = await mediator.Send(
            new GetEventTeamListRequest
            {
                TenantId = tenantContext.TenantId,
                EventId = eventDto.Id,
                IncludeInactive = includeInactive
            },
            cancellationToken);
        var permissions = await mediator.Send(
            new GetCurrentUserEventPermissionsRequest
            {
                TenantId = tenantContext.TenantId,
                EventId = eventDto.Id,
                UserId = userId
            },
            cancellationToken);
        var assignablePresets = await mediator.Send(
            new GetAssignableEventRolePresetsRequest
            {
                TenantId = tenantContext.TenantId,
                EventId = eventDto.Id,
                AssignerUserId = userId
            },
            cancellationToken);

        var truncatedFields = new List<string>();
        var returnedMembers = teamMembers
            .Take(MaxTeamMembers)
            .Select(member => MapTeamMember(member, truncatedFields))
            .ToArray();
        var returnedPresets = assignablePresets
            .Take(MaxAssignableRolePresets)
            .Select(preset => MapRolePreset(preset, truncatedFields))
            .ToArray();

        if (teamMembers.Count > returnedMembers.Length)
        {
            truncatedFields.Add("TeamMembers");
        }

        if (assignablePresets.Count > returnedPresets.Length)
        {
            truncatedFields.Add("AssignableRolePresets");
        }

        var context = new EventMcpTeamContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            includeInactive,
            MapCurrentUserPermissions(permissions, truncatedFields),
            teamMembers.Count,
            returnedMembers.Length,
            teamMembers.Count > returnedMembers.Length,
            returnedMembers,
            assignablePresets.Count,
            returnedPresets.Length,
            assignablePresets.Count > returnedPresets.Length,
            returnedPresets,
            truncatedFields);

        return new EventMcpTeamContextResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpRegistrationsContextResultDescriptor> BuildRegistrationsContextAsync(
        Guid eventId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpRegistrationsContextResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpRegistrationsContextResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var (normalizedPageNumber, normalizedPageSize, pageSizeWasClamped) =
            NormalizeManagementPage(pageNumber, pageSize, MaxManagedRegistrations);
        IReadOnlyList<RegistrationOrderDto> orders = await mediator.Send(
            new GetEventRegistrationOrdersQuery(eventDto.Id),
            cancellationToken);
        int totalOrderCount = orders.Count;
        int totalPages = totalOrderCount == 0
            ? 0
            : (int)Math.Ceiling(totalOrderCount / (double)normalizedPageSize);
        RegistrationOrderDto[] page = orders
            .Skip((normalizedPageNumber - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArray();

        var truncatedFields = new List<string>();
        var context = new EventMcpRegistrationsContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            normalizedPageNumber,
            normalizedPageSize,
            pageSizeWasClamped,
            totalOrderCount,
            totalPages,
            normalizedPageNumber > 1 && totalPages > 0,
            normalizedPageNumber < totalPages,
            page.Select(order => MapRegistrationOrder(order, truncatedFields)).ToArray(),
            truncatedFields);

        return new EventMcpRegistrationsContextResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpTemplateCatalogResultDescriptor> BuildTemplateCatalogContextAsync(
        Guid eventId,
        int? eventTypeId,
        Guid? eventTemplateId,
        int pageNumber,
        int pageSize,
        int sessionTemplatePageNumber,
        int sessionTemplatePageSize,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpTemplateCatalogResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpTemplateCatalogResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var (normalizedPageNumber, normalizedPageSize, pageSizeWasClamped) =
            NormalizeManagementPage(pageNumber, pageSize, MaxTemplateCatalogItems);
        var templates = await mediator.Send(
            new GetEventTemplateListRequest
            {
                EventTypeId = eventTypeId ?? eventDto.EventTypeId,
                PageNumber = normalizedPageNumber,
                PageSize = normalizedPageSize
            },
            cancellationToken);

        EventMcpSessionTemplateCatalogPageDescriptor? sessionTemplates = null;
        if (eventTemplateId.HasValue)
        {
            var (normalizedSessionPageNumber, normalizedSessionPageSize, sessionPageSizeWasClamped) =
                NormalizeManagementPage(sessionTemplatePageNumber, sessionTemplatePageSize, MaxTemplateCatalogItems);
            var sessionTemplatePage = await mediator.Send(
                new GetEventSessionTemplateListRequest
                {
                    EventTemplateId = eventTemplateId.Value,
                    PageNumber = normalizedSessionPageNumber,
                    PageSize = normalizedSessionPageSize
                },
                cancellationToken);

            sessionTemplates = new EventMcpSessionTemplateCatalogPageDescriptor(
                eventTemplateId.Value,
                sessionTemplatePage.PageNumber,
                sessionTemplatePage.PageSize,
                sessionPageSizeWasClamped,
                sessionTemplatePage.TotalCount,
                sessionTemplatePage.TotalPages,
                sessionTemplatePage.HasPreviousPage,
                sessionTemplatePage.HasNextPage,
                sessionTemplatePage.Items.Select(MapSessionTemplate).ToArray());
        }

        var truncatedFields = new List<string>();
        var context = new EventMcpTemplateCatalogContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            eventTypeId ?? eventDto.EventTypeId,
            templates.PageNumber,
            templates.PageSize,
            pageSizeWasClamped,
            templates.TotalCount,
            templates.TotalPages,
            templates.HasPreviousPage,
            templates.HasNextPage,
            templates.Items.Select(template => MapTemplate(template, truncatedFields)).ToArray(),
            sessionTemplates,
            truncatedFields);

        return new EventMcpTemplateCatalogResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpTemplateSyncContextResultDescriptor> BuildEventTemplateSyncContextAsync(
        Guid eventId,
        int? targetTemplateVersion,
        int historyPageNumber,
        int historyPageSize,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpTemplateSyncContextResultDescriptor.NotFound(eventId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpTemplateSyncContextResultDescriptor.Unavailable(eventId);
        }

        var eventDto = gate.Event!;
        var (normalizedHistoryPageNumber, normalizedHistoryPageSize, historyPageSizeWasClamped) =
            NormalizeManagementPage(historyPageNumber, historyPageSize, MaxSyncHistoryItems);
        var history = await mediator.Send(
            new GetEventTemplateSyncHistoryQuery(eventDto.Id, normalizedHistoryPageNumber, normalizedHistoryPageSize),
            cancellationToken);
        var diffRead = await ReadEventTemplateDiffAsync(eventDto.Id, targetTemplateVersion, cancellationToken);
        var truncatedFields = new List<string>();

        var context = new EventMcpTemplateSyncContextDescriptor(
            eventDto.Id,
            eventDto.ConcurrencyStamp,
            targetTemplateVersion,
            diffRead.IsAvailable,
            diffRead.FailureCode,
            diffRead.Diff,
            MapEventTemplateSyncHistory(history, historyPageSizeWasClamped, truncatedFields),
            truncatedFields);

        return new EventMcpTemplateSyncContextResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpSessionTemplateSyncContextResultDescriptor> BuildEventSessionTemplateSyncContextAsync(
        Guid eventId,
        Guid sessionId,
        int? targetTemplateVersion,
        int historyPageNumber,
        int historyPageSize,
        CancellationToken cancellationToken)
    {
        var gate = await BuildManagementReadGateAsync(eventId, LinkRelations.Edit, cancellationToken);
        if (!gate.Found)
        {
            return EventMcpSessionTemplateSyncContextResultDescriptor.NotFound(eventId, sessionId);
        }

        if (!gate.IsAvailable)
        {
            return EventMcpSessionTemplateSyncContextResultDescriptor.Unavailable(eventId, sessionId);
        }

        var sessions = await mediator.Send(new GetManagedSessionsByEventRequest { EventId = eventId }, cancellationToken);
        if (sessions.All(session => session.Id != sessionId))
        {
            return EventMcpSessionTemplateSyncContextResultDescriptor.NotFound(eventId, sessionId);
        }

        var eventDto = gate.Event!;
        var (normalizedHistoryPageNumber, normalizedHistoryPageSize, historyPageSizeWasClamped) =
            NormalizeManagementPage(historyPageNumber, historyPageSize, MaxSyncHistoryItems);
        var history = await mediator.Send(
            new GetEventSessionTemplateSyncHistoryQuery(sessionId, normalizedHistoryPageNumber, normalizedHistoryPageSize),
            cancellationToken);
        var diffRead = await ReadEventSessionTemplateDiffAsync(sessionId, targetTemplateVersion, cancellationToken);
        var truncatedFields = new List<string>();

        var context = new EventMcpSessionTemplateSyncContextDescriptor(
            eventDto.Id,
            sessionId,
            eventDto.ConcurrencyStamp,
            targetTemplateVersion,
            diffRead.IsAvailable,
            diffRead.FailureCode,
            diffRead.Diff,
            MapEventSessionTemplateSyncHistory(history, historyPageSizeWasClamped, truncatedFields),
            truncatedFields);

        return new EventMcpSessionTemplateSyncContextResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            SessionId: sessionId,
            Available: true,
            FailureCode: null,
            Context: context);
    }

    private async Task<EventMcpManagementReadGate> BuildManagementReadGateAsync(
        Guid eventId,
        string requiredLinkRelation,
        CancellationToken cancellationToken)
    {
        var eventDto = await mediator.Send(new GetEventManagementDetailsRequest { Id = eventId }, cancellationToken);
        if (eventDto is null)
        {
            return EventMcpManagementReadGate.NotFound();
        }

        var links = await GetEventHalLinksAsync(eventDto);
        return links.ContainsKey(requiredLinkRelation)
            ? EventMcpManagementReadGate.ForAvailable(eventDto, links)
            : EventMcpManagementReadGate.Unavailable(eventDto, links);
    }

    private async Task<EventMcpTemplateDiffRead> ReadEventTemplateDiffAsync(
        Guid eventId,
        int? targetTemplateVersion,
        CancellationToken cancellationToken)
    {
        if (!targetTemplateVersion.HasValue)
        {
            return EventMcpTemplateDiffRead.NotRequested();
        }

        if (targetTemplateVersion.Value <= 0)
        {
            return EventMcpTemplateDiffRead.Unavailable("invalid_target_template_version");
        }

        try
        {
            var response = await mediator.Send(
                new GetEventTemplateDiffQuery(eventId, targetTemplateVersion.Value),
                cancellationToken);
            return response.IsSuccess && response.Id is not null
                ? EventMcpTemplateDiffRead.WithDiff(MapTemplateDiff(response.Id))
                : EventMcpTemplateDiffRead.Unavailable(response.FailureCode ?? "not_available");
        }
        catch (NotFoundException)
        {
            return EventMcpTemplateDiffRead.Unavailable("not_found");
        }
    }

    private async Task<EventMcpTemplateDiffRead> ReadEventSessionTemplateDiffAsync(
        Guid sessionId,
        int? targetTemplateVersion,
        CancellationToken cancellationToken)
    {
        if (!targetTemplateVersion.HasValue)
        {
            return EventMcpTemplateDiffRead.NotRequested();
        }

        if (targetTemplateVersion.Value <= 0)
        {
            return EventMcpTemplateDiffRead.Unavailable("invalid_target_template_version");
        }

        try
        {
            var response = await mediator.Send(
                new GetEventSessionTemplateDiffQuery(sessionId, targetTemplateVersion.Value),
                cancellationToken);
            return response.IsSuccess && response.Id is not null
                ? EventMcpTemplateDiffRead.WithDiff(MapTemplateDiff(response.Id))
                : EventMcpTemplateDiffRead.Unavailable(response.FailureCode ?? "not_available");
        }
        catch (NotFoundException)
        {
            return EventMcpTemplateDiffRead.Unavailable("not_found");
        }
    }

    private async Task<IReadOnlyDictionary<string, HalLink>> GetEventHalLinksAsync(EventDto eventDto)
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for MCP event HAL capability planning.");
        var halResource = await eventResourceAssembler.ToResource(eventDto, httpContext);
        return halResource.Links;
    }

    private async Task<EventDto?> GetPublicEventOrNullAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventDto = await mediator.Send(new GetEventDetailsRequest { Id = eventId }, cancellationToken);
        return IsPublishedPublicEvent(eventDto) ? eventDto : null;
    }

    private static bool IsPublishedPublicEvent(EventDto? eventDto)
        => eventDto is not null
            && eventDto.EventStatusId == (int)EventStatusEnum.Published
            && eventDto.VisibilityTypeId == (int)VisibilityTypeEnum.Public;


    private static string? NormalizeSearchTerm(string? searchTerm)
        => TrimToNull(searchTerm, MaxSearchTermLength);

    private static string? NormalizeSortBy(string? sortBy)
    {
        var normalized = TrimToNull(sortBy, MaxShortTextLength)?.ToLowerInvariant();
        return normalized switch
        {
            "date" => "date",
            "title" => "title",
            "views" => "views",
            "createdat" => "createdAt",
            _ => null
        };
    }

    private static (int PageNumber, int PageSize, bool PageSizeWasClamped) NormalizeManagementPage(
        int pageNumber,
        int pageSize,
        int maxPageSize)
    {
        var normalizedPageNumber = Math.Max(1, pageNumber);
        var normalizedPageSize = pageSize <= 0
            ? DefaultManagementPageSize
            : Math.Clamp(pageSize, 1, maxPageSize);
        return (normalizedPageNumber, normalizedPageSize, normalizedPageSize != pageSize);
    }



    private sealed record EventMcpManagementReadGate(
        bool Found,
        bool IsAvailable,
        EventDto? Event,
        IReadOnlyDictionary<string, HalLink> Links)
    {
        public static EventMcpManagementReadGate NotFound()
            => new(false, false, null, new Dictionary<string, HalLink>(StringComparer.Ordinal));

        public static EventMcpManagementReadGate Unavailable(
            EventDto eventDto,
            IReadOnlyDictionary<string, HalLink> links)
            => new(true, false, eventDto, links);

        public static EventMcpManagementReadGate ForAvailable(
            EventDto eventDto,
            IReadOnlyDictionary<string, HalLink> links)
            => new(true, true, eventDto, links);
    }

    private sealed record EventMcpTemplateDiffRead(
        bool IsAvailable,
        string? FailureCode,
        EventMcpTemplateDiffDescriptor? Diff)
    {
        public static EventMcpTemplateDiffRead NotRequested()
            => new(false, "not_requested", null);

        public static EventMcpTemplateDiffRead Unavailable(string failureCode)
            => new(false, failureCode, null);

        public static EventMcpTemplateDiffRead WithDiff(EventMcpTemplateDiffDescriptor diff)
            => new(true, null, diff);
    }
}

