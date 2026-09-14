using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Studio;

namespace Explore.Application.Features.Studio.Requests.Queries;

public sealed record GetStudioContextQuery(Guid? ActorId = null) : IQuery<StudioContextDto>;
