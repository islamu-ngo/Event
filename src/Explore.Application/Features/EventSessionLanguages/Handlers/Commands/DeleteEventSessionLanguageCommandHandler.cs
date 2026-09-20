using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventSessionLanguages.Requests.Commands;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Handlers.Commands;

public class DeleteEventSessionLanguageCommandHandler : ICommandHandler<DeleteEventSessionLanguageCommand, bool>
{
    private readonly IEventSessionLanguageRepository _repository;

    public DeleteEventSessionLanguageCommandHandler(IEventSessionLanguageRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> ExecuteAsync(DeleteEventSessionLanguageCommand request, CancellationToken cancellationToken)
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
