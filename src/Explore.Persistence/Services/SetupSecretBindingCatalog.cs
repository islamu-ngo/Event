using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>Reads only the binding ID on the original tenant execution owner.</summary>
public sealed class SetupSecretBindingCatalog(
    ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment = null) : ISetupSecretBindingCatalog
{
    public async Task<Guid?> FindInstanceIdAsync(string bindingKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (bindingKey is not ("setup.signing" or "setup.encryption")) return null;
        context.RequireBoundTenantLease();
        var contextId = context.ContextId;
        var tenant = context.TenantFilterTenantId;
        if (tenant is null || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("setup_binding_tenant_owner_required");
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await context.Database.OpenConnectionAsync(cancellationToken);
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || interrupted || !connection.Matches(context) || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || transaction is not null && !connection.Owns(transaction))
                throw new UnauthorizedAccessException("setup_binding_original_owner_changed");
        }
        bool CanCloseOriginalConnection()
        {
            try
            {
                context.RequireBoundTenantLease();
                return context.ContextId == contextId && context.TenantFilterTenantId == tenant
                    && !context.IsTenantFilterBypassed && !context.IsConfigurationInstanceImportOwner
                    && !interrupted && connection.Matches(context) && connection.State == ConnectionState.Open
                    && connection.BackendProcessId == backend
                    && ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                    && (transaction is null || connection.Owns(transaction));
            }
            catch (InvalidOperationException) { return false; }
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(
                context, context.Database.IsNpgsql() ? enrollment : null, cancellationToken);
            RequireOriginal();
            var bindings = context.SecretBindings.IgnoreAutoIncludes().AsNoTracking();
            bindings = context.Database.IsNpgsql()
                ? bindings.Where(binding => EF.Functions.Collate(binding.SettingKey, "C") == bindingKey
                    && EF.Functions.Collate(binding.Qualifier, "C") == string.Empty)
                : bindings.Where(binding => binding.SettingKey == bindingKey && binding.Qualifier == string.Empty);
            Guid? id = await bindings.Where(binding => binding.SettingScopeId == 1 && binding.ScopeId == null)
                .Select(binding => (Guid?)binding.Id).SingleOrDefaultAsync(cancellationToken);
            RequireOriginal();
            return id;
        }
        finally
        {
            connection.StateChange -= Changed;
            if (openedHere && CanCloseOriginalConnection())
                await context.Database.CloseConnectionAsync();
        }
    }
}
