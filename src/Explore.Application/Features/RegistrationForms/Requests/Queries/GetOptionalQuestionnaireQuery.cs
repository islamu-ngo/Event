using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.RegistrationForms;

namespace Explore.Application.Features.RegistrationForms.Requests.Queries;

public sealed record GetOptionalQuestionnaireQuery(Guid EventId) : IQuery<OptionalQuestionnaireDto?>;
