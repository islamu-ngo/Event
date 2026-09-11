using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.StorageObjects.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.StorageObjects.Handlers.Queries;

public class GetStorageObjectDetailsRequestHandler : IRequestHandler<GetStorageObjectDetailsRequest, StorageObjectDto?>
{
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly TimeProvider _timeProvider;

    public GetStorageObjectDetailsRequestHandler(IStorageObjectRepository storageObjectRepository, TimeProvider timeProvider)
    {
        _storageObjectRepository = storageObjectRepository;
        _timeProvider = timeProvider;
    }

    public async Task<StorageObjectDto?> Handle(GetStorageObjectDetailsRequest request, CancellationToken cancellationToken)
    {
        var storageObject = await _storageObjectRepository.GetById(request.Id);
        if (storageObject is null) return null;
        var eligibility = await StorageObjectContentEligibilityDto.ResolveAsync(
            storageObject, _storageObjectRepository, _timeProvider, cancellationToken);
        var dto = ActorFederationMapper.ToStorageDetail(storageObject) with { ContentEligibility = eligibility };
        return dto.ForDisclosureAt(_timeProvider.GetUtcNow().UtcDateTime);
    }
}
