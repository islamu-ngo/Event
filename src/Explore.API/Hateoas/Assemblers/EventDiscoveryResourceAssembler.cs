using System.Security.Claims;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Hateoas;

namespace Explore.API.Hateoas.Assemblers;

public sealed class EventDiscoveryResourceAssembler : ResourceAssemblerBase<EventDiscoveryItemDto>
{
    public EventDiscoveryResourceAssembler(
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventDiscoveryItemDto> detailLinkPolicy,
        ICollectionLinkPolicy<EventDiscoveryItemDto> collectionLinkPolicy,
        IEventReportingIntakeGuard intakeGuard)
        : base(linkGenerator, detailLinkPolicy, collectionLinkPolicy)
    {
        _intakeGuard = intakeGuard;
        _detailLinkPolicy = detailLinkPolicy;
        _collectionLinkPolicy = collectionLinkPolicy;
    }

    private readonly IEventReportingIntakeGuard _intakeGuard;
    private readonly ILinkPolicy<EventDiscoveryItemDto> _detailLinkPolicy;
    private readonly ICollectionLinkPolicy<EventDiscoveryItemDto> _collectionLinkPolicy;

    protected override async Task<IReadOnlyList<LinkDefinition>> GetDetailLinkDefinitionsAsync(
        EventDiscoveryItemDto dto,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        EventDiscoveryItemDto policyInput = await CreatePolicyInputAsync(dto, httpContext.RequestAborted);
        return _detailLinkPolicy.GetLinks(policyInput, user).ToList();
    }

    protected override async Task<IReadOnlyList<LinkDefinition>> GetListItemLinkDefinitionsAsync(
        EventDiscoveryItemDto dto,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        EventDiscoveryItemDto policyInput = await CreatePolicyInputAsync(dto, httpContext.RequestAborted);
        return _collectionLinkPolicy.GetItemLinks(policyInput, user).ToList();
    }

    protected override async Task<IReadOnlyList<IReadOnlyList<LinkDefinition>>> GetCollectionItemLinkDefinitionsAsync(
        IReadOnlyList<EventDiscoveryItemDto> items,
        ClaimsPrincipal? user,
        HttpContext httpContext)
    {
        var decisions = new Dictionary<Guid, EventReportingIntakeDecision>();
        foreach (Guid tenantId in items
                     .Where(item => item.Source == "local" && item.Event is not null)
                     .Select(item => item.Event!.TenantId)
                     .Where(tenantId => tenantId != Guid.Empty)
                     .Distinct())
        {
            decisions[tenantId] = await _intakeGuard.ResolveAsync(tenantId, httpContext.RequestAborted);
        }

        return items
            .Select(item => (IReadOnlyList<LinkDefinition>)_collectionLinkPolicy.GetItemLinks(
                CreatePolicyInput(item, item.Event is null ? null : decisions.GetValueOrDefault(item.Event.TenantId)),
                user).ToList())
            .ToList();
    }

    private async Task<EventDiscoveryItemDto> CreatePolicyInputAsync(
        EventDiscoveryItemDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.Source != "local" || dto.Event is null || dto.Event.TenantId == Guid.Empty)
        {
            return CreatePolicyInput(dto, null);
        }

        EventReportingIntakeDecision decision = await _intakeGuard.ResolveAsync(dto.Event.TenantId, cancellationToken);
        return CreatePolicyInput(dto, decision);
    }

    private static EventDiscoveryItemDto CreatePolicyInput(
        EventDiscoveryItemDto dto,
        EventReportingIntakeDecision? decision)
    {
        if (dto.Source != "local")
        {
            return dto with { Event = null };
        }

        if (dto.Event is null)
        {
            return dto;
        }

        EventListDto eventPolicyInput = dto.Event with
        {
            IsReportingIntakeEnabled = decision is { TenantResolved: true, IntakeEnabled: true }
        };
        return dto with { Event = eventPolicyInput };
    }
}
