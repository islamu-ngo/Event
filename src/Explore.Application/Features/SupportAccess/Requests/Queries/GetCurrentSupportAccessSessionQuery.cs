using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.SupportAccess;

namespace Explore.Application.Features.SupportAccess.Requests.Queries;

public sealed record GetCurrentSupportAccessSessionQuery : IQuery<CurrentSupportAccessSessionDto>;
