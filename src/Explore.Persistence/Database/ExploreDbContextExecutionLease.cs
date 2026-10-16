using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database;

internal sealed class ExploreDbContextExecutionLease
{
    private RelationalConnectionIdentity? _connection;
    private DbContextId _contextId;
    internal ExploreDbContext? Context { get; private set; }
    internal Guid? SelectedTenantId { get; private set; }
    internal PolicyMutationPurpose? PolicyPurpose { get; private set; }
    internal HumanAdministratorCommitScope? HumanScope { get; private set; }
    internal HumanAuthenticationLifetime? HumanLifetime { get; private set; }

    internal void BindHuman(HumanCommitSubject subject, HumanAdministratorCommitPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment, HumanAuthenticationLifetime? lifetime = null)
    {
        if (Context is null || HumanScope is not null)
            throw new InvalidOperationException("A human commit scope requires one original database lease.");
        if (lifetime is not null && lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The validated credential must belong to the resolved human.");
        HumanScope = new(Context, subject, purpose, enrollment);
        HumanLifetime = lifetime;
        Context.HumanCommitAuditSubject = subject;
        Context.HumanCommitContextLease = Context.ContextId;
    }

    internal void Bind(ExploreDbContext context, Guid? selectedTenantId, PolicyMutationPurpose? policyPurpose = null)
    {
        if (Context is not null)
            throw new InvalidOperationException("An execution scope cannot replace its database lease.");
        context.ValidateTenantLease();
        Context = context;
        SelectedTenantId = selectedTenantId;
        PolicyPurpose = policyPurpose;
        _contextId = context.ContextId;
        _connection = context.Database.IsRelational() ? RelationalConnectionIdentity.Capture(context) : null;
    }

    internal void RequireOriginalOwner(ExploreDbContext context)
    {
        context.ValidateTenantLease();
        if (!ReferenceEquals(Context, context) || context.ContextId != _contextId
            || context.TenantFilterTenantId != SelectedTenantId
            || (_connection is not null && !_connection.Matches(context))
            || (_connection is not null && context.Database.CurrentTransaction?.GetDbTransaction() is { } transaction
                && !_connection.Owns(transaction))
            || (HumanScope is { } human && (human.TenantId != SelectedTenantId
                || context.IsTenantFilterBypassed
                || (human.Purpose == HumanAdministratorCommitPurpose.ConfigurationImport)
                    != context.IsConfigurationInstanceImportOwner)))
            throw new InvalidOperationException("An operation must retain its original context, connection and tenant.");
    }
}
