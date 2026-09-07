using Explore.Application.DTOs.SupportAccess;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Requests.Queries;

public sealed record GetCurrentSupportAccessSessionQuery : IRequest<CurrentSupportAccessSessionDto>;
