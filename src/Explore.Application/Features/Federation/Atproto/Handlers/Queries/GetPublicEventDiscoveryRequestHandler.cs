using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Validators;
using Explore.Application.Responses;
using Explore.Application.Services.Federation;
using Explore.Application.Specifications.Events;
using Explore.Application.Features.PublicExperience;
using Explore.Domain;
using Explore.Application.Exceptions;
using Explore.Domain.Services.Discovery;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using FluentValidation;
using static Explore.Application.Features.Federation.Atproto.Handlers.Queries.EventDiscoveryCandidateMapping;

namespace Explore.Application.Features.Federation.Atproto.Handlers.Queries;

public sealed class GetPublicEventDiscoveryRequestHandler(
    IQueryHandler<GetEventListRequest, PaginatedResult<EventListDto>> localHandler,
    IAtprotoEventProjectionRepository projectionRepository,
    AtprotoEventGovernanceResolver governanceResolver,
    Explore.Application.Contracts.Infrastructure.ITenantContext tenantContext,
    TimeProvider timeProvider,
    ITenantLifecycleAccessService lifecycle,
    IEventDiscoveryIdentityRepository identities)
    : IQueryHandler<GetPublicEventDiscoveryRequest, PaginatedResult<EventDiscoveryItemDto>>
{
    public async Task<PaginatedResult<EventDiscoveryItemDto>> QueryAsync(
        GetPublicEventDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        var validator = new GetPublicEventDiscoveryRequestValidator();
        await validator.ValidateAndThrowAsync(request, cancellationToken);
        GetPublicEventDiscoveryRequestValidator.TryGetWindow(request, out int window);

        if (!await lifecycle.IsPublicAsync(tenantContext.TenantId, cancellationToken))
            return PaginatedResult<EventDiscoveryItemDto>.Create(
                [], 0, request.Criteria.PageNumber, request.Criteria.PageSize);

        AtprotoEventGovernance governance = await governanceResolver.ResolveAsync(
            tenantContext.TenantId,
            null,
            cancellationToken);

        GetEventListRequest criteria = request.Criteria;
        int requestedPage = criteria.PageNumber;
        int requestedPageSize = criteria.PageSize;
        PaginatedResult<EventListDto> localPage = await localHandler.QueryAsync(
            criteria.CopyWithPagination(1, window),
            cancellationToken);

        var localItems = localPage.Items
            .Take(window)
            .Select(value => MapLocal(value, governance.EventsEnabled))
            .ToList();
        var bindings = await identities.GetBindingsAsync(tenantContext.TenantId,
            EventDiscoverySourceKind.LocalEvent,
            localItems.Select(item => item.Event!.Id.ToString("D")).ToArray(), cancellationToken);
        if (bindings.Any(binding => binding.TenantId != tenantContext.TenantId
                || binding.Alias is { Primary: null }
                || !binding.IsDeleted && EventDiscoveryIdentityRules.SelectRepresentation(
                    binding.Alias is { } alias ? [binding, alias.Primary] : [binding],
                    new HashSet<Guid> { binding.Id }) is null))
            throw new EventDiscoveryUnavailableException();
        var bindingsByKey = bindings.ToDictionary(binding => binding.SourceKey, StringComparer.Ordinal);
        localItems.RemoveAll(item => bindingsByKey.TryGetValue(item.Event!.Id.ToString("D"), out var binding)
            && binding.IsDeleted);
        for (int index = 0; index < localItems.Count; index++)
        {
            var item = localItems[index];
            if (bindingsByKey.TryGetValue(item.Event!.Id.ToString("D"), out var binding))
                localItems[index] = item with
                {
                    DiscoveryIdentityId = binding.Alias?.PrimaryIdentityId ?? binding.Id
                };
        }

        int federatedTotalCount = 0;
        var federatedItems = new List<EventDiscoveryItemDto>();
        if (governance.EventsEnabled && TryCreateProjectionQuery(
                criteria, window, criteria.OperationNow ?? timeProvider.GetUtcNow(), out var projectionQuery))
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
            var reviewedEchoes = localItems.Where(item => item.Federation is not null
                    && item.DiscoveryIdentityId.HasValue)
                .ToDictionary(item => item.Federation!.AtprotoRecordId, item => item.DiscoveryIdentityId);
            for (int index = 0; index < federatedItems.Count; index++)
                if (reviewedEchoes.TryGetValue(federatedItems[index].FederatedEvent!.Id, out var root))
                    federatedItems[index] = federatedItems[index] with { DiscoveryIdentityId = root };
        }

        List<EventDiscoveryItemDto> merged = Collapse(localItems.Concat(federatedItems), bindingsByKey);
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
}

