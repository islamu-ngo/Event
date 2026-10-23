using Explore.Application.Contracts.Operations;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Services;
using Explore.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.EventResourceProviderActivation.Handlers.Commands;

public sealed class BindEventResourceProviderCommandHandler(IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<BindEventResourceProviderCommand, EventResourceProviderOperation>
{
    public async Task<EventResourceProviderOperation> ExecuteAsync(BindEventResourceProviderCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
            return await selected.Services.GetRequiredService<EventResourceProviderControlPlane>().BindAsync(command, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}
public sealed class BeginEventResourceProviderOperationCommandHandler(IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<BeginEventResourceProviderOperationCommand, EventResourceProviderOperation>
{
    public async Task<EventResourceProviderOperation> ExecuteAsync(BeginEventResourceProviderOperationCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
            return await selected.Services.GetRequiredService<EventResourceProviderControlPlane>().BeginAsync(command, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}
public sealed class ActivateEventResourceProviderCommandHandler(IPolicyMutationExecutionScopeFactory scopes)
    : ICommandHandler<ActivateEventResourceProviderCommand, bool>
{
    public async Task<bool> ExecuteAsync(ActivateEventResourceProviderCommand command, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
            return await selected.Services.GetRequiredService<EventResourceProviderControlPlane>().ActivateAsync(command, cancellationToken);
        }
        catch (ArgumentException exception)
        {
            throw new BadRequestException(exception.Message);
        }
    }
}
