using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Repairs all instance accounting scopes in the original selected single-attempt lease.
/// Authority and transaction ownership stay here; provider primitives execute the custody projection.
/// </summary>
public sealed class InstanceStorageAdministration(
    IGlobalExploreDbContextFactory globalFactory,
    IPolicyMutationExecutionScopeFactory scopeFactory) : IInstanceStorageAdministration
{
    public async Task<IReadOnlyList<InstanceStorageProviderTotals>> ReadUsageAsync(CancellationToken cancellationToken)
    {
        await using var global = await globalFactory.CreateDbContextAsync(cancellationToken);
        return await InstanceStorageAccountingProjection.ReadAsync(global, cancellationToken);
    }

    public Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageCommand command, CancellationToken cancellationToken) =>
        RecalculateAsync(command, cancellationToken);

    public Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateUsageAsync(
        RecalculateInstanceStorageUsageDuringSetupCommand command, CancellationToken cancellationToken) =>
        RecalculateAsync(command, cancellationToken);

    private async Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateAsync(
        object command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await using var selected = await scopeFactory.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var services = selected.Services;
        var global = services.GetRequiredService<ExploreDbContext>();
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var owner = services.GetRequiredService<ISerializableAttemptOwner>();
        lease.RequireOriginalOwner(global);
        if (global.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || global.TenantFilterTenantId is not null || global.IsTenantFilterBypassed
            || global.IsConfigurationInstanceImportOwner || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || owner is not EfCoreUnitOfWork physicalOwner)
            throw new UnauthorizedAccessException("Accounting requires its idle ordinary Global owner.");
        physicalOwner.RequireStorageAccountingOwner(global);
        InstanceStorageAccountingProjection.RequireSupportedProvider(global);
        var contextId = global.ContextId;
        var connection = RelationalConnectionIdentity.Capture(global);
        var subject = lease.HumanScope?.Subject;
        if (command is RecalculateInstanceStorageUsageCommand human
            && (subject is null || human.UserId == Guid.Empty || human.UserId != subject.PrincipalId))
            throw new UnauthorizedAccessException("Human accounting requires its actual nonempty original actor.");
        using var admission = await services.GetRequiredService<IStorageAccountingOriginalAuthority>()
            .PrepareAsync(command, cancellationToken);
        void RequireSelection()
        {
            lease.RequireOriginalOwner(global);
            if (global.ContextId != contextId || !connection.Matches(global)
                || !ReferenceEquals(lease.HumanScope?.Subject, subject))
                throw new UnauthorizedAccessException("The original accounting selection changed.");
        }
        RequireSelection();
        return await owner.ExecuteAsync(async ct =>
        {
            RequireSelection();
            var transaction = global.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new UnauthorizedAccessException("Accounting requires its original transaction.");
            void RequireOwner()
            {
                RequireSelection();
                if (!global.HumanCommitAttemptOwnsTransaction
                    || !ReferenceEquals(global.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                    || !connection.Owns(transaction)
                    || transaction.IsolationLevel != System.Data.IsolationLevel.Serializable)
                    throw new UnauthorizedAccessException("Accounting lost its original single-attempt owner.");
            }
            RequireOwner();
            await admission.AcquireAsync(command, ct);
            RequireOwner();
            // This lower registry fences a physical attempt, not an administrator grant.
            // A setup request retains its actual ticket source, including an ordinary
            // authenticated subject when that is what the controller accepted.
            global.RegisterHumanCommitAdmission(async token =>
            {
                RequireOwner();
                await admission.ValidateSourcesAsync(token);
                RequireOwner();
            }, () =>
            {
                RequireOwner();
                admission.ValidateTime();
            });

            return await InstanceStorageAccountingProjection.RecalculateAsync(global, ct);
        }, cancellationToken);
    }
}
