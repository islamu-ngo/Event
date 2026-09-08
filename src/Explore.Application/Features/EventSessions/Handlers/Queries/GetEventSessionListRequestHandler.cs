using AutoMapper;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyProjection;
using Explore.Application.DTOs.EventSession;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Specifications.EventSessions;
using MediatR;

namespace Explore.Application.Features.EventSessions.Handlers.Queries;

public class GetEventSessionListRequestHandler : IRequestHandler<GetEventSessionListRequest, PaginatedResult<EventSessionListDto>>
{
    private readonly IEventSessionRepository _eventSessionRepository;
    private readonly IMapper _mapper;
    private readonly ICustomPropertyQuotaResolver _quotaResolver;
    private readonly ITenantContext _tenantContext;
    private readonly IEventLocationDisclosureService _disclosureService;

    public GetEventSessionListRequestHandler(
        IEventSessionRepository eventSessionRepository,
        IMapper mapper,
        ICustomPropertyQuotaResolver quotaResolver,
        ITenantContext tenantContext,
        IEventLocationDisclosureService disclosureService)
    {
        _eventSessionRepository = eventSessionRepository;
        _mapper = mapper;
        _quotaResolver = quotaResolver;
        _tenantContext = tenantContext;
        _disclosureService = disclosureService;
    }

    public async Task<PaginatedResult<EventSessionListDto>> Handle(GetEventSessionListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionListDto>.NormalizeParameters(request.PageNumber, request.PageSize);

        var specification = await BuildSpecificationAsync(request, cancellationToken);

        if (specification is null)
        {
            var (items, totalCount) = await _eventSessionRepository.GetPublicSessionsWithDetailsPagedAsync(
                pageNumber,
                pageSize,
                cancellationToken);
            return PaginatedResult<EventSessionListDto>.Create(
                await PublicEventSessionLocationProjector.ProjectAsync(
                    items,
                    _mapper,
                    _disclosureService,
                    cancellationToken),
                totalCount,
                pageNumber,
                pageSize);
        }

        var (sessions, total) = await _eventSessionRepository.GetPublicSessionsWithDetailsPagedFilteredAsync(
            pageNumber,
            pageSize,
            specification,
            cancellationToken);
        return PaginatedResult<EventSessionListDto>.Create(
            await PublicEventSessionLocationProjector.ProjectAsync(
                sessions,
                _mapper,
                _disclosureService,
                cancellationToken),
            total,
            pageNumber,
            pageSize);
    }

    private async Task<EventSessionQuerySpecification?> BuildSpecificationAsync(
        GetEventSessionListRequest request,
        CancellationToken ct)
    {
        var hasCustomPropertyFilters = request.CustomPropertyFilters is { Count: > 0 };
        var hasCustomPropertySearch = !string.IsNullOrWhiteSpace(request.CustomPropertySearchTerm);

        if (!hasCustomPropertyFilters && !hasCustomPropertySearch)
            return null;

        var tenantId = _tenantContext.TenantId;
        var projectionEnabled = await _quotaResolver.GetBoolAsync(
            "custom_properties.projection_discovery_enabled", tenantId, ct);

        if (!projectionEnabled)
            return null;

        var spec = new EventSessionQuerySpecification();

        if (hasCustomPropertySearch)
        {
            spec = spec.And(EventSessionCustomPropertyProjectionFilter.GlobalTextSearch(
                request.CustomPropertySearchTerm!.Trim()));
        }

        if (hasCustomPropertyFilters)
        {
            foreach (var criterion in request.CustomPropertyFilters!)
            {
                var filter = MapCriterionToFilter(criterion);
                if (filter is not null)
                {
                    spec = spec.And(filter);
                }
            }
        }

        return spec.HasFilters ? spec : null;
    }

    internal static EventSessionCustomPropertyProjectionFilter? MapCriterionToFilter(CustomPropertyFilterCriterion criterion)
    {
        return criterion.Operator switch
        {
            CustomPropertyFilterOperator.Equals when !string.IsNullOrWhiteSpace(criterion.Value) =>
                EventSessionCustomPropertyProjectionFilter.ExactMatch(criterion.Namespace, criterion.Key, criterion.Value),

            CustomPropertyFilterOperator.Contains when !string.IsNullOrWhiteSpace(criterion.Value) =>
                EventSessionCustomPropertyProjectionFilter.TextSearch(criterion.Namespace, criterion.Key, criterion.Value),

            CustomPropertyFilterOperator.Exists =>
                EventSessionCustomPropertyProjectionFilter.Exists(criterion.Namespace, criterion.Key),

            CustomPropertyFilterOperator.BooleanTrue =>
                EventSessionCustomPropertyProjectionFilter.BooleanTrue(criterion.Namespace, criterion.Key),

            CustomPropertyFilterOperator.OptionEquals when criterion.OptionId.HasValue =>
                EventSessionCustomPropertyProjectionFilter.OptionMatch(criterion.Namespace, criterion.Key, criterion.OptionId.Value),

            CustomPropertyFilterOperator.OptionIn when criterion.OptionIds is { Count: > 0 } =>
                EventSessionCustomPropertyProjectionFilter.OptionsMatchAny(criterion.Namespace, criterion.Key, criterion.OptionIds.ToList()),

            CustomPropertyFilterOperator.NumberRange when criterion.MinNumber.HasValue || criterion.MaxNumber.HasValue =>
                EventSessionCustomPropertyProjectionFilter.NumberRange(criterion.Namespace, criterion.Key, criterion.MinNumber, criterion.MaxNumber),

            CustomPropertyFilterOperator.DateRange when criterion.DateFrom.HasValue || criterion.DateTo.HasValue =>
                EventSessionCustomPropertyProjectionFilter.DateRange(criterion.Namespace, criterion.Key, criterion.DateFrom, criterion.DateTo),

            _ => null
        };
    }
}
