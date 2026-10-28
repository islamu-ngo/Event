using System.Runtime.ExceptionServices;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Operations.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Operations;

internal sealed class MutationScopeCommandHandler<TCommand, TResult>(
    IServiceProvider provider, Type implementation) : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken)
    {
        if (LocalIdentityOperationScope.Handles(typeof(TCommand)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var identity = await provider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
                .CreateAsync(cancellationToken);
            await AuthorizeAsync(identity.Services, command, cancellationToken);
            await PrepareHumanOwnerAsync(identity.Services, cancellationToken);
            return await ExecuteCoreAsync(identity.Services, command, cancellationToken);
        }
        var selection = await PolicyMutationCommandScope.SelectAsync(command, provider, cancellationToken);
        if (selection is null && ModerationMutationCommandScope.Handles(typeof(TCommand)))
            selection = PolicyMutationScope.Instance;
        if (selection is null)
        {
            await AuthorizeAsync(provider, command, cancellationToken);
            return await ExecuteHumanOwnerAsync(provider, command, cancellationToken);
        }
        if (FooterOperationScope.Handles(typeof(TCommand)))
        {
            await using var footer = await provider.GetRequiredService<IFooterExecutionScopeFactory>()
                .CreateAsync(selection, cancellationToken);
            await AuthorizeAsync(footer.Services, command, cancellationToken);
            return await footer.Services.GetRequiredService<IFooterOperationExecution>()
                .ExecuteAsync(command, token => ExecuteCoreAsync(footer.Services, command, token), cancellationToken);
        }
        await using var scope = await provider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(selection, cancellationToken);
        await AuthorizeAsync(scope.Services, command, cancellationToken);
        return await ExecuteHumanOwnerAsync(scope.Services, command, cancellationToken);
    }

    private static Task AuthorizeAsync(
        IServiceProvider services, TCommand command, CancellationToken cancellationToken) =>
        services.GetRequiredService<RequestAuthorization<TCommand>>().AuthorizeAsync(
            command, services.GetRequiredService<ILogger<RequestAuthorization<TCommand>>>(), cancellationToken);

    private bool RequiresHumanAdministratorOwner =>
        implementation.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter =>
            parameter.ParameterType == typeof(IInstanceAdministratorCommitAuthority)
            || parameter.ParameterType == typeof(ITenantAdministratorCommitAuthority)));

    private ValueTask PrepareHumanOwnerAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        RequiresHumanAdministratorOwner
            ? services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private async Task<TResult> ExecuteHumanOwnerAsync(
        IServiceProvider services, TCommand command, CancellationToken cancellationToken)
    {
        // A delegating command does not own the ambient database attempt. Resolve
        // custody only for the direct administrator owner after final selection.
        if (!RequiresHumanAdministratorOwner)
            return await ExecuteCoreAsync(services, command, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var gate = services.GetRequiredService<Func<IHumanSecuritySourceGate>>()();
        var ownedGate = !gate.TryRequireCurrent()
            ? await gate.AcquireAsync(cancellationToken) : null;
        List<Exception> failures = [];
        Exception? primaryFailure = null;
        TResult result = default!;
        try
        {
            gate.RequireCurrent();
            await PrepareHumanOwnerAsync(services, cancellationToken);
            gate.RequireCurrent();
            result = await ExecuteCoreAsync(services, command, cancellationToken);
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            failures.Add(failure);
        }
        // Settlement proof and owned cleanup must not replace the handler's
        // uncertain outcome. Borrowed custody remains its caller's.
        try { gate.RequireCurrent(); }
        catch (Exception failure) { failures.Add(failure); }
        if (ownedGate is not null)
        {
            try { await ownedGate.DisposeAsync(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1)
        {
            var aggregate = new AggregateException("Human command settlement and owned gate cleanup failures.", failures);
            if (primaryFailure is HumanMutationOutcomeUnknownException)
                throw new HumanMutationOutcomeUnknownException(aggregate);
            throw aggregate;
        }
        return result;
    }

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
        var timed = new PerformanceCommandHandlerDecorator<TCommand, TResult>(
            handler, services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<PerformanceCommandHandlerDecorator<TCommand, TResult>>>());
        return await timed.ExecuteAsync(command, cancellationToken);
    }
}
