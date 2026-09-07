using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Requests.Commands;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Create)]
public sealed record CreateRegistrationFormTemplateCommand(RegistrationFormTemplateInputDto Input)
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => Input.SourceRegistrationFormId == Guid.Empty
        ? null
        : Input.SourceRegistrationFormId.ToString();
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record InstantiateRegistrationFormTemplateCommand(
    Guid TemplateId,
    InstantiateRegistrationFormTemplateInputDto Input)
    : IRequest<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => Input.EventId == Guid.Empty ? null : Input.EventId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, Input.EventId);
}
