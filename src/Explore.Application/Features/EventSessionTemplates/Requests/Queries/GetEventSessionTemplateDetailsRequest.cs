using Explore.Application.DTOs.EventSessionTemplate;
using MediatR;

namespace Explore.Application.Features.EventSessionTemplates.Requests.Queries;

public sealed record GetEventSessionTemplateDetailsRequest(Guid Id = default) : IRequest<EventSessionTemplateDto>;
