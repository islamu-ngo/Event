using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.DidCustodyTypes.Handlers.Queries;

public class GetDidCustodyTypeDetailsRequestHandler : IRequestHandler<GetDidCustodyTypeDetailsRequest, DidCustodyTypeDto>
{
    private readonly IDidCustodyTypeRepository _didCustodyTypeRepository;
    private readonly IMapper _mapper;

    public GetDidCustodyTypeDetailsRequestHandler(IDidCustodyTypeRepository didCustodyTypeRepository, IMapper mapper)
    {
        _didCustodyTypeRepository = didCustodyTypeRepository;
        _mapper = mapper;
    }

    public async Task<DidCustodyTypeDto> Handle(GetDidCustodyTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var didCustodyType = await _didCustodyTypeRepository.GetById(request.Id);
        return _mapper.Map<DidCustodyTypeDto>(didCustodyType);
    }
}
