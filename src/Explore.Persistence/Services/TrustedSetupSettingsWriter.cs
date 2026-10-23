using System.Data;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Services;

/// <summary>
/// Closed setup-only writer. Public settings repositories remain human-only. The immutable
/// capsule is consumed on the existing Serializable owner without a new login or transaction.
/// </summary>
public sealed partial class TrustedSetupSettingsWriter(
    ExploreDbContext context, ITrustedSetupSettingsAuthority authority,
    ISettingMutationLock mutationLock, IUnitOfWork unitOfWork) : ITrustedSetupSettingsWriter
{
    public async Task<T> ExecuteAsync<T>(TrustedSetupSettingsApply<T> apply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);
        apply.ClaimOnce();
        if (!ReferenceEquals(apply.OriginalOwner, unitOfWork) || unitOfWork is not EfCoreUnitOfWork original)
            throw new UnauthorizedAccessException("Setup must retain its original enrollment unit of work.");
        original.RequireTrustedSetupSettingsOwner(context);
        var contextId = context.ContextId;
        var tenant = context.TenantFilterTenantId;
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Setup effects require the original transaction.");
        var connection = RelationalConnectionIdentity.Capture(context);
        void RequireOwner()
        {
            context.ValidateTenantLease();
            if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
                || !ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection.Matches(context)
                || !connection.Owns(transaction)
                || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new UnauthorizedAccessException("Setup effects cannot move between owners or transactions.");
        }
        RequireOwner();
        if (apply.Effects.IsDefaultOrEmpty
            || apply.Effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != apply.Effects.Length
            || apply.Effects.Any(effect => !OwnsKey(effect.Key)))
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
                apply.RequireActive();
                return true;
            }, cancellationToken);
        }
        async Task RequireCompletionSource(CancellationToken token)
        {
            RequireOwner();
            apply.RequireActive();
            await lease.RequireCurrentAsync(token);
            RequireOwner();
            apply.RequireActive();
        }
        var completion = new CompletionEffects<T>(context, apply, RequireCompletionSource);
        T result;
        try
        {
            result = await apply.CompleteAsync(completion, cancellationToken);
            await completion.RequireCompletedAsync(cancellationToken);
        }
        finally
        {
            completion.Close();
        }
        apply.RequireActive();
        async Task RequireTerminal(CancellationToken token)
        {
            RequireOwner();
            await apply.RequireTerminalAsync(token);
            await lease.RequireCurrentAsync(token);
            await completion.RequireCompletedAsync(token);
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

    private static bool OwnsKey(string key) => key is
        GovernanceSettingKeys.Deployment.Mode
        or GovernanceSettingKeys.Branding.DisplayName
        or GovernanceSettingKeys.Branding.SupportEmail
        or GovernanceSettingKeys.Domains.PublicBaseUrl
        or GovernanceSettingKeys.Domains.AdminHost
        or GovernanceSettingKeys.Localization.DefaultLanguage
        or GovernanceSettingKeys.PublicExperience.Mode
        or GovernanceSettingKeys.PublicExperience.EventCatalogLabel
        or GovernanceSettingKeys.PublicExperience.HomeBlocks
        or GovernanceSettingKeys.PublicExperience.Ctas
        or GovernanceSettingKeys.Routing.DefaultPublicHomePage
        or InstanceOperatorIdentitySettingKeys.OperatorIdentity;
}
