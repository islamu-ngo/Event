using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Features.ContactShareConsents.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.ContactShareConsents.Handlers.Queries;

public class GetOrganizationSharedContactsQueryHandler : IRequestHandler<GetOrganizationSharedContactsQuery, PaginatedResult<SharedContactDto>>
{
    private readonly IEventContactShareConsentRepository _consentRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IOrganizationRepository _organizationRepository;
    private readonly ILogger<GetOrganizationSharedContactsQueryHandler> _logger;

    public GetOrganizationSharedContactsQueryHandler(
        IEventContactShareConsentRepository consentRepository,
        IActorRepository actorRepository,
        IOrganizationRepository organizationRepository,
        ILogger<GetOrganizationSharedContactsQueryHandler> logger)
    {
        _consentRepository = consentRepository;
        _actorRepository = actorRepository;
        _organizationRepository = organizationRepository;
        _logger = logger;
    }

    public async Task<PaginatedResult<SharedContactDto>> Handle(GetOrganizationSharedContactsQuery request, CancellationToken cancellationToken)
    {
        // Validate actor is an approved organisation
        var actor = await _actorRepository.GetById(request.RecipientActorId);
        if (actor?.OrganizationId == null || actor.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning("Shared contacts query rejected: actor {ActorId} is not an organisation", request.RecipientActorId);
            return PaginatedResult<SharedContactDto>.Create([], 0, request.PageNumber, request.PageSize);
        }

        var org = await _organizationRepository.GetById(actor.OrganizationId.Value);
        if (org is null || !org.TenantParticipations.Any(
                participation => participation.TenantId == request.TenantId &&
                    participation.ApprovalStatusId == (int)ApprovalStatusEnum.Approved))
        {
            _logger.LogWarning("Shared contacts query rejected: organisation {OrgId} is not approved", actor.OrganizationId);
            return PaginatedResult<SharedContactDto>.Create([], 0, request.PageNumber, request.PageSize);
        }

        var (pageNumber, pageSize) = PaginatedResult<SharedContactDto>.NormalizeParameters(
            request.PageNumber, request.PageSize);

        var (items, totalCount) = await _consentRepository.GetGrantedForRecipient(
            request.TenantId, request.RecipientActorId, request.EventId, request.EmailSearch, pageNumber, pageSize);

        var dtos = items.Select(c => new SharedContactDto
        {
            ConsentId = c.Id,
            Email = c.EmailSnapshot,
            GrantedAt = c.GrantedAt,
            SourceEventId = null,
            SourceEventTitle = null,
            PurposeCode = c.PurposeCode
        }).ToList();

        return PaginatedResult<SharedContactDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
