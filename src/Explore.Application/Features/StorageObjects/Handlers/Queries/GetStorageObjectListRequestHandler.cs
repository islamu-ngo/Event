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
    private readonly TimeProvider _timeProvider;

    public GetStorageObjectListRequestHandler(IStorageObjectRepository storageObjectRepository, IMapper mapper, TimeProvider timeProvider)
    {
        _storageObjectRepository = storageObjectRepository;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<PaginatedResult<StorageObjectListDto>> Handle(GetStorageObjectListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<StorageObjectListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (storageObjects, totalCount) = await _storageObjectRepository.GetFilesWithDetailsPaged(pageNumber, pageSize);
        var dtos = new List<StorageObjectListDto>();
        foreach (var storageObject in storageObjects)
        {
            var eligibility = await StorageObjectContentEligibilityDto.ResolveAsync(
                storageObject, _storageObjectRepository, _timeProvider, cancellationToken);
            dtos.Add(_mapper.Map<StorageObjectListDto>(storageObject) with { ContentEligibility = eligibility });
        }
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        for (var index = 0; index < dtos.Count; index++)
            dtos[index] = dtos[index].ForDisclosureAt(utcNow);
        return PaginatedResult<StorageObjectListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
