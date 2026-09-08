using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationForms;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Requests.Queries;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record ListRegistrationFormTemplatesQuery : IRequest<IReadOnlyList<RegistrationFormTemplateDto>>, ISecureRequest;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record GetRegistrationFormTemplateQuery(Guid TemplateId) : IRequest<RegistrationFormTemplateDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TemplateId == Guid.Empty ? null : TemplateId.ToString();
}
