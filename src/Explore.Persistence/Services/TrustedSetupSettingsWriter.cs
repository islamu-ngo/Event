using System.Data;
using System.Collections.Immutable;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Application.Settings;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Closed setup-only writer. Public settings repositories remain human-only. The immutable
/// capsule is consumed on the existing Serializable owner without a new login or transaction.
/// </summary>
public sealed partial class TrustedSetupSettingsWriter(
    ExploreDbContext context, ITrustedSetupSettingsAuthority authority,
    ISettingMutationLock mutationLock, IUnitOfWork unitOfWork,
    PostgresNativeQueueEnrollment? nativeEnrollment = null) : ITrustedSetupSettingsWriter
{
    internal static ImmutableArray<string> Keys { get; } =
    [
        GovernanceSettingKeys.Deployment.Mode,
        GovernanceSettingKeys.Branding.DisplayName,
        GovernanceSettingKeys.Branding.SupportEmail,
        GovernanceSettingKeys.Domains.PublicBaseUrl,
        GovernanceSettingKeys.Domains.AdminHost,
        GovernanceSettingKeys.Localization.DefaultLanguage,
        GovernanceSettingKeys.PublicExperience.Mode,
        GovernanceSettingKeys.PublicExperience.EventCatalogLabel,
        GovernanceSettingKeys.PublicExperience.HomeBlocks,
        GovernanceSettingKeys.PublicExperience.Ctas,
        GovernanceSettingKeys.Routing.DefaultPublicHomePage,
        InstanceOperatorIdentitySettingKeys.OperatorIdentity
    ];

    /// <summary>Observes presence only; setup source admission remains exclusive to ExecuteAsync.</summary>
    public async Task<bool> HasOperatorIdentityAsync(IUnitOfWork originalOwner, CancellationToken cancellationToken)
    {
        var requireOwner = CaptureOwner(originalOwner);
        requireOwner();
        await RequireStorageCurrentAsync(cancellationToken);
        requireOwner();
        bool exists = await context.SystemSettings.AsNoTracking().AnyAsync(
            setting => setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity, cancellationToken);
        requireOwner();
        await RequireStorageCurrentAsync(cancellationToken);
        requireOwner();
        return exists;
    }

    private Action CaptureOwner(IUnitOfWork originalOwner)
    {
        if (!ReferenceEquals(originalOwner, unitOfWork) || unitOfWork is not EfCoreUnitOfWork original)
            throw new UnauthorizedAccessException("Setup must retain its original enrollment unit of work.");
        original.RequireTrustedSetupSettingsOwner(context);
        var contextId = context.ContextId;
        var tenant = context.TenantFilterTenantId;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Setup effects require the original transaction.");
        var connection = RelationalConnectionIdentity.Capture(context);
        var backend = connection.BackendProcessId;
        return () =>
        {
            original.RequireTrustedSetupSettingsOwner(context);
            context.ValidateTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection.Matches(context)
                || connection.State != ConnectionState.Open
                || connection.BackendProcessId != backend
                || !connection.Owns(transaction)
                || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new UnauthorizedAccessException("Setup effects cannot move between owners or transactions.");
        };
    }

    private Task RequireStorageCurrentAsync(CancellationToken cancellationToken) =>
        context.Database.IsNpgsql()
            ? PostgresSetupLiveStoragePermissions.RequireTrustedSettingsCurrentAsync(context,
                nativeEnrollment ?? throw new UnauthorizedAccessException("Setup requires its actual PostgreSQL enrollment."),
                cancellationToken)
            : Task.CompletedTask;

    public async Task<T> ExecuteAsync<T>(TrustedSetupSettingsApply<T> apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);
        apply.ClaimOnce();
        var RequireOwner = CaptureOwner(apply.OriginalOwner);
        RequireOwner();
        await RequireStorageCurrentAsync(cancellationToken);
        RequireOwner();
        if (apply.Effects.IsDefaultOrEmpty
            || apply.Effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != apply.Effects.Length
            || apply.Effects.Any(effect => !Keys.Contains(effect.Key, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("Setup only admits its fixed compiled effects.");
        var lease = await authority.CaptureAsync(apply.Source, cancellationToken);
        apply.RequireActive();
        if (context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Setup cannot flush unrelated pending changes.");
        foreach (var effect in apply.Effects)
        {
            await mutationLock.ExecuteAsync(effect.Key, async token =>
            {
                RequireOwner();
                apply.RequireActive();
                await lease.RequireCurrentAsync(token);
                await RequireStorageCurrentAsync(token);
                RequireOwner();
                apply.RequireActive();
                var existing = await context.SystemSettings
                    .SingleOrDefaultAsync(setting => setting.SettingKey == effect.Key, token);
                // The operator document is create-only in setup. An existing administrator
                // document must not be overwritten by a raced bootstrap plan.
                if (effect.Key == InstanceOperatorIdentitySettingKeys.OperatorIdentity && existing is not null)
                    throw new UnauthorizedAccessException("Setup cannot replace an existing operator identity.");
                if (existing is null)
                {
                    existing = new SystemSetting
                    {
                        Id = effect.Id,
                        SettingKey = effect.Key,
                        Value = effect.Value,
                        CreatedAt = effect.CreatedAt
                    };
                    context.SystemSettings.Add(existing);
                }
                existing.Value = effect.Value;
                existing.ValueType = effect.ValueType;
                existing.IsLocked = effect.IsLocked;
                existing.AllowedValues = effect.AllowedValues;
                existing.Description = effect.Description;
                existing.Category = effect.Category;
                existing.DisplayOrder = effect.DisplayOrder;
                existing.UpdatedAt = effect.UpdatedAt;
                await context.SaveChangesAsync(token);
                RequireOwner();
                await RequireStorageCurrentAsync(token);
                RequireOwner();
                apply.RequireActive();
                return true;
            }, cancellationToken);
        }
        async Task RequireCompletionSource(CancellationToken token)
        {
            RequireOwner();
            apply.RequireActive();
            await lease.RequireCurrentAsync(token);
            await RequireStorageCurrentAsync(token);
            RequireOwner();
            apply.RequireActive();
        }
        var completion = new CompletionEffects<T>(context, apply, RequireCompletionSource);
        T result;
        try
        {
            await RequireCompletionSource(cancellationToken);
            result = await apply.CompleteAsync(completion, cancellationToken);
            await completion.RequireCompletedAsync(cancellationToken);
            await RequireStorageCurrentAsync(cancellationToken);
            RequireOwner();
        }
        finally
        {
            completion.Close();
        }
        apply.RequireActive();
        async Task RequireTerminal(CancellationToken token)
        {
            RequireOwner();
            await RequireStorageCurrentAsync(token);
            RequireOwner();
            await apply.RequireTerminalAsync(token);
            await lease.RequireCurrentAsync(token);
            await completion.RequireCompletedAsync(token);
            await RequireStorageCurrentAsync(token);
            RequireOwner();
        }
        await RequireTerminal(cancellationToken);
        lease.RequireCurrentTime();
        context.RegisterTrustedSetupSettingsAdmission(RequireTerminal, () =>
        {
            RequireOwner();
            lease.RequireCurrentTime();
        });
        return result;
    }

}
