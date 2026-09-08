using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Tags.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.Tags.Handlers.Commands;

public class DeleteTagCommandHandler : IRequestHandler<DeleteTagCommand, bool>
{
    private readonly ITagRepository _tagRepository;

    public DeleteTagCommandHandler(ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public async Task<bool> Handle(DeleteTagCommand request, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetById(request.Id);

        if (tag == null)
        {
            return false;
        }

        await _tagRepository.Delete(tag);

        return true;
    }
}
