using Explore.Application.Authorization;
using Explore.Application.DTOs.EmailDispatch;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EmailDispatch.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetEmailDispatchProcessorControlQuery : IQuery<EmailDispatchProcessorControlDto>, ISecureRequest
{
    string ISecureRequest.ResourceId => EmailDispatchProcessorControl.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
