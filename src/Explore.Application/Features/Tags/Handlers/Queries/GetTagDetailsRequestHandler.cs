using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.Tags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tags.Handlers.Queries;

public class GetTagDetailsRequestHandler : IRequestHandler<GetTagDetailsRequest, TagDto>
{
    private readonly ITagRepository _tagRepository;
    private readonly IMapper _mapper;

    public GetTagDetailsRequestHandler(
        ITagRepository tagRepository,
        IMapper mapper)
    {
        _tagRepository = tagRepository;
        _mapper = mapper;
    }

    public async Task<TagDto> Handle(GetTagDetailsRequest request, CancellationToken cancellationToken)
    {
        var tag = await _tagRepository.GetTagWithDetails(request.Id);
        return _mapper.Map<TagDto>(tag);
    }
}
