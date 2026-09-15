using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Responses;

namespace Explore.Application.Features.RegistrationForms.Requests.Commands;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Attach)]
public sealed record AttachRegistrationRequirementCommand(
    Guid EventId,
    Guid WorkflowId,
    Guid RequirementId,
    bool StandaloneQuestionnaire,
    Guid? RegistrationFormId,
    Guid? RegistrationFormVersionId,
    Guid ExpectedConcurrencyStamp) : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => RequirementId == Guid.Empty ? null : RequirementId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Detach)]
public sealed record DetachRegistrationRequirementCommand(
    Guid EventId,
    Guid RequirementId,
    Guid ExpectedConcurrencyStamp) : ICommand<BaseCommandResponse<Guid>>, ISecureRequest
{
    string? ISecureRequest.ResourceId => RequirementId == Guid.Empty ? null : RequirementId.ToString();

    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}
