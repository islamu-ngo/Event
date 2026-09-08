using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Integrations.Listmonk.Requests.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.Integrations.Listmonk.Handlers.Commands;

public sealed class TestListmonkConnectionCommandHandler(IListmonkConnectionTester connectionTester)
    : IRequestHandler<TestListmonkConnectionCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        TestListmonkConnectionCommand request,
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
