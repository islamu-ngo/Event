using Explore.Application.Authorization;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.ContactShareConsents.Requests.Queries;

[AuthorizeResource(ResourceKinds.EventContactShareConsent, AuthorizationActions.ViewSharedContacts)]
public sealed record GetOrganizationSharedContactsQuery : IRequest<PaginatedResult<SharedContactDto>>, ISecureRequest
{
    public Guid RecipientActorId { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? EventId { get; init; }
    public string? EmailSearch { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => OrganizationId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new ContactShareAuthorizationFacts(TenantId, OrganizationId);
}
