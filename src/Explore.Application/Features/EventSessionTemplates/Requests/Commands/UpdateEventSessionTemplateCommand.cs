using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionTemplate;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateEventSessionTemplateCommand : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid SessionTemplateId { get; init; }
    public required UpdateEventSessionTemplateDto SessionTemplateDto { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
