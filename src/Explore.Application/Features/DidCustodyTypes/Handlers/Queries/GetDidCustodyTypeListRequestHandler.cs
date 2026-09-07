using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.DidCustodyTypes.Handlers.Queries;

public class GetDidCustodyTypeListRequestHandler : IRequestHandler<GetDidCustodyTypeListRequest, List<DidCustodyTypeListDto>>
{
    private readonly IDidCustodyTypeRepository _didCustodyTypeRepository;
    private readonly IMapper _mapper;

    public GetDidCustodyTypeListRequestHandler(IDidCustodyTypeRepository didCustodyTypeRepository, IMapper mapper)
    {
        _didCustodyTypeRepository = didCustodyTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<DidCustodyTypeListDto>> Handle(GetDidCustodyTypeListRequest request, CancellationToken cancellationToken)
    {
        var didCustodyTypes = await _didCustodyTypeRepository.GetAll();
        return _mapper.Map<List<DidCustodyTypeListDto>>(didCustodyTypes);
    }
}
