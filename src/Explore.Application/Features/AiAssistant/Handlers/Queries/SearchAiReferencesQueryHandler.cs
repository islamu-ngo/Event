using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.AiAssistant.Handlers.Queries;

public sealed class SearchAiReferencesQueryHandler(IEventRepository eventRepository, IActorRepository actorRepository)
    : IRequestHandler<SearchAiReferencesQuery, IReadOnlyList<AiReferenceSearchResultDto>>
{
    public const int DefaultLimit = 10;
    public const int MaxLimit = 20;
    private const int MinimumSearchTermLength = 2;
    private const int MaxSummaryLength = 240;
    private const string EventReferenceKind = "Event";
    private const string ActorReferenceKind = "Actor";
    private const string OrganizationReferenceKind = "Organization";

    public async Task<IReadOnlyList<AiReferenceSearchResultDto>> Handle(
        SearchAiReferencesQuery request,
        CancellationToken cancellationToken)
    {
        string searchTerm = request.SearchTerm.Trim();

        if (searchTerm.Length < MinimumSearchTermLength)
        {
            return [];
        }

        int limit = NormalizeLimit(request.Limit);
        IReadOnlyList<Event> events = await eventRepository.SearchAiReferenceEventsAsync(
            searchTerm,
            limit,
            cancellationToken);

        IReadOnlyList<Actor> actors = await actorRepository.SearchAiReferenceActorsAsync(
            searchTerm,
            limit,
            cancellationToken);

        return events
            .Select(MapEvent)
            .Concat(actors.Select(MapActor))
            .OrderBy(ReferenceKindSort)
            .ThenBy(reference => reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reference => reference.ReferenceId)
            .Take(limit)
            .ToList();
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultLimit;
        }

        return Math.Min(limit, MaxLimit);
    }

    private static AiReferenceSearchResultDto MapEvent(Event @event)
    {
        return new AiReferenceSearchResultDto(
            EventReferenceKind,
            @event.Id,
            @event.Title,
            BuildSummary(@event),
            @event.FirstSessionDate,
            @event.LastSessionDate,
            @event.EventStatus?.FullName,
            @event.VisibilityType?.FullName,
            @event.EventFormat?.FullName);
    }

    private static AiReferenceSearchResultDto MapActor(Actor actor)
    {
        var kind = actor.ActorTypeId == (int)ActorTypeEnum.Organization
            ? OrganizationReferenceKind
            : ActorReferenceKind;
        var referenceId = actor.ActorTypeId == (int)ActorTypeEnum.Organization && actor.OrganizationId is Guid organizationId
            ? organizationId
            : actor.Id;

        return new AiReferenceSearchResultDto(
            kind,
            referenceId,
            actor.DisplayName,
            BuildActorSummary(actor),
            null,
            null,
            null,
            null,
            null);
    }

    private static string? BuildSummary(Event @event)
    {
        string? summary = FirstNonBlank(@event.Subtitle, @event.Description);
        return summary is null ? null : Truncate(summary, MaxSummaryLength);
    }

    private static string? BuildActorSummary(Actor actor)
    {
        string? handle = actor.AtprotoIdentities.Select(identity => identity.Handle).FirstOrDefault();
        string? summary = FirstNonBlank(actor.Description, handle is null ? null : $"@{handle}");
        return summary is null ? null : Truncate(summary, MaxSummaryLength);
    }

    private static int ReferenceKindSort(AiReferenceSearchResultDto reference)
    {
        return reference.Kind switch
        {
            EventReferenceKind => 0,
            OrganizationReferenceKind => 1,
            ActorReferenceKind => 2,
            _ => 3
        };
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd();
    }
}
