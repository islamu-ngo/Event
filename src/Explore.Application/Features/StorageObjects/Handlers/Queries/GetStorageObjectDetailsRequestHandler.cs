using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetStorageObjectDetailsRequestHandler : IRequestHandler<GetStorageObjectDetailsRequest, StorageObjectDto?>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IMapper _mapper;

    public GetStorageObjectDetailsRequestHandler(IStorageObjectRepository storageObjectRepository, IMapper mapper)
    {
        _storageObjectRepository = storageObjectRepository;
        _mapper = mapper;
    }

    public async Task<StorageObjectDto?> Handle(GetStorageObjectDetailsRequest request, CancellationToken cancellationToken)
    {
        var storageObject = await _storageObjectRepository.GetById(request.Id);
        return _mapper.Map<StorageObjectDto>(storageObject);
    }
}
