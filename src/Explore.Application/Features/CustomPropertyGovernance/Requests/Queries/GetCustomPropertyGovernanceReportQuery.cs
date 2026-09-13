using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;

[AuthorizeResource(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View)]
public sealed record GetCustomPropertyGovernanceReportQuery : IQuery<PaginatedResult<CustomPropertyGovernanceRowDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public GovernanceReportFilterDto Filter { get; init; } = new();

    string? ISecureRequest.ResourceId => null;
    IAuthorizationFacts ISecureRequest.AuthorizationFacts => new TenantScopedAuthorizationFacts(TenantId);
}
