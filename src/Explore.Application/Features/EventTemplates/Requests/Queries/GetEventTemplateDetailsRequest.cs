using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventTemplate;

namespace Explore.Application.Features.EventTemplates.Requests.Queries;

public sealed record GetEventTemplateDetailsRequest(Guid Id = default) : IQuery<EventTemplateDto>;
