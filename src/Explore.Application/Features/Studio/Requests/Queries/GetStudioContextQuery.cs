using Explore.Application.DTOs.Studio;
using MediatR;

namespace Explore.Application.Features.Studio.Requests.Queries;

public sealed record GetStudioContextQuery(Guid? ActorId = null) : IRequest<StudioContextDto>;
