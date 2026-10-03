using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetStorageObjectDetailsRequestHandler : IQueryHandler<GetStorageObjectDetailsRequest, StorageObjectDto?>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IStorageObjectRetirementEligibilityReader _retirementEligibility;
    private readonly TimeProvider _timeProvider;

    public GetStorageObjectDetailsRequestHandler(
        IStorageObjectRepository storageObjectRepository,
        IStorageObjectRetirementEligibilityReader retirementEligibility,
        TimeProvider timeProvider)
    {
        _storageObjectRepository = storageObjectRepository;
        _retirementEligibility = retirementEligibility;
        _timeProvider = timeProvider;
    }

    public async Task<StorageObjectDto?> QueryAsync(GetStorageObjectDetailsRequest request, CancellationToken cancellationToken)
    {
        var storageObject = await _storageObjectRepository.GetForGenericAccessAsync(request.Id, cancellationToken);
        if (storageObject is null) return null;
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var eligibility = await StorageObjectContentEligibilityDto.ResolveAsync(
            storageObject, _storageObjectRepository, _timeProvider, cancellationToken);
        bool retirementAllowed = await _retirementEligibility.CanRetireAsync(
            storageObject, utcNow, cancellationToken);
        var dto = ActorFederationMapper.ToStorageDetail(storageObject) with
        {
            ContentEligibility = eligibility,
            RetirementAllowed = retirementAllowed
        };
        return dto.ForDisclosureAt(_timeProvider.GetUtcNow().UtcDateTime);
    }
}
