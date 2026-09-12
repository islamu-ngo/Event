using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Handlers.Commands;

public class DeleteTagTypeTagsCommandHandler : ICommandHandler<DeleteTagTypeTagsCommand, bool>
{
    private readonly ITagTypeTagsRepository _repository;

    public DeleteTagTypeTagsCommandHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> ExecuteAsync(DeleteTagTypeTagsCommand request, CancellationToken cancellationToken)
    {
        var tagTypeTags = await _repository.GetById(request.Id);
        if (tagTypeTags == null)
        {
            return false;
        }

        await _repository.Delete(tagTypeTags);
        return true;
    }
}