public static class EventDiscoveryCandidateMapping
{
    public static List<EventDiscoveryItemDto> Collapse(
        IEnumerable<EventDiscoveryItemDto> items,
        IReadOnlyDictionary<string, EventDiscoveryIdentity> bindingsByKey) => items
            .GroupBy(HomeDiscoveryAllocator.CanonicalIdentity)
            .Select(group => group
                .Order(Comparer<EventDiscoveryItemDto>.Create((left, right) => CompareRepresentations(
                    left, left.Event is not null
                        && bindingsByKey.TryGetValue(left.Event.Id.ToString("D"), out var leftBinding)
                        && leftBinding.Alias is null,
                    right, right.Event is not null
                        && bindingsByKey.TryGetValue(right.Event.Id.ToString("D"), out var rightBinding)
                        && rightBinding.Alias is null)))
                .First())
            .ToList();

    public static int CompareRepresentations(
        EventDiscoveryItemDto left, bool leftIsPrimary, EventDiscoveryItemDto right, bool rightIsPrimary)
    {
        int primary = rightIsPrimary.CompareTo(leftIsPrimary);
        if (primary != 0)
            return primary;
        int source = (right.Source == "local").CompareTo(left.Source == "local");
        return source != 0 ? source : StringComparer.Ordinal.Compare(
            EventDiscoveryRank.SourceKey(left.Event?.Id ?? left.FederatedEvent!.Id),
            EventDiscoveryRank.SourceKey(right.Event?.Id ?? right.FederatedEvent!.Id));
    }

    public static EventDiscoveryItemDto MapLocal(EventListDto value, bool includeFederationMetadata) => new()
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

    public static EventDiscoveryItemDto MapFederated(AtprotoEventProjection value) => new()
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

    public static bool TryCreateProjectionQuery(
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
        || value.AreaId.HasValue
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

    public static Comparison<EventDiscoveryItemDto> CreateComparer(string? sortBy, bool descending)
    {
        AtprotoEventDiscoverySort sort = MapSort(sortBy);
        return (left, right) =>
        {
            int primary = sort switch
            {
                AtprotoEventDiscoverySort.Title => StringComparer.Ordinal.Compare(
                    EventDiscoveryRank.TitleKey(Title(left)), EventDiscoveryRank.TitleKey(Title(right))),
                AtprotoEventDiscoverySort.Views => Views(left).CompareTo(Views(right)),
                AtprotoEventDiscoverySort.CreatedAt => CreatedAt(left).CompareTo(CreatedAt(right)),
                _ => Nullable.Compare(StartsAt(left), StartsAt(right))
            };
            if (descending)
            {
                primary = -primary;
            }
            if (primary != 0)
                return primary;
            int sourceKind = (left.Event is null ? EventDiscoverySourceKind.AtprotoRecord : EventDiscoverySourceKind.LocalEvent)
                .CompareTo(right.Event is null ? EventDiscoverySourceKind.AtprotoRecord : EventDiscoverySourceKind.LocalEvent);
            return sourceKind != 0 ? sourceKind : StringComparer.Ordinal.Compare(
                EventDiscoveryRank.SourceKey(StableIdentity(left)), EventDiscoveryRank.SourceKey(StableIdentity(right)));
        };
    }

    private static Guid StableIdentity(EventDiscoveryItemDto value) =>
        value.Event?.Id ?? value.FederatedEvent?.Id ?? Guid.Empty;

    private static string Title(EventDiscoveryItemDto value) =>
        value.Event?.Title ?? value.FederatedEvent?.Name ?? string.Empty;

    private static int Views(EventDiscoveryItemDto value) => value.Event?.TotalViews ?? 0;

    private static DateTimeOffset CreatedAt(EventDiscoveryItemDto value) =>
        value.Event?.CreatedAtUtc ?? value.FederatedEvent?.CreatedAtUtc ?? DateTimeOffset.MinValue;

    private static DateTimeOffset? StartsAt(EventDiscoveryItemDto value) =>
        value.Event?.MatchingSession?.StartsAtUtc ?? value.Event?.FirstSessionStartUtc ?? value.FederatedEvent?.StartsAtUtc;
}
