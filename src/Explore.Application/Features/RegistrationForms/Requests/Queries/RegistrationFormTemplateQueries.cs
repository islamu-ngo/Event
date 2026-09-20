using Explore.Application.Authorization;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationForms;

namespace Explore.Application.Features.RegistrationForms.Requests.Queries;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record ListRegistrationFormTemplatesQuery : IQuery<IReadOnlyList<RegistrationFormTemplateDto>>, ISecureRequest;

[AuthorizeResource(ResourceKinds.RegistrationForm, AuthorizationActions.RegistrationForms.View)]
public sealed record GetRegistrationFormTemplateQuery(Guid TemplateId) : IQuery<RegistrationFormTemplateDto?>, ISecureRequest
{
    string? ISecureRequest.ResourceId => TemplateId == Guid.Empty ? null : TemplateId.ToString();
}
