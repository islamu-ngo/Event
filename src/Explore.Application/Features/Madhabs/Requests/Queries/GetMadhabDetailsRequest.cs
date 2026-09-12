using Explore.Application.DTOs.Madhab;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Madhabs.Requests.Queries;

public sealed record GetMadhabDetailsRequest(int Id = default) : IQuery<MadhabDto?>;
