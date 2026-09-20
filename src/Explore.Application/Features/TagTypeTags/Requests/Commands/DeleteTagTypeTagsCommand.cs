using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.TagTypeTags.Requests.Commands;

public sealed record DeleteTagTypeTagsCommand(Guid Id = default) : ICommand<bool>;
