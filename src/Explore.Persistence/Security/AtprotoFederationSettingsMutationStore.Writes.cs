using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Security;

public sealed partial class AtprotoFederationSettingsMutationStore
{
    internal async Task<AtprotoFederationSettingWriteResult> ApplyCurrentAsync(
        IAtprotoFederationSettingsSource source, HumanSettingsMutation effect, CancellationToken token)
    {
        var issued = RequireIssued(source);
        try
        {
            var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
            if (!ReferenceEquals(preparation.CurrentAtprotoFederationSource, source))
                throw new UnauthorizedAccessException("Federation writes require the original issuer's current callback.");
            await preparation.RequireCurrentOwnedAsync(effect.Scope, effect.TenantId, effect.UserId,
                effect.Key, effect.Kind, effect.Value, effect.IsLocked, token);
            issued.RequireAcquired();
            issued.RequireEffectShape(effect);
            if (issued.Writing || issued.Effects is not { } effects || !effects.Contains(effect)
                || issued.Completed.ContainsKey(effect.Key))
                throw new UnauthorizedAccessException("A federation effect must be original, compiled and privately applied once.");
            if (context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                throw new UnauthorizedAccessException("A federation writer cannot flush caller-owned tracked changes.");
            await VerifyAsync(source, token);
            issued.Writing = true;
            var before = issued.Expected!.Target(effect);

            string? refusal = effect.Scope == SettingScope.Tenant
                && issued.Expected.Instance.GetValueOrDefault(effect.Key)?.IsLocked == true
                    ? SettingSystemLockedException.Code
                : effect.Kind is HumanSettingsMutationKind.Remove or HumanSettingsMutationKind.SetLock && before is null
                    ? effect.Scope == SettingScope.Tenant ? "setting_override_not_found" : "setting_not_found"
                : effect.Kind == HumanSettingsMutationKind.SetLock && effect.Scope == SettingScope.Tenant
                    && before!.IsLocked == effect.IsLocked ? "setting_state_conflict" : null;
            if (refusal is not null)
            {
                await CompleteWriteAsync(issued, effect, before, before, applied: false, token);
                return Outcome(before, before, applied: false, refusal);
            }

            AtprotoFederationSettingImage? after;
            if (effect.Kind == HumanSettingsMutationKind.Remove)
            {
                if (before is null || await TenantTarget(before).ExecuteDeleteAsync(token) != 1)
                    throw new UnauthorizedAccessException("The original federation override could not be removed exactly once.");
                after = null;
            }
            else if (before is null)
                after = await InsertAsync(issued, effect, token);
            else
                after = await UpdateAsync(issued, effect, before, token);

            await CompleteWriteAsync(issued, effect, before, after, applied: true, token);
            return Outcome(before, after, applied: true, null);
        }
        catch { issued.State = 2; throw; }
    }

    private async Task<AtprotoFederationSettingImage> InsertAsync(Source source, HumanSettingsMutation effect, CancellationToken token)
    {
        if (effect.Kind != HumanSettingsMutationKind.SetValue)
            throw new UnauthorizedAccessException("Only an original value effect may create a missing federation target.");
        string value = effect.Value!;
        const bool isLocked = false;
        AtprotoFederationSettingImage after;
        if (effect.Scope == SettingScope.Instance)
        {
            var row = AtprotoFederationInstanceSetting.Create(effect.Key, value, isLocked, source.OccurredAt, source.Subject.PrincipalId);
            after = NewImage(row.Id);
            if (AtprotoFederationSettingImage.Capture(row) != after)
                throw new UnauthorizedAccessException("The new instance row differs from its canonical original effect.");
            context.Add(row);
            try { await context.SaveChangesAsync(token); }
            finally { context.Entry(row).State = EntityState.Detached; }
        }
        else
        {
            var row = AtprotoFederationTenantSetting.Create(source.TenantId!.Value,
                effect.Key, value, isLocked, source.OccurredAt, source.Subject.PrincipalId);
            after = NewImage(row.Id);
            if (AtprotoFederationSettingImage.Capture(row) != after)
                throw new UnauthorizedAccessException("The new tenant row differs from its canonical original effect.");
            context.Add(row);
            try { await context.SaveChangesAsync(token); }
            finally { context.Entry(row).State = EntityState.Detached; }
        }
        return after;

        AtprotoFederationSettingImage NewImage(Guid id) => new(id, source.TenantId, effect.Key, value, isLocked,
            source.OccurredAt, source.Subject.PrincipalId, null, null);
    }

    private async Task<AtprotoFederationSettingImage> UpdateAsync(Source source, HumanSettingsMutation effect,
        AtprotoFederationSettingImage before, CancellationToken token)
    {
        var after = effect.Kind == HumanSettingsMutationKind.SetValue
            ? before.WithValue(effect.Value!, source.OccurredAt, source.Subject.PrincipalId)
            : before.WithLock(effect.IsLocked!.Value, source.OccurredAt, source.Subject.PrincipalId);
        int affected;
        if (effect.Scope == SettingScope.Instance)
        {
            var row = await InstanceTarget(before).AsNoTracking().SingleOrDefaultAsync(token)
                ?? throw new UnauthorizedAccessException("The original federation instance target changed.");
            if (effect.Kind == HumanSettingsMutationKind.SetValue) row.SetValue(effect.Value!, source.OccurredAt, source.Subject.PrincipalId);
            else row.SetLock(effect.IsLocked!.Value, source.OccurredAt, source.Subject.PrincipalId);
            if (AtprotoFederationSettingImage.Capture(row) != after)
                throw new UnauthorizedAccessException("The instance transition differs from its canonical postimage.");
            affected = await InstanceTarget(before).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Value, after.Value).SetProperty(row => row.IsLocked, after.IsLocked)
                .SetProperty(row => row.UpdatedAt, after.UpdatedAt).SetProperty(row => row.UpdatedBy, after.UpdatedBy), token);
        }
        else
        {
            var row = await TenantTarget(before).AsNoTracking().SingleOrDefaultAsync(token)
                ?? throw new UnauthorizedAccessException("The original federation tenant target changed.");
            if (effect.Kind == HumanSettingsMutationKind.SetValue) row.SetValue(effect.Value!, source.OccurredAt, source.Subject.PrincipalId);
            else row.SetLock(effect.IsLocked!.Value, source.OccurredAt, source.Subject.PrincipalId);
            if (AtprotoFederationSettingImage.Capture(row) != after)
                throw new UnauthorizedAccessException("The tenant transition differs from its canonical postimage.");
            affected = await TenantTarget(before).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Value, after.Value).SetProperty(row => row.IsLocked, after.IsLocked)
                .SetProperty(row => row.UpdatedAt, after.UpdatedAt).SetProperty(row => row.UpdatedBy, after.UpdatedBy), token);
        }
        if (affected != 1)
            throw new UnauthorizedAccessException("The original federation target did not persist exactly one canonical transition.");
        return after;
    }

    private async Task CompleteWriteAsync(Source source, HumanSettingsMutation effect,
        AtprotoFederationSettingImage? before, AtprotoFederationSettingImage? after, bool applied, CancellationToken token)
    {
        source.RequireAcquired();
        var expected = source.Expected!.Apply(effect, after);
        if (!expected.Same(await ReadAsync(source, token)))
            throw new UnauthorizedAccessException("The federation writer did not persist its exact canonical postimage.");
        source.RequireAcquired();
        source.Expected = expected;
        source.Completed.Add(effect.Key, new(effect, before, after, applied));
        source.Writing = false;
    }

    private IQueryable<AtprotoFederationInstanceSetting> InstanceTarget(AtprotoFederationSettingImage before) =>
        context.Set<AtprotoFederationInstanceSetting>().Where(row => row.Id == before.Id && row.SettingKey == before.Key
            && row.Value == before.Value && row.IsLocked == before.IsLocked
            && row.CreatedAt == before.CreatedAt && row.CreatedBy == before.CreatedBy
            && row.UpdatedAt == before.UpdatedAt && row.UpdatedBy == before.UpdatedBy);

    private IQueryable<AtprotoFederationTenantSetting> TenantTarget(AtprotoFederationSettingImage before) =>
        context.Set<AtprotoFederationTenantSetting>().Where(row => row.Id == before.Id && row.TenantId == before.TenantId
            && row.SettingKey == before.Key && row.Value == before.Value && row.IsLocked == before.IsLocked
            && row.CreatedAt == before.CreatedAt && row.CreatedBy == before.CreatedBy
            && row.UpdatedAt == before.UpdatedAt && row.UpdatedBy == before.UpdatedBy);

    private static AtprotoFederationSettingWriteResult Outcome(AtprotoFederationSettingImage? before,
        AtprotoFederationSettingImage? after, bool applied, string? failureCode) =>
        new(applied, after?.Id ?? before?.Id, failureCode, before?.Value, after?.Value, before?.IsLocked, after?.IsLocked);
}
