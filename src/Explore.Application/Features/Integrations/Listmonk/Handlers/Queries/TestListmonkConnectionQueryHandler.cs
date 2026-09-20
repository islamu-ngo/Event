using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Integrations.Listmonk.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Integrations.Listmonk.Handlers.Queries;

public sealed class TestListmonkConnectionQueryHandler(IListmonkConnectionTester connectionTester)
    : IQueryHandler<TestListmonkConnectionQuery, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> QueryAsync(
        TestListmonkConnectionQuery request,
        CancellationToken cancellationToken)
    {
        var connected = await connectionTester.TestConnectionAsync(cancellationToken);
        return connected
            ? BaseCommandResponse.Success(Guid.Empty, "Listmonk connection successful.")
            : BaseCommandResponse.Validation<Guid>(
                ["Listmonk connection failed. Check provider settings and API credentials."],
                "Listmonk connection failed. Check provider settings and API credentials.");
    }
}
