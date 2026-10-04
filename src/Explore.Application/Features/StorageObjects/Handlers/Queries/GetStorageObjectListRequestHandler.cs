using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetStorageObjectListRequestHandler : IQueryHandler<GetStorageObjectListRequest, PaginatedResult<StorageObjectListDto>>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IStorageObjectRetirementEligibilityReader _retirementEligibility;
    private readonly TimeProvider _timeProvider;

    public GetStorageObjectListRequestHandler(
        IStorageObjectRepository storageObjectRepository,
        IStorageObjectRetirementEligibilityReader retirementEligibility,
        TimeProvider timeProvider)
    {
        _storageObjectRepository = storageObjectRepository;
        _retirementEligibility = retirementEligibility;
        _timeProvider = timeProvider;
    }

    public async Task<PaginatedResult<StorageObjectListDto>> QueryAsync(GetStorageObjectListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<StorageObjectListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (storageObjects, totalCount) = await _storageObjectRepository.GetFilesWithDetailsPaged(pageNumber, pageSize);
        var dtos = new List<StorageObjectListDto>();
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var storageObject in storageObjects)
        {
            var eligibility = await StorageObjectContentEligibilityDto.ResolveAsync(
                storageObject, _storageObjectRepository, _timeProvider, cancellationToken);
            bool retirementAllowed = await _retirementEligibility.CanRetireAsync(
                storageObject, utcNow, cancellationToken);
            dtos.Add(ActorFederationMapper.ToStorageListItem(storageObject) with
            {
                ContentEligibility = eligibility,
                RetirementAllowed = retirementAllowed
            });
        }
        var disclosureTime = _timeProvider.GetUtcNow().UtcDateTime;
        for (var index = 0; index < dtos.Count; index++)
            dtos[index] = dtos[index].ForDisclosureAt(disclosureTime);
        return PaginatedResult<StorageObjectListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
