using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventSessionTemplate;

namespace Explore.Application.Features.EventSessionTemplates.Requests.Queries;

public sealed record GetEventSessionTemplateDetailsRequest(Guid Id = default) : IQuery<EventSessionTemplateDto>;
