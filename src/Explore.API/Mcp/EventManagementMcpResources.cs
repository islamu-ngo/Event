using System.ComponentModel;
using System.Text.Json;
using Explore.API.Hateoas;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace Explore.API.Mcp;

[McpServerResourceType]
public sealed class EventManagementMcpResources(
    IMediator mediator,
    IResourceAssembler<EventDto, EventListDto> eventResourceAssembler,
    IHttpContextAccessor httpContextAccessor)
{
    private const int MaxShortTextLength = 500;
    private const int MaxReadinessErrors = 25;

    private static readonly string[] ManagementActionRelations =
    [
        LinkRelations.Edit,
        LinkRelations.Delete,
        LinkRelations.Publish,
        LinkRelations.PublishReadiness,
        LinkRelations.AddSession,
        LinkRelations.SessionCreateContext,
        LinkRelations.ModerateLight,
        LinkRelations.ModerateHeavy,
        LinkRelations.Unmoderate
    ];

    [McpServerResource(
        Name = "event_management_context",
        Title = "Event management context",
        UriTemplate = "islamu-event://events/{eventId}/management-context",
        MimeType = "application/json")]
    [Authorize(Policy = McpAuthorizationPolicies.EventManagementRead)]
    [Description("Read bounded event-management state and HAL-derived action affordances for an authenticated principal.")]
    public async Task<string> GetEventManagementContextAsync(
        [Description("Event identifier whose management context should be read.")]
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        EventDto? eventDto = await mediator.Send(new GetEventManagementDetailsRequest { Id = eventId }, cancellationToken);
        if (eventDto is null)
        {
            return Serialize(EventMcpManagementContextResultDescriptor.NotFound(eventId));
        }

        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HTTP context is required for MCP event HAL capability planning.");

        var halResource = await eventResourceAssembler.ToResource(eventDto, httpContext);
        var publishReadiness = halResource.Links.ContainsKey(LinkRelations.PublishReadiness)
            ? await mediator.Send(new GetEventPublishReadinessRequest { Id = eventDto.Id }, cancellationToken)
            : null;

        var descriptor = new EventMcpManagementContextResultDescriptor(
            Found: true,
            EventId: eventDto.Id,
            FailureCode: null,
            Context: MapContext(eventDto, halResource.Links, publishReadiness));

        return Serialize(descriptor);
    }

    private static EventMcpManagementContextDescriptor MapContext(
        EventDto dto,
        IReadOnlyDictionary<string, HalLink> links,
        EventPublishReadinessDto? publishReadiness)
    {
        var truncatedFields = new List<string>();

        return new EventMcpManagementContextDescriptor(
            dto.Id,
            dto.ConcurrencyStamp,
            TrimToEmpty(dto.Title, MaxShortTextLength, truncatedFields, nameof(dto.Title)),
            TrimToNull(dto.Slug, MaxShortTextLength, truncatedFields, nameof(dto.Slug)),
            TrimToEmpty(dto.EventStatusFullName, MaxShortTextLength, truncatedFields, nameof(dto.EventStatusFullName)),
            TrimToEmpty(dto.VisibilityTypeFullName, MaxShortTextLength, truncatedFields, nameof(dto.VisibilityTypeFullName)),
            TrimToEmpty(dto.EventFormatFullName, MaxShortTextLength, truncatedFields, nameof(dto.EventFormatFullName)),
            links.ContainsKey(LinkRelations.PublishReadiness),
            MapPublishReadiness(publishReadiness, truncatedFields),
            ManagementActionRelations.Select(rel => MapAction(rel, links, truncatedFields)).ToArray(),
            truncatedFields);
    }

    private static EventMcpManagementActionDescriptor MapAction(
        string relation,
        IReadOnlyDictionary<string, HalLink> links,
        ICollection<string> truncatedFields)
    {
        if (!links.TryGetValue(relation, out var link))
        {
            return new EventMcpManagementActionDescriptor(
                relation,
                Available: false,
                Method: null,
                Href: null,
                Title: null);
        }

        return new EventMcpManagementActionDescriptor(
            relation,
            Available: true,
            TrimToNull(link.Method, MaxShortTextLength, truncatedFields, $"{relation}.Method"),
            TrimToNull(link.Href, MaxShortTextLength, truncatedFields, $"{relation}.Href"),
            TrimToNull(link.Title, MaxShortTextLength, truncatedFields, $"{relation}.Title"));
    }

    private static EventMcpPublishReadinessDescriptor? MapPublishReadiness(
        EventPublishReadinessDto? dto,
        ICollection<string> truncatedFields)
        => dto is null
            ? null
            : EventManagementMcpReadinessMapper.Map(
                dto,
                MaxReadinessErrors,
                MaxShortTextLength,
                truncatedFields);

    private static string Serialize(EventMcpManagementContextResultDescriptor descriptor)
        => JsonSerializer.Serialize(
            descriptor,
            EventManagementMcpJsonContext.Default.EventMcpManagementContextResultDescriptor);

    private static string TrimToEmpty(
        string? value,
        int maxLength,
        ICollection<string> truncatedFields,
        string fieldName)
        => TrimToNull(value, maxLength, truncatedFields, fieldName) ?? string.Empty;

    private static string? TrimToNull(
        string? value,
        int maxLength,
        ICollection<string> truncatedFields,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        truncatedFields.Add(fieldName);
        return trimmed[..maxLength];
    }
}
