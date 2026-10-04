using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.Features.Events.Discovery;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Events.Handlers.Queries;

public class GetEventListRequestHandler(
    IEventRepository eventRepository,
    IActorRepository actorRepository,
    IObjectStorageService objectStorageService,
    ILogger<GetEventListRequestHandler> logger,
    IModuleService moduleService,
    ITenantContext tenantContext,
    ICustomPropertyQuotaResolver quotaResolver,
    ITenantLifecycleAccessService lifecycle,
    IHierarchicalSettingsResolver settingsResolver,
    ILocationRepository locationRepository,
    TimeProvider clock,
    ILocationPrivacyGovernanceService locationPrivacy,
    IEventLocationDisclosureService locationDisclosure)
    : IQueryHandler<GetEventListRequest, PaginatedResult<EventListDto>>
{
    private readonly EventDiscoveryLocalSource _source = new(
        eventRepository, actorRepository, objectStorageService, logger, moduleService, tenantContext,
        quotaResolver, lifecycle, settingsResolver, locationRepository, clock, locationPrivacy, locationDisclosure);

    public Task<PaginatedResult<EventListDto>> QueryAsync(
        GetEventListRequest request, CancellationToken cancellationToken) =>
        _source.QueryAsync(request, cancellationToken);
}
