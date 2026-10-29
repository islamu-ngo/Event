using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Management;
using Explore.Application.Features.Management.Requests.Commands;
using Explore.Application.Management;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Fixtures;

internal sealed partial class AuthenticatedSettingsFixture
{
    internal static async Task<AuthenticatedSettingsFixture> CreateForManagedWorkerAsync(
        Func<ExploreDbContext> createDatabase, PostgreSqlContainerFixture postgres,
        Guid managedInstance, IInterceptor? interceptor = null)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using (var migrator = createDatabase())
        {
            await PostgresTenantCreationPermissions.ApplyAsync(
                migrator, postgres.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, cancellationToken);
            await PostgresProvisioningPolicyPermissions.ApplyAsync(
                migrator, postgres.NativeQueueEnrollment, cancellationToken);
            await PostgresManagedTenantWorkerCreationPermissions.ApplyAsync(
                migrator, postgres.NativeQueueEnrollment, IdentityDatabaseTopology.Colocated, cancellationToken);
        }
        return await CreateAsync(createDatabase, postgres, interceptor, configure: configuration =>
        {
            configuration["ManagedControlPlane:Enabled"] = "true";
            configuration["ManagedControlPlane:ManagedInstanceId"] = managedInstance.ToString("D");
            configuration["ManagedControlPlane:ControlPlaneUrl"] = "https://control.example.test";
            configuration["ManagedControlPlane:MaximumTenantCount"] = "100";
        }, configureServices: services => services.AddSingleton<IConfigureOptions<ManagedControlPlaneOptions>>(
            new ManagedWorkerRegistrationTokenTestAuthority()));
    }

    /// <summary>Command-level instance scheduling, without borrowing this fixture's human request or principal.</summary>
    internal async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> ScheduleManagedWorkerAsync(
        Guid managedInstance, ManagementTenantProvisioningRequestDto request, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var handler = new Explore.Application.Features.Management.Handlers.Commands.ScheduleManagedTenantProvisioningCommandHandler(
            scope.ServiceProvider.GetRequiredService<IManagedTenantProvisioningSchedulingPort>());
        return await handler.ExecuteAsync(new(managedInstance, request), token);
    }

    internal async Task<GenericOutboxClaim> ClaimManagedWorkerAsync(Guid operation, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var claims = await scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
            .ClaimDueAsync(100, DateTime.UtcNow, token);
        return claims.Single(claim => claim.AggregateId == operation);
    }

    internal async Task<bool> ProcessManagedWorkerAsync(GenericOutboxClaim claim, CancellationToken token)
    {
        await using var owner = host.Services.CreateAsyncScope();
        await using var execution = await owner.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, token);
        return await execution.Services.GetRequiredService<ICommandHandler<ProcessManagedTenantProvisioningOperationCommand, bool>>()
            .ExecuteAsync(new(claim.AggregateId, claim.MessageId), token);
    }

    internal async Task<BaseCommandResponse<ManagementTenantProvisioningOperationDto>> CancelManagedWorkerAsync(
        Guid managedInstance, Guid operation, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<CancelManagedTenantProvisioningOperationCommand,
            BaseCommandResponse<ManagementTenantProvisioningOperationDto>>>()
            .ExecuteAsync(new(managedInstance, operation), token);
    }

    internal async Task<OutboxFailureTransition> FailManagedWorkerDeliveryAsync(GenericOutboxClaim claim, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await using var execution = await scope.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, token);
        return await execution.Services.GetRequiredService<IOutboxRepository>().MarkAsFailed(
            claim.MessageId, claim.LeaseExpiresAt, "worker_test_dispatch_failure", true, 0, DateTime.UtcNow, token);
    }

    internal async Task<GenericOutboxClaim> ReadManagedWorkerDeadLetterAsync(GenericOutboxClaim original, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IGenericOutboxAdmission>()
            .ReadClaimAsync(original.MessageId, original.LeaseExpiresAt, OutboxMessageStatus.DeadLettered, token)
            ?? throw new InvalidOperationException("worker_test_real_dead_letter_claim_missing");
    }

    internal async Task<bool> ReconcileManagedWorkerAsync(GenericOutboxClaim claim, CancellationToken token)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await using var execution = await scope.ServiceProvider.GetRequiredService<IGenericOutboxExecutionScopeFactory>()
            .CreateAsync(claim, token);
        return await execution.Services.GetRequiredService<ICommandHandler<ReconcileManagedTenantProvisioningDeadLetterCommand, bool>>()
            .ExecuteAsync(new(claim.AggregateId, claim.MessageId), token);
    }

    /// <summary>Explicit test-only secret authority; normal options validation still runs.</summary>
    private sealed class ManagedWorkerRegistrationTokenTestAuthority : IConfigureOptions<ManagedControlPlaneOptions>
    {
        private readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public void Configure(ManagedControlPlaneOptions options) => options.RegistrationToken = token;
    }
}
