using Explore.Application.Authorization;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Footer.Requests.Commands;

[AuthorizeResource(ResourceKinds.Tenant, AuthorizationActions.Update)]
public sealed record ReorderFooterLinkGroupsCommand : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    public Guid UserId { get; init; }
    public Guid TenantId { get; init; }
    /// <summary>Group IDs in the desired display order (first = 0).</summary>
    private IReadOnlyList<Guid> _orderedGroupIds = Array.AsReadOnly(Array.Empty<Guid>());

    public required IReadOnlyList<Guid> OrderedGroupIds
    {
        get => _orderedGroupIds;
        init => _orderedGroupIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }
    string? ISecureRequest.ResourceId => TenantId == Guid.Empty ? null : TenantId.ToString("D");
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        TenantId == Guid.Empty
        ? null
        : new TenantScopedAuthorizationFacts(TenantId);

}
