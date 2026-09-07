using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Locations.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Locations.Handlers.Commands;

public class DeleteLocationCommandHandler : IRequestHandler<DeleteLocationCommand, bool>
{
    private readonly ILocationRepository _locationRepository;

    public DeleteLocationCommandHandler(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<bool> Handle(DeleteLocationCommand request, CancellationToken cancellationToken)
    {
        var location = await _locationRepository.GetById(request.Id);

        if (location == null)
        {
            return false;
        }

        await _locationRepository.Delete(location);

        return true;
    }
}
