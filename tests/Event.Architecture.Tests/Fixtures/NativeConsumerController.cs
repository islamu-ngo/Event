using Event.Architecture.Tests.Fixtures;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[NonController]
public sealed class NativeConsumerController : ControllerBase
{
    public Task Direct([FromServices] NativeConsumerHandler handler) =>
        handler.ExecuteAsync(new NativeConsumerCommand(), default);

    public Task Protected([FromServices] ICommandHandler<NativeConsumerCommand> handler) =>
        handler.ExecuteAsync(new NativeConsumerCommand(), default);
}

[NonController]
public sealed class NativeConsumerPropertyController : ControllerBase
{
    [FromServices]
    public NativeConsumerHandler Handler { get; set; } = null!;
}
