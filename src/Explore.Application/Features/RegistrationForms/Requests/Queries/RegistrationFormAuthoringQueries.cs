using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationForms;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Requests.Queries;

public interface IRegistrationFormAuthoringQuery<out TResponse> : IRequest<TResponse>, ISecureRequest
{
    Guid EventId { get; }

    string? ISecureRequest.ResourceId => EventId == Guid.Empty ? null : EventId.ToString();

    // Form and version identifiers select the payload, not the authority: registration authoring reads
    // are decided against the parent event, which the resolver reloads server-side.
    IAuthorizationFacts? ISecureRequest.AuthorizationFacts =>
        new EventScopedAuthorizationFacts(Guid.Empty, EventId);
}

public interface IRegistrationFormScopedQuery<out TResponse> : IRegistrationFormAuthoringQuery<TResponse>
{
    Guid FormId { get; }

    string? ISecureRequest.ResourceId => FormId == Guid.Empty ? null : FormId.ToString();
}

public interface IRegistrationFormVersionScopedQuery<out TResponse> : IRegistrationFormScopedQuery<TResponse>
{
    Guid VersionId { get; }
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ManageRegistrationWorkflow)]
public sealed record GetRegistrationWorkflowQuery(Guid EventId, string Purpose)
    : IRegistrationFormAuthoringQuery<RegistrationWorkflowDto?>;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record GetRegistrationFormQuery(Guid EventId, Guid FormId)
    : IRegistrationFormScopedQuery<RegistrationFormDto?>;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record GetRegistrationFormVersionQuery(Guid EventId, Guid FormId, Guid VersionId)
    : IRegistrationFormVersionScopedQuery<RegistrationFormVersionDto?>;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.Preflight)]
public sealed record GetRegistrationFormPublishPreflightQuery(Guid EventId, Guid FormId, Guid VersionId)
    : IRegistrationFormVersionScopedQuery<RegistrationFormPublishPreflightDto?>;
