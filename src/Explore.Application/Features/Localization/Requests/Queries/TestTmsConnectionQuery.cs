using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Localization.Requests.Queries;

public sealed record TestTmsConnectionQuery : IQuery<BaseCommandResponse<Guid>>
{
}
