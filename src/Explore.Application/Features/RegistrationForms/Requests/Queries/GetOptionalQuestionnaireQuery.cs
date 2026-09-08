using Explore.Application.DTOs.RegistrationForms;
using MediatR;

namespace Explore.Application.Features.RegistrationForms.Requests.Queries;

public sealed record GetOptionalQuestionnaireQuery(Guid EventId) : IRequest<OptionalQuestionnaireDto?>;
