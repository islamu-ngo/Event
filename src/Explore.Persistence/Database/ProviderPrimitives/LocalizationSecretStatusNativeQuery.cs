using System.Data;
using Explore.Domain.Secrets;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class LocalizationSecretStatusNativeQuery
{
    internal static async Task<bool> ReadAsync(
        ExploreDbContext context, IServiceProvider services, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.RequireBoundTenantLease();
        var contextId = context.ContextId;
        var tenant = context.TenantFilterTenantId;
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        bool openedHere = connection.State != ConnectionState.Open;
        if (context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw new UnauthorizedAccessException("localization_status_ordinary_owner_required");
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
                throw new UnauthorizedAccessException("localization_status_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresLocalizationSecretStatusReadPermissions.RequireReadyAsync(context,
                context.Database.IsNpgsql() ? services.GetService<PostgresNativeQueueEnrollment>() : null,
                cancellationToken);
            RequireOriginal();
            var bindings = context.SecretBindings.IgnoreAutoIncludes().AsNoTracking();
            // Preserve the retired PostgreSQL read's ordinal key/qualifier comparison.
            bindings = context.Database.IsNpgsql()
                ? bindings.Where(binding =>
                    EF.Functions.Collate(binding.SettingKey, "C") == SecretDefinitionRegistry.Keys.Localization.TmsApiKey
                    && EF.Functions.Collate(binding.Qualifier, "C") == string.Empty)
                : bindings.Where(binding =>
                    binding.SettingKey == SecretDefinitionRegistry.Keys.Localization.TmsApiKey
                    && binding.Qualifier == string.Empty);
            bool configured = await bindings.AnyAsync(binding =>
                binding.SettingScopeId == 1 && binding.ScopeId == null
                    || tenant != null && binding.SettingScopeId == 2 && binding.ScopeId == tenant,
                cancellationToken);
            RequireOriginal();
            return configured;
        }
        finally
        {
            connection.StateChange -= Changed;
            if (openedHere && !interrupted && connection.Matches(context)
                && connection.State == ConnectionState.Open && connection.BackendProcessId == backend)
                await context.Database.CloseConnectionAsync();
        }
    }
}
