// ABOUTME: Query handler returning metadata for a single storage object by ID.
// ABOUTME: Maps StorageObject entity to StorageObjectDto.
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
    private readonly TimeProvider _timeProvider;

    public GetStorageObjectDetailsRequestHandler(IStorageObjectRepository storageObjectRepository, IMapper mapper, TimeProvider timeProvider)
    {
        _storageObjectRepository = storageObjectRepository;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<StorageObjectDto?> Handle(GetStorageObjectDetailsRequest request, CancellationToken cancellationToken)
    {
        var storageObject = await _storageObjectRepository.GetById(request.Id);
        if (storageObject is null) return null;
        var eligibility = await StorageObjectContentEligibilityDto.ResolveAsync(
            storageObject, _storageObjectRepository, _timeProvider, cancellationToken);
        var dto = _mapper.Map<StorageObjectDto>(storageObject) with { ContentEligibility = eligibility };
        return dto.ForDisclosureAt(_timeProvider.GetUtcNow().UtcDateTime);
    }
}
