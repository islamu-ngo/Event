using System;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventCategories.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.EventCategories.Handlers.Commands;

public class DeleteEventCategoriesCommandHandler : IRequestHandler<DeleteEventCategoriesCommand, bool>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;

    public DeleteEventCategoriesCommandHandler(IEventCategoriesRepository eventCategoriesRepository)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
    }

    public async Task<bool> Handle(DeleteEventCategoriesCommand request, CancellationToken cancellationToken)
    {
        var eventCategories = await _eventCategoriesRepository.GetById(request.Id);

        if (eventCategories == null)
        {
            return false;
        }

        await _eventCategoriesRepository.Delete(eventCategories);
        return true;
    }
}
