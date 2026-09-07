using MediatR;

namespace Explore.Application.Features.TagTypeTags.Requests.Commands;

public sealed record DeleteTagTypeTagsCommand(Guid Id = default) : IRequest<bool>;
