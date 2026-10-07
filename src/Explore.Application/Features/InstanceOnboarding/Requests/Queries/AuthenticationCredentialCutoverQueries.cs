using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record InspectAuthenticationCredentialCleanupQuery(Guid OperationId)
    : IQuery<AuthenticationCredentialCleanupInventoryDto>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetCurrentAuthenticationCredentialCutoverQuery
    : IQuery<AuthenticationCredentialCutoverDto?>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetAuthenticationCredentialCutoverQuery(Guid OperationId)
    : IQuery<AuthenticationCredentialCutoverDto?>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}
