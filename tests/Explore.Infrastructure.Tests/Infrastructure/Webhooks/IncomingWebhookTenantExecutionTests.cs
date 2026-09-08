using System.Collections.Concurrent;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Webhooks;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Infrastructure.Webhooks;

public sealed class IncomingWebhookTenantExecutionTests
{
    [Test]
    public async Task ConcurrentClaims_UseIndependentTenantAndNarrowMachinePrincipalScopes()
    {
        var state = new ExecutionState(expectedConcurrentExecutions: 2);
        await using var provider = CreateProvider(state);
        var executor = new IncomingWebhookTenantExecutor(provider.GetRequiredService<IServiceScopeFactory>());
        var tenantA = Guid.CreateVersion7();
        var tenantB = Guid.CreateVersion7();

        var results = await Task.WhenAll(
            executor.ExecuteAsync(CreateClaim(tenantA), CancellationToken.None),
            executor.ExecuteAsync(CreateClaim(tenantB), CancellationToken.None));

        await Assert.That(results.All(result => result.Outcome == IncomingWebhookClaimExecutionOutcome.Completed)).IsTrue();
        await Assert.That(state.Observations.Select(observation => observation.TenantId)).IsEquivalentTo(new[] { tenantA, tenantB });
        await Assert.That(state.Observations.All(observation =>
            observation.PrincipalTenantId == observation.TenantId &&
            observation.PrincipalScopes.SequenceEqual([InternalMachineScopes.ProcessIncomingWebhook]) &&
            observation.Action == AuthorizationActions.Webhooks.ProcessIncoming)).IsTrue();
        await Assert.That(state.ScopedAccessors.All(accessors =>
            accessors.TenantAccessor.TenantId is null && accessors.MachineAccessor.Current is null)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(
            [InternalMachineScopes.ProcessIncomingWebhook],
            ResourceKinds.Webhook,
            AuthorizationActions.Webhooks.ProcessIncoming)).IsTrue();
        await Assert.That(MachineScopeMapping.ScopesPermit(
            [InternalMachineScopes.ProcessIncomingWebhook],
            ResourceKinds.Webhook,
            AuthorizationActions.Webhooks.ManageProvider)).IsFalse();
    }

    [Test]
    [Arguments(ProcessingExit.Cancellation)]
    [Arguments(ProcessingExit.Exception)]
    public async Task NonSuccessExit_AlwaysClearsTenantAndMachinePrincipal(ProcessingExit exit)
    {
        var state = new ExecutionState(expectedConcurrentExecutions: 1) { Exit = exit };
        await using var provider = CreateProvider(state);
        var executor = new IncomingWebhookTenantExecutor(provider.GetRequiredService<IServiceScopeFactory>());
        using var cancellation = new CancellationTokenSource();
        if (exit == ProcessingExit.Cancellation)
        {
            cancellation.Cancel();
        }

        try
        {
            await executor.ExecuteAsync(CreateClaim(Guid.CreateVersion7()), cancellation.Token);
        }
        catch (OperationCanceledException) when (exit == ProcessingExit.Cancellation)
        {
        }
        catch (InvalidOperationException) when (exit == ProcessingExit.Exception)
        {
        }

        await Assert.That(state.ScopedAccessors).HasSingleItem();
        var accessors = state.ScopedAccessors.Single();
        await Assert.That(accessors.TenantAccessor.TenantId).IsNull();
        await Assert.That(accessors.MachineAccessor.Current).IsNull();
    }

    private static ServiceProvider CreateProvider(ExecutionState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton(state);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider => provider.GetRequiredService<TenantContextAccessor>());
        services.AddScoped<MachinePrincipalAccessor>();
        services.AddScoped<IMachinePrincipalAccessor>(provider => provider.GetRequiredService<MachinePrincipalAccessor>());
        services.AddScoped<IMachinePrincipalExecutionAccessor>(provider => provider.GetRequiredService<MachinePrincipalAccessor>());
        services.AddScoped<IAuthorizationProvider, RecordingAuthorizationProvider>();
        services.AddScoped<IIncomingWebhookProcessingService, RecordingProcessingService>();
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static IncomingWebhookClaim CreateClaim(Guid tenantId) =>
        new(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), 1, 1);

    public enum ProcessingExit
    {
        Complete = 1,
        Cancellation = 2,
        Exception = 3
    }

    private sealed class ExecutionState(int expectedConcurrentExecutions)
    {
        private readonly TaskCompletionSource _allEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _entered;

        public ConcurrentBag<ExecutionObservation> Observations { get; } = [];
        public ConcurrentBag<ScopedAccessors> ScopedAccessors { get; } = [];
        public ProcessingExit Exit { get; init; } = ProcessingExit.Complete;

        public async Task SynchronizeAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _entered) == expectedConcurrentExecutions)
            {
                _allEntered.TrySetResult();
            }

            await _allEntered.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed record ExecutionObservation(
        Guid TenantId,
        Guid? PrincipalTenantId,
        IReadOnlyList<string> PrincipalScopes,
        string Action);

    private sealed record ScopedAccessors(
        ITenantContextAccessor TenantAccessor,
        IMachinePrincipalAccessor MachineAccessor);

    private sealed class RecordingAuthorizationProvider(
        ITenantContextAccessor tenantAccessor,
        IMachinePrincipalAccessor machineAccessor,
        ExecutionState state) : IAuthorizationProvider
    {
        public async Task<AuthorizationDecision> AuthorizeAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            var tenantId = tenantAccessor.TenantId ?? Guid.Empty;
            var principal = machineAccessor.Current;
            state.ScopedAccessors.Add(new ScopedAccessors(tenantAccessor, machineAccessor));
            state.Observations.Add(new ExecutionObservation(
                tenantId,
                principal?.TenantId,
                principal?.Scopes ?? [],
                request.Action));
            await state.SynchronizeAsync(cancellationToken);
            var hasMatchingTenantAttribute =
                request.Facts is TenantScopedAuthorizationFacts tenantFacts &&
                tenantFacts.TenantId == tenantId;
            var allowed = request.ResourceKind == ResourceKinds.Webhook &&
                          request.Action == AuthorizationActions.Webhooks.ProcessIncoming &&
                          principal is not null &&
                          principal.TenantId == tenantId &&
                          hasMatchingTenantAttribute;
            return allowed
                ? AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local)
                : AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local);
        }

        public Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
            IReadOnlyList<AuthorizationRequest> requests,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AuthorizationDecision>>(requests
                .Select(_ => AuthorizationDecision.Allow(AuthorizationProviderMetadata.Local))
                .ToArray());
    }

    private sealed class RecordingProcessingService(ExecutionState state) : IIncomingWebhookProcessingService
    {
        public Task<IncomingWebhookClaimExecutionResult> ProcessAsync(
            IncomingWebhookClaim claim,
            CancellationToken cancellationToken)
        {
            return state.Exit switch
            {
                ProcessingExit.Complete => Task.FromResult(IncomingWebhookClaimExecutionResult.Completed()),
                ProcessingExit.Cancellation => Task.FromCanceled<IncomingWebhookClaimExecutionResult>(cancellationToken),
                ProcessingExit.Exception => Task.FromException<IncomingWebhookClaimExecutionResult>(
                    new InvalidOperationException("Injected processing failure.")),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }
}
