using System.Security.Claims;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Operations;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

/// <summary>Exercises owner selection before the real onboarding dependency graph can be constructed.</summary>
public sealed class InstanceOnboardingOwnerSelectionTests
{
    [Test]
    public async Task LocalOnboardingMustSelectGlobalBeforeConstructingItsCredentialOrCompletionGraph()
    {
        var services = OperationCompositionTests.Services(
            typeof(CompleteLocalInstanceOnboardingCommand), typeof(CompleteLocalInstanceOnboardingCommandHandler));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = provider.CreateAsyncScope();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await request.ServiceProvider
                .GetRequiredService<ICommandHandler<CompleteLocalInstanceOnboardingCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(LocalRequest(), CancellationToken.None));

        // Neither graph is installed: only the original Global scope selector may
        // be requested first. A Local-writer-first graph has the wrong outer owner.
        await Assert.That(failure.Message.Contains(
            typeof(IPolicyMutationExecutionScopeFactory).FullName!, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LocalOnboardingSelectsGlobalWithoutRebindingTheAmbientTenant(bool hasAmbientTenant)
    {
        var services = new ServiceCollection();
        Guid? tenant = hasAmbientTenant ? Guid.CreateVersion7() : null;
        if (tenant.HasValue)
            services.AddSingleton<ITenantContextAccessor>(new FixedTenantFacts(tenant.Value));
        await using var provider = services.BuildServiceProvider();

        var selection = await PolicyMutationCommandScope.SelectAsync(LocalRequest(), provider, CancellationToken.None);

        await Assert.That(selection).IsEqualTo(PolicyMutationScope.Instance);
        await Assert.That(provider.GetService<ITenantContextAccessor>()?.TenantId).IsEqualTo(tenant);
    }

    [Test]
    public async Task MissingSelectedOwnerRejectsBeforeConstructingOnboardingBusinessDependencies()
    {
        var services = OperationCompositionTests.Services(
            typeof(CompleteInstanceOnboardingCommand), typeof(CompleteInstanceOnboardingCommandHandler));
        services.AddSingleton<IAuthorizationProvider>(new OperationAuthorizationTests.Policy(
            AuthorizationDecision.Deny(AuthorizationProviderMetadata.Local)));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var requestScope = provider.CreateAsyncScope();

        // The real handler's repositories and completion operation are deliberately absent.
        // Resolving the protected port must not construct them on the ambient Runtime owner.
        var command = requestScope.ServiceProvider
            .GetRequiredService<ICommandHandler<CompleteInstanceOnboardingCommand, BaseCommandResponse<Guid>>>();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.ExecuteAsync(Request(), CancellationToken.None));

        // No synthetic scope supplies successful authority: a missing owner fails closed
        // before any business dependency can be resolved or any onboarding effect can occur.
        await Assert.That(failure.Message.Contains(
            typeof(IPolicyMutationExecutionScopeFactory).FullName!, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InteractiveCompletionSelectsInstanceOwnerRegardlessOfAmbientTenant(bool hasAmbientTenant)
    {
        var services = new ServiceCollection();
        Guid? tenantId = hasAmbientTenant ? Guid.CreateVersion7() : null;
        if (tenantId.HasValue)
            services.AddSingleton<ITenantContextAccessor>(new FixedTenantFacts(tenantId.Value));
        await using var provider = services.BuildServiceProvider();

        var selection = await PolicyMutationCommandScope.SelectAsync(Request(), provider, CancellationToken.None);

        await Assert.That(selection).IsNotNull();
        await Assert.That(selection!.TenantId).IsNull();
        await Assert.That(selection.Purpose).IsEqualTo(PolicyMutationPurpose.Standard);
        await Assert.That(selection).IsEqualTo(PolicyMutationScope.Instance);
        await Assert.That(provider.GetService<ITenantContextAccessor>()?.TenantId).IsEqualTo(tenantId);
    }

    [Test]
    public async Task CancelledInteractiveCompletionCannotSelectAnOwner()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await PolicyMutationCommandScope.SelectAsync(Request(), provider, cancellation.Token));

        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
    }

    private static CompleteInstanceOnboardingCommand Request() => new()
    {
        UserId = Guid.CreateVersion7(),
        Settings = new CompleteInstanceOnboardingRequest()
    };

    private static CompleteLocalInstanceOnboardingCommand LocalRequest() =>
        new(new CompleteLocalInstanceOnboardingRequestDto(), new ClaimsPrincipal());

    private sealed class FixedTenantFacts(Guid tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId => tenantId;
        public bool IsResolved => true;
        public void SetTenant(Guid value) => throw new InvalidOperationException("Ambient tenant facts are immutable.");
        public void BindExecutionTenant(Guid value) => throw new InvalidOperationException("Selection cannot rebind ambient facts.");
        public void Clear() => throw new InvalidOperationException("Selection cannot clear ambient facts.");
    }
}
