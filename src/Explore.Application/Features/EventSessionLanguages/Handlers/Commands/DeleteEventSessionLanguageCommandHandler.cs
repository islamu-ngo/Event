using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Commands;

public class DeleteEventSessionLanguageCommandHandler : IRequestHandler<DeleteEventSessionLanguageCommand, bool>
{
    private readonly IEventSessionLanguageRepository _repository;

    public DeleteEventSessionLanguageCommandHandler(IEventSessionLanguageRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(DeleteEventSessionLanguageCommand request, CancellationToken cancellationToken)
    {
        var eventSessionLanguage = await _repository.GetById(request.Id);
        if (eventSessionLanguage == null)
        {
            return false;
        }

        if (eventSessionLanguage.EventSessionId != request.EventSessionId)
        {
            return false;
        }

        await _repository.Delete(eventSessionLanguage);
        return true;
    }
}
