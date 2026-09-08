using Explore.Application.Authorization;
using Explore.Application.DTOs.EmailDispatch;
using MediatR;

namespace Explore.Application.Features.EmailDispatch.Requests.Queries;

[AuthorizeResource(ResourceKinds.InstanceSetting, AuthorizationActions.InstanceSettings.View)]
public sealed record GetEmailDispatchProcessorControlQuery : IRequest<EmailDispatchProcessorControlDto>, ISecureRequest
{
    string ISecureRequest.ResourceId => EmailDispatchProcessorControl.SettingKey;

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        InstanceScopedAuthorizationFacts.Instance;
}
