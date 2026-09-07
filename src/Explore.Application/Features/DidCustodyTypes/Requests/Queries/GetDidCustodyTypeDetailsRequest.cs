using Explore.Application.DTOs.DidCustodyType;
using MediatR;

namespace Explore.Application.Features.DidCustodyTypes.Requests.Queries;

public sealed record GetDidCustodyTypeDetailsRequest(int Id = default) : IRequest<DidCustodyTypeDto>;
