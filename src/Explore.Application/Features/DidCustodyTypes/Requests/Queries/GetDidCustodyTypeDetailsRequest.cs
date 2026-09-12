using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.DidCustodyTypes.Requests.Queries;

public sealed record GetDidCustodyTypeDetailsRequest(int Id = default) : IQuery<DidCustodyTypeDto?>;
