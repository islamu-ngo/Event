using Explore.Application.DTOs.EventTemplate;
using MediatR;

namespace Explore.Application.Features.EventTemplates.Requests.Queries;

public sealed record GetEventTemplateDetailsRequest(Guid Id = default) : IRequest<EventTemplateDto>;
