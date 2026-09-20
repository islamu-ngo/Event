using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplate;
using Explore.Application.Responses;

namespace Explore.Application.Features.EventTemplates.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record UpdateEventTemplateCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid TemplateId { get; init; }
    public required UpdateEventTemplateDto TemplateDto { get; init; }
    public Guid ExpectedConcurrencyStamp { get; init; }
    public Guid TenantId { get; init; }

    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);
}
