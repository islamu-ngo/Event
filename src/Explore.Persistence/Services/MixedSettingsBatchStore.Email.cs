using System.Collections.Immutable;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

public sealed partial class MixedSettingsBatchStore
{
    private EmailDeliverySettingsWriter? emailWriter;

    internal IDisposable EnterEmail(EmailDeliverySettingsWriter writer)
    {
        Require(Mutation);
        if (emailWriter is not null || !ReferenceEquals(writer, services.GetRequiredService<EmailDeliverySettingsWriter>())
            || !Mutation.Accepted.Keys.Any(EmailDeliverySettingKeys.Contains)
            || Mutation.Accepted.Keys.Where(EmailDeliverySettingKeys.Contains).Any(completions.ContainsKey))
            throw new UnauthorizedAccessException("The original selected Email writer must own the complete unconsumed slice.");
        emailWriter = writer;
        return new EmailSlice(this, writer);
    }

    internal Dictionary<string, SystemSetting> EmailSystemSettings()
    {
        RequireEmail();
        return Rows<SystemSetting>().Where(row => EmailDeliverySettingKeys.Contains(row.SettingKey))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
    }

    internal Dictionary<Guid, Dictionary<string, TenantSetting>> EmailTenantSettings()
    {
        RequireEmail();
        var rows = Rows<TenantSetting>().Where(row => EmailDeliverySettingKeys.Contains(row.SettingKey)).ToArray();
        return MixedSettingsDataImage.TenantIds(expected!.ToJsonString()).ToDictionary(tenant => tenant, tenant => rows.Where(row => row.TenantId == tenant)
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal));
    }

    internal EmailDeliveryPolicySet EmailPolicies() =>
        EmailDeliveryPolicyReader.EvaluateAll(EmailSystemSettings(), EmailTenantSettings());

    internal Task WriteEmailRowsAsync(
        ImmutableArray<(object Entity, EntityState State)> writes, CancellationToken token)
    {
        RequireEmail();
        if (writes.Any(write => write.Entity is not (SystemSetting or TenantSetting or EmailDispatchProcessorState or EmailDispatchTenantControl)))
            throw new UnauthorizedAccessException("Email cannot emit another mixed family's rows.");
        return EmitAsync("email", Mutation.Accepted.Keys.Where(EmailDeliverySettingKeys.Contains).ToImmutableArray(),
            writes, false, token);
    }

    internal async Task WriteEmailControlsAsync(
        IReadOnlyList<PostgresEmailDeliveryPolicyContract.ControlChange> changes, DateTime now, Guid? actor, CancellationToken token)
    {
        RequireEmail();
        if (actor != Mutation.Actor || now != OccurredAt)
            throw new UnauthorizedAccessException("Email control effects must retain their original actor and compiled data time.");
        var existing = Rows<EmailDispatchTenantControl>().ToDictionary(row => row.TenantId);
        var writes = ImmutableArray.CreateBuilder<(object, EntityState)>();
        foreach (var change in changes)
        {
            var present = existing.GetValueOrDefault(change.TenantId);
            if ((present?.DeliveryPolicyRevision ?? 0) != change.ExpectedRevision)
                throw new UnauthorizedAccessException("The original Email control revision changed.");
            var row = present ?? new EmailDispatchTenantControl
            {
                Id = Guid.CreateVersion7(),
                TenantId = change.TenantId,
                CreatedAt = now,
                CreatedBy = actor
            };
            row.DeliveryPolicyRevision = change.Revision;
            row.OptionalSuppressedThroughRevision = change.SuppressedRevision;
            row.OptionalSuppressedThroughUtc = change.SuppressedUtc;
            row.UpdatedAt = now;
            row.UpdatedBy = actor;
            writes.Add((row, present is null ? EntityState.Added : EntityState.Modified));
        }
        await WriteEmailRowsAsync(writes.ToImmutable(), token);
    }

    internal async Task CompleteEmailAsync(EmailDeliverySettingsWriter writer, CancellationToken token)
    {
        RequireEmail();
        if (!ReferenceEquals(emailWriter, writer))
            throw new UnauthorizedAccessException("Another writer cannot complete the original Email slice.");
        var effects = Mutation.Accepted.Where(pair => EmailDeliverySettingKeys.Contains(pair.Key)).ToArray();
        foreach (var pair in effects)
        {
            string? stored = Mutation.TenantId is { } tenant
                ? Rows<TenantSetting>().SingleOrDefault(row => row.TenantId == tenant && row.SettingKey == pair.Key)?.Value
                : Rows<SystemSetting>().SingleOrDefault(row => row.SettingKey == pair.Key)?.Value;
            if (stored != pair.Value)
                throw new UnauthorizedAccessException("The real Email writer omitted an accepted setting effect.");
        }
        await EmitAsync("email", effects.Select(pair => pair.Key).ToImmutableArray(), [], true, token);
        foreach (var pair in effects)
            completions.Add(pair.Key, new(writer, Mutation, pair.Key, pair.Value));
    }

    private void RequireEmail()
    {
        Require(Mutation);
        if (emailWriter is null)
            throw new UnauthorizedAccessException("Only the actual joined Email writer can emit or complete Email effects.");
    }

    private sealed class EmailSlice(MixedSettingsBatchStore store, EmailDeliverySettingsWriter writer) : IDisposable
    {
        public void Dispose()
        {
            if (!ReferenceEquals(store.emailWriter, writer))
                throw new UnauthorizedAccessException("Email writer custody changed.");
            store.emailWriter = null;
        }
    }
}
