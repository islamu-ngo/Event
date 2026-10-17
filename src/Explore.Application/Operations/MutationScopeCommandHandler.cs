using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Operations;

internal sealed class MutationScopeCommandHandler<TCommand, TResult>(
    IServiceProvider provider, Type implementation) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        var selection = await PolicyMutationCommandScope.SelectAsync(command, provider, cancellationToken);
        if (selection is null && ModerationMutationCommandScope.Handles(typeof(TCommand)))
            selection = PolicyMutationScope.Instance;
        if (selection is null)
        {
            await PrepareHumanOwnerAsync(provider, cancellationToken);
            return await ExecuteCoreAsync(provider, command, cancellationToken);
        }
        if (FooterOperationScope.Handles(typeof(TCommand)))
        {
            await using var footer = await provider.GetRequiredService<IFooterExecutionScopeFactory>()
                .CreateAsync(selection, cancellationToken);
            return await footer.Services.GetRequiredService<IFooterOperationExecution>()
                .ExecuteAsync(command, token => ExecuteCoreAsync(footer.Services, command, token), cancellationToken);
        }
        await using var scope = await provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(selection, cancellationToken);
        await PrepareHumanOwnerAsync(scope.Services, cancellationToken);
        return await ExecuteCoreAsync(scope.Services, command, cancellationToken);
    }

    private ValueTask PrepareHumanOwnerAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        implementation.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(Contracts.Persistence.IInstanceAdministratorCommitAuthority)
            || parameter.ParameterType == typeof(Contracts.Persistence.ITenantAdministratorCommitAuthority)))
            ? services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private async Task<TResult> ExecuteCoreAsync(
        IServiceProvider services, TCommand command, CancellationToken cancellationToken)
    {
        var guard = services.GetRequiredService<OperationConstructionGuard>();
        var contract = typeof(ICommandHandler<TCommand, TResult>);
        guard.Enter(contract);
        ICommandHandler<TCommand, TResult> handler;
        try
        {
            handler = (ICommandHandler<TCommand, TResult>)services.GetRequiredService(implementation);
        }
        finally
        {
            guard.Exit(contract);
        }
        return await handler.ExecuteAsync(command, cancellationToken);
    }
}
