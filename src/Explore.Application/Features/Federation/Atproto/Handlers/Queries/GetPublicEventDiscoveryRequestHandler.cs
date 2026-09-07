using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Federation;
using Explore.Application.Specifications.Events;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Federation.Atproto.Handlers.Queries;

public sealed class GetPublicEventDiscoveryRequestHandler(
    IRequestHandler<GetEventListRequest, PaginatedResult<EventListDto>> localHandler,
    IAtprotoEventProjectionRepository projectionRepository,
    AtprotoEventGovernanceResolver governanceResolver,
    Explore.Application.Contracts.Infrastructure.ITenantContext tenantContext,
    TimeProvider timeProvider)
    : IRequestHandler<GetPublicEventDiscoveryRequest, PaginatedResult<EventDiscoveryItemDto>>
{
    public async Task<PaginatedResult<EventDiscoveryItemDto>> Handle(
        GetPublicEventDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        var validator = new GetPublicEventDiscoveryRequestValidator();
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        GetPublicEventDiscoveryRequestValidator.TryGetWindow(request, out int window);

        AtprotoEventGovernance governance = await governanceResolver.ResolveAsync(
            tenantContext.TenantId,
            null,
            cancellationToken);

        GetEventListRequest criteria = request.Criteria;
        int requestedPage = criteria.PageNumber;
        int requestedPageSize = criteria.PageSize;
        PaginatedResult<EventListDto> localPage = await localHandler.Handle(
            criteria.CopyWithPagination(1, window),
            cancellationToken);

        var localItems = localPage.Items
            .Take(window)
            .Select(value => MapLocal(value, governance.EventsEnabled))
            .ToList();

        int federatedTotalCount = 0;
        var federatedItems = new List<EventDiscoveryItemDto>();
        if (governance.EventsEnabled && TryCreateProjectionQuery(criteria, window, timeProvider.GetUtcNow(), out var projectionQuery))
        {
            (IReadOnlyList<AtprotoEventProjection> projections, federatedTotalCount) =
                await projectionRepository.GetPublicWindowAsync(projectionQuery, cancellationToken);
            federatedItems.AddRange(projections.Select(MapFederated));

            Guid[] localRecordIds = localItems
                .Where(item => item.Federation is not null)
                .Select(item => item.Federation!.AtprotoRecordId)
                .Distinct()
                .ToArray();
            IReadOnlyList<AtprotoEventProjection> echoes = await projectionRepository
                .GetVisibleByRecordIdsAsync(localRecordIds, cancellationToken);
            HashSet<Guid> sourceAvailable = echoes
                .Where(value => value.SourceUrl is not null)
                .Select(value => value.AtprotoRecordId)
                .ToHashSet();
            foreach (EventDiscoveryItemDto item in localItems.Where(item => item.Federation is not null))
            {
                item.Federation!.HasSourceLink = sourceAvailable.Contains(item.Federation.AtprotoRecordId);
            }
        }

        List<EventDiscoveryItemDto> merged = localItems
            .Concat(federatedItems)
            .GroupBy(StableIdentity)
            .Select(group => group.OrderBy(item => item.Source == "local" ? 0 : 1).First())
            .ToList();
        merged.Sort(CreateComparer(criteria.SortBy, criteria.SortDescending));
        int offset = checked((requestedPage - 1) * requestedPageSize);
        List<EventDiscoveryItemDto> pageItems = merged
            .Skip(offset)
            .Take(requestedPageSize)
            .ToList();

        return PaginatedResult<EventDiscoveryItemDto>.Create(
            pageItems,
            checked(localPage.TotalCount + federatedTotalCount),
            requestedPage,
            requestedPageSize);
    }

    private static EventDiscoveryItemDto MapLocal(EventListDto value, bool includeFederationMetadata) => new()
    {
        Source = "local",
        Event = value,
        Federation = includeFederationMetadata && value.AtprotoRecordId.HasValue
            ? new EventFederationMetadataDto
            {
                AtprotoRecordId = value.AtprotoRecordId.Value,
                Provenance = "local-owned",
                IsLocalEcho = true
            }
            : null
    };

    private static EventDiscoveryItemDto MapFederated(AtprotoEventProjection value) => new()
    {
        Source = "atproto",
        FederatedEvent = new FederatedEventDto
        {
            Id = value.AtprotoRecordId,
            Name = value.Name,
            Description = value.Description,
            CreatedAtUtc = value.CreatedAt,
            StartsAtUtc = value.StartsAt,
            EndsAtUtc = value.EndsAt,
            Mode = value.Mode,
            Status = value.Status,
            RsvpExpected = value.RsvpExpected,
            LocationSummary = value.LocationSummary
        },
        Federation = new EventFederationMetadataDto
        {
            AtprotoRecordId = value.AtprotoRecordId,
            Provenance = "atproto",
            HasSourceLink = value.SourceUrl is not null
        }
    };

    private static bool TryCreateProjectionQuery(
        GetEventListRequest criteria,
        int take,
        DateTimeOffset now,
        out AtprotoEventProjectionQuery query)
    {
        query = null!;
        if (HasUnsupportedFederatedFilter(criteria)
            || !TryMapModes(criteria.FormatIds, out IReadOnlyCollection<string>? modes))
        {
            return false;
        }

        query = new AtprotoEventProjectionQuery(
            take,
            criteria.SearchTerm?.Trim(),
            criteria.DateFrom,
            criteria.DateTo,
            modes,
            MapTemporalFilter(criteria),
            MapSort(criteria.SortBy),
            criteria.SortDescending,
            now);
        return true;
    }

    private static bool HasUnsupportedFederatedFilter(GetEventListRequest value) =>
        value.Id != Guid.Empty
        || value.ActorId.HasValue
        || value.OrganizationId.HasValue
        || value.GroupId.HasValue
        || value.CategoryId.HasValue
        || value.IncludedCategoryIds is { Count: > 0 }
        || value.ExcludedCategoryIds is { Count: > 0 }
        || value.IncludedTagIds is { Count: > 0 }
        || value.ExcludedTagIds is { Count: > 0 }
        || value.MadhabIds is { Count: > 0 }
        || value.LocationIds is { Count: > 0 }
        || value.RegistrationModeIds is { Count: > 0 }
        || value.LanguageIds is { Count: > 0 }
        || value.EventTypeIds is { Count: > 0 }
        || value.AudienceGenderIds is { Count: > 0 }
        || value.AudienceAgeIds is { Count: > 0 }
        || value.EventStatusIds is { Count: > 0 }
        || value.GenderModeIds is { Count: > 0 }
        || value.IncludesQuranRecitation.HasValue
        || value.ReferencePrayerIds is { Count: > 0 }
        || value.IslamicPrimaryLanguageIds is { Count: > 0 }
        || value.HasIslamicAspect.HasValue
        || value.SkillLevelId.HasValue
        || value.IsCodingCompetition.HasValue
        || value.IsHackathon.HasValue
        || value.RequiresLaptop.HasValue
        || !string.IsNullOrWhiteSpace(value.TechStackTag)
        || value.HasTechAspect.HasValue
        || value.CustomPropertyFilters is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(value.CustomPropertySearchTerm);

    private static bool TryMapModes(
        IReadOnlyCollection<int>? formatIds,
        out IReadOnlyCollection<string>? modes)
    {
        modes = null;
        if (formatIds is not { Count: > 0 })
        {
            return true;
        }

        var mapped = new HashSet<string>(StringComparer.Ordinal);
        foreach (int id in formatIds)
        {
            string? mode = id switch
            {
                (int)EventFormatEnum.Local => "inperson",
                (int)EventFormatEnum.Digital => "virtual",
                (int)EventFormatEnum.Hybrid => "hybrid",
                _ => null
            };
            if (mode is null)
            {
                return false;
            }
            mapped.Add(mode);
        }

        modes = mapped;
        return true;
    }

    private static AtprotoEventTemporalFilter MapTemporalFilter(GetEventListRequest value) => value.View switch
    {
        TemporalView.Upcoming => AtprotoEventTemporalFilter.Upcoming,
        TemporalView.Ongoing => AtprotoEventTemporalFilter.Ongoing,
        TemporalView.Past => AtprotoEventTemporalFilter.Past,
        TemporalView.All => AtprotoEventTemporalFilter.All,
        TemporalView.UpcomingAndOngoing => AtprotoEventTemporalFilter.CurrentOrUpcoming,
        _ when value.DateFrom.HasValue || value.DateTo.HasValue => AtprotoEventTemporalFilter.All,
        _ => AtprotoEventTemporalFilter.CurrentOrUpcoming
    };

    private static AtprotoEventDiscoverySort MapSort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "title" => AtprotoEventDiscoverySort.Title,
        "views" => AtprotoEventDiscoverySort.Views,
        "createdat" => AtprotoEventDiscoverySort.CreatedAt,
        _ => AtprotoEventDiscoverySort.Date
    };

    private static Comparison<EventDiscoveryItemDto> CreateComparer(string? sortBy, bool descending)
    {
        AtprotoEventDiscoverySort sort = MapSort(sortBy);
        return (left, right) =>
        {
            int primary = sort switch
            {
                AtprotoEventDiscoverySort.Title => StringComparer.OrdinalIgnoreCase.Compare(Title(left), Title(right)),
                AtprotoEventDiscoverySort.Views => Views(left).CompareTo(Views(right)),
                AtprotoEventDiscoverySort.CreatedAt => CreatedAt(left).CompareTo(CreatedAt(right)),
                _ => Nullable.Compare(StartsAt(left), StartsAt(right))
            };
            if (descending)
            {
                primary = -primary;
            }
            return primary != 0 ? primary : StableIdentity(left).CompareTo(StableIdentity(right));
        };
    }

    private static Guid StableIdentity(EventDiscoveryItemDto value) =>
        value.Federation?.AtprotoRecordId ?? value.Event?.Id ?? value.FederatedEvent?.Id ?? Guid.Empty;

    private static string Title(EventDiscoveryItemDto value) =>
        value.Event?.Title ?? value.FederatedEvent?.Name ?? string.Empty;

    private static int Views(EventDiscoveryItemDto value) => value.Event?.TotalViews ?? 0;

    private static DateTimeOffset CreatedAt(EventDiscoveryItemDto value) =>
        value.Event?.CreatedAtUtc ?? value.FederatedEvent?.CreatedAtUtc ?? DateTimeOffset.MinValue;

    private static DateTimeOffset? StartsAt(EventDiscoveryItemDto value) =>
        value.Event?.FirstSessionStartUtc ?? value.FederatedEvent?.StartsAtUtc;
}
