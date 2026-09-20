using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Tags.Handlers.Queries;

public class GetTagDetailsRequestHandler : IQueryHandler<GetTagDetailsRequest, TagDto?>
{
    private readonly ITagRepository _tagRepository;

    public GetTagDetailsRequestHandler(
        ITagRepository tagRepository)
    {
        _tagRepository = tagRepository;
    }

    public async Task<TagDto?> QueryAsync(GetTagDetailsRequest request, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetTagWithDetails(request.Id);
        return TagMapper.ToDetail(tag);
    }
}
