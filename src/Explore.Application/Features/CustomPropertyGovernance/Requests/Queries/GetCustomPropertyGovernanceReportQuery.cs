using Explore.Application.Authorization;
using Explore.Application.DTOs.CustomPropertyGovernance;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.CustomPropertyGovernance.Requests.Queries;

[AuthorizeResource(ResourceKinds.CustomPropertyGovernance, AuthorizationActions.View)]
public sealed record GetCustomPropertyGovernanceReportQuery : IRequest<PaginatedResult<CustomPropertyGovernanceRowDto>>, ISecureRequest
{
    public Guid TenantId { get; init; }
    public GovernanceReportFilterDto Filter { get; init; } = new();

    string? ISecureRequest.ResourceId => null;
}
