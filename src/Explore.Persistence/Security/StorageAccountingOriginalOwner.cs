using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence.Security;

/// <summary>Owns no data grant; binds source custody to the original ordinary accounting attempt.</summary>
internal sealed class StorageAccountingOriginalOwner(
    ExploreDbContext context, ExploreDbContextExecutionLease selection,
    ISerializableAttemptOwner owner) : IStorageAccountingOriginalOwner
{
    public IStorageAccountingOwnerLease Capture()
    {
        selection.RequireOriginalOwner(context);
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || selection.PolicyPurpose != PolicyMutationPurpose.Standard || selection.SelectedTenantId is not null
            || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
            || owner is not EfCoreUnitOfWork physical)
            throw new UnauthorizedAccessException("Accounting custody requires its idle ordinary Global owner.");
        physical.RequireStorageAccountingOwner(context);
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        var subject = selection.HumanScope?.Subject;
        return new Lease(context, () =>
        {
            selection.RequireOriginalOwner(context);
            if (context.ContextId != contextId || !connection.Matches(context)
                || !ReferenceEquals(selection.HumanScope?.Subject, subject))
                throw new UnauthorizedAccessException("The original accounting source owner changed.");
        });
    }

    private sealed class Lease(ExploreDbContext context, Action requireSelection) : IStorageAccountingOwnerLease
    {
        private DbTransaction? transaction;

        public void Acquire()
        {
            requireSelection();
            if (transaction is not null || !context.HumanCommitAttemptOwnsTransaction
                || context.Database.CurrentTransaction?.GetDbTransaction() is not { } current
                || current.IsolationLevel != IsolationLevel.Serializable)
                throw new UnauthorizedAccessException("Accounting sources require one original physical attempt.");
            transaction = current;
            Validate();
        }

        public void Validate()
        {
            requireSelection();
            if (transaction is null || !context.HumanCommitAttemptOwnsTransaction
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !RelationalConnectionIdentity.Capture(context).Owns(transaction))
                throw new UnauthorizedAccessException("Accounting sources cannot move to another physical transaction.");
        }
    }
}

public static class StorageAccountingOriginalOwnerRegistration
{
    public static IServiceCollection AddStorageAccountingOriginalOwner(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageAccountingOriginalOwner, StorageAccountingOriginalOwner>();
        return services;
    }
}
