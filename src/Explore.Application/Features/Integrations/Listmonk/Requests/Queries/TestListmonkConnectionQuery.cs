using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Integrations.Listmonk.Requests.Queries;

public sealed record TestListmonkConnectionQuery : IQuery<BaseCommandResponse<Guid>>
{
}
