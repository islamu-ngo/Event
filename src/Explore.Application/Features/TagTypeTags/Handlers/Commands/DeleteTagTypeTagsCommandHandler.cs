using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.TagTypeTags.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.TagTypeTags.Handlers.Commands;

public class DeleteTagTypeTagsCommandHandler : IRequestHandler<DeleteTagTypeTagsCommand, bool>
{
    private readonly ITagTypeTagsRepository _repository;

    public DeleteTagTypeTagsCommandHandler(ITagTypeTagsRepository repository)
    {
        _repository = repository;
    }

    public async Task<bool> Handle(DeleteTagTypeTagsCommand request, CancellationToken cancellationToken)
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
