using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetStorageObjectListRequestHandler : IRequestHandler<GetStorageObjectListRequest, PaginatedResult<StorageObjectListDto>>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IMapper _mapper;

    public GetStorageObjectListRequestHandler(IStorageObjectRepository storageObjectRepository, IMapper mapper)
    {
        _storageObjectRepository = storageObjectRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<StorageObjectListDto>> Handle(GetStorageObjectListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<StorageObjectListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (storageObjects, totalCount) = await _storageObjectRepository.GetFilesWithDetailsPaged(pageNumber, pageSize);
        var dtos = _mapper.Map<List<StorageObjectListDto>>(storageObjects);
        return PaginatedResult<StorageObjectListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
