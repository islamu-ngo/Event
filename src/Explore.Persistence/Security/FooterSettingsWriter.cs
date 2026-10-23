using System.Data;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Footer.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public static class FooterSettingsWriterRegistration
{
    public static IServiceCollection AddFooterSettingsWriter(this IServiceCollection services)
    {
        services.AddScoped<IFooterSettingsWriter, FooterSettingsWriter>();
        services.AddScoped<IFooterExecutionScopeFactory, FooterExecutionScopeFactory>();
        return services;
    }
}

internal sealed class FooterSettingsWriter(
    ExploreDbContext context, ExploreDbContextExecutionLease lease,
    IFooterOperationExecution execution, TimeProvider clock) : IFooterSettingsWriter
{
    public async Task WriteGovernanceAsync(UpdateFooterGovernanceSettingsCommand original, CancellationToken cancellationToken)
    {
        var subject = await RequireAsync(original, null, cancellationToken);
        (SettingDefinition Definition, OptionalUpdate<bool> Value)[] changes =
        [
            (FooterSettingDefinitions.LockTenantTemplate, original.Patch.LockTenantTemplate),
            (FooterSettingDefinitions.LockTenantLinkGroups, original.Patch.LockTenantLinkGroups),
            (FooterSettingDefinitions.LockTenantSocialLinks, original.Patch.LockTenantSocialLinks),
            (FooterSettingDefinitions.LockTenantDescription, original.Patch.LockTenantDescription),
            (FooterSettingDefinitions.LockTenantCopyright, original.Patch.LockTenantCopyright)
        ];
        foreach (var (definition, value) in changes.Where(change => change.Value.HasValue).OrderBy(change => change.Definition.Key, StringComparer.Ordinal))
        {
            var row = await context.SystemSettings.SingleOrDefaultAsync(setting => setting.SettingKey == definition.Key, cancellationToken);
            if (row is null)
            {
                row = new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = definition.Key,
                    Value = SettingValueSerializer.Serialize(value.Value),
                    ValueType = definition.ValueType,
                    Category = definition.Category,
                    Description = definition.Description,
                    CreatedBy = subject.PrincipalId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime
                };
                context.SystemSettings.Add(row);
            }
            else
            {
                row.Value = SettingValueSerializer.Serialize(value.Value);
                row.UpdatedBy = subject.PrincipalId;
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        await RequireAsync(original, null, cancellationToken);
    }

    public async Task WriteTenantAsync(PatchTenantFooterSettingsCommand original,
        IReadOnlyList<TenantSettingOverrideUpsert> writes, CancellationToken cancellationToken)
    {
        Guid tenant = lease.SelectedTenantId
            ?? throw new UnauthorizedAccessException("Footer tenant writes require their original tenant owner.");
        var subject = await RequireAsync(original, tenant, cancellationToken);
        foreach (var write in writes.OrderBy(write => write.SettingKey, StringComparer.Ordinal))
        {
            if (write.IsLocked || !FooterSettingDefinitions.All.Any(definition =>
                    definition.Key == write.SettingKey && definition.MaxScope == SettingScope.Tenant))
                throw new UnauthorizedAccessException("Only Footer tenant scalar values belong to this writer.");
            var row = await context.Set<TenantSetting>().SingleOrDefaultAsync(setting =>
                setting.TenantId == tenant && setting.SettingKey == write.SettingKey, cancellationToken);
            if (row is null)
            {
                row = new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    SettingKey = write.SettingKey,
                    Value = write.Value,
                    IsLocked = false,
                    CreatedBy = subject.PrincipalId,
                    CreatedAt = clock.GetUtcNow().UtcDateTime
                };
                context.Set<TenantSetting>().Add(row);
            }
            else
            {
                row.Value = write.Value;
                row.IsLocked = false;
                row.UpdatedBy = subject.PrincipalId;
                row.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }
        }
        await context.SaveChangesAsync(cancellationToken);
        await RequireAsync(original, tenant, cancellationToken);
    }

    private async Task<HumanCommitSubject> RequireAsync(object original, Guid? tenant, CancellationToken token)
    {
        var subject = await execution.RequireOwnedAsync(original, token)
            ?? throw new UnauthorizedAccessException("The actual human Footer producer is required.");
        lease.RequireOriginalOwner(context);
        if (!ReferenceEquals(lease.HumanScope?.Subject, subject)
            || lease.SelectedTenantId != tenant
            || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
            || lease.HumanScope?.Purpose != (tenant.HasValue
                ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance)
            || context.IsConfigurationInstanceImportOwner || context.IsTenantFilterBypassed
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Footer writes require the original ordinary Serializable owner.");
        return subject;
    }
}
