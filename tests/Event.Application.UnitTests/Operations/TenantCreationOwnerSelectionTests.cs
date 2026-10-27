using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Handlers.Commands.CreateTenant;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class TenantCreationOwnerSelectionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOwnerRejectsBeforeAuthorizationCanAcquireAmbientPrincipalSources(bool unavailable)
    {
        var policy = new OperationAuthorizationTests.Policy(AuthorizationDecision.Deny(
            AuthorizationProviderMetadata.Cerbos,
            unavailable ? AuthorizationDecisionReasonCodes.ProviderUnavailable : AuthorizationDecisionReasonCodes.Denied));
        var services = OperationCompositionTests.Services(
            typeof(CreateTenantCommand), typeof(CreateTenantCommandHandler));
        services.AddSingleton<IAuthorizationProvider>(policy);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var request = provider.CreateAsyncScope();
        var command = request.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateTenantCommand, BaseCommandResponse<Guid>>>();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.ExecuteAsync(new CreateTenantCommand
            {
                TenantDto = new CreateTenantDto { FullName = "Owner selection", Slug = "owner-selection" }
            }, CancellationToken.None));

        await Assert.That(failure.Message.Contains(
            typeof(IPolicyMutationExecutionScopeFactory).FullName!, StringComparison.Ordinal)).IsTrue();
        await Assert.That(policy.Observed).IsNull();
    }
}
