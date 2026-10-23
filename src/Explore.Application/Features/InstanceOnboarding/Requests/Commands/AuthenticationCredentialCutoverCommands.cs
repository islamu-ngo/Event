using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Onboarding;

namespace Explore.Application.Features.InstanceOnboarding.Requests.Commands;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record CleanupAuthenticationCredentialsCommand(AuthenticationCredentialCleanupInputDto Input)
    : ICommand<AuthenticationCredentialCutoverDto>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record PrepareAuthenticationCredentialCutoverCommand(
    PrepareAuthenticationCredentialCutoverInputDto Input)
    : ICommand<AuthenticationCredentialCutoverDto>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record ReportAuthenticationCredentialActivationCommand(
    AuthenticationCredentialActivationInputDto Input)
    : ICommand<AuthenticationCredentialCutoverDto>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.Update)]
public sealed record AttestAuthenticationCredentialFenceCommand(
    AuthenticationCredentialFenceInputDto Input)
    : ICommand<AuthenticationCredentialCutoverDto>, ISecureRequest
{
    public const string ResourceKey = "authentication-credential-cutover";
    string? ISecureRequest.ResourceId => ResourceKey;
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts => InstanceScopedAuthorizationFacts.Instance;
}
