using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

public sealed partial class MixedSettingsBatchStore
{
    private EventResourceSettingsWriter? resourceWriter;

    internal async Task<EventResourceSettingsWriteResult> ExecuteResourceAsync(
        SelectedEventResourceSettingsWriter selected, EventResourceSettingsWriter writer,
        ImmutableArray<EventResourceSettingMutation> effects, HumanCommitSubject subject,
        SettingsMutationOperation operation, Guid? actor, CancellationToken token)
    {
        Require(Mutation);
        if (resourceWriter is not null || !ReferenceEquals(selected, services.GetRequiredService<IEventResourceSettingsWriter>()))
            throw new UnauthorizedAccessException("The actual scoped Resource selector must borrow the mixed owner.");
        await RequireSpecializedAsync(subject, operation, actor, effects.Select(effect => effect.Key), token);
        resourceWriter = writer;
        try { return await writer.ApplyAsync(effects, subject, operation, actor, token); }
        finally { resourceWriter = null; }
    }

    internal List<Guid> AccountRequiredTenants()
    {
        Require(Mutation);
        var events = expected![nameof(Explore.Domain.Event)]!.AsArray()
            .Where(row => !row![nameof(Explore.Domain.Event.IsDeleted)]!.GetValue<bool>())
            .Select(row => (row![nameof(Explore.Domain.Event.TenantId)]!.GetValue<string>(),
                row[nameof(Explore.Domain.Event.Id)]!.GetValue<string>())).ToHashSet();
        return expected[nameof(EventParticipationConfiguration)]!.AsArray()
            .Where(row => !row![nameof(EventParticipationConfiguration.IsDeleted)]!.GetValue<bool>()
                && row[nameof(EventParticipationConfiguration.IdentityAccessModeId)]?.GetValue<int>() == (int)IdentityAccessModeEnum.AccountRequired
                && events.Contains((row[nameof(EventParticipationConfiguration.TenantId)]!.GetValue<string>(),
                    row[nameof(EventParticipationConfiguration.Id)]!.GetValue<string>())))
            .Select(row => Guid.Parse(row![nameof(EventParticipationConfiguration.TenantId)]!.GetValue<string>()))
            .Distinct().ToList();
    }

    internal async Task WriteVisitorAsync(VisitorAccessSettingsWriter writer,
        ImmutableArray<VisitorAccessSettingMutation> effects,
        ImmutableArray<(object Entity, EntityState State)> writes, CancellationToken token)
    {
        Require(Mutation);
        if (!ReferenceEquals(writer, services.GetRequiredService<IVisitorAccessSettingsWriter>())
            || effects.Any(effect => effect.Kind != VisitorAccessSettingMutationKind.SetValue
                || !VisitorAccessSettingMutationGuard.Handles(effect.Key) || effect.TenantId != Mutation.TenantId
                || !Mutation.Accepted.TryGetValue(effect.Key, out string? value) || effect.Value != value))
            throw new UnauthorizedAccessException("Only the actual compiled visitor writer can complete its mixed slice.");
        var keys = effects.Select(effect => effect.Key).ToImmutableArray();
        await EmitAsync("settings", keys, writes, true, token);
        foreach (string key in keys)
            completions.Add(key, new(writer, Mutation, key, Mutation.Accepted[key]));
    }

    internal async Task WritePublicationAsync(CoordinatedSettingMutationRepository writer, Guid? tenant,
        ImmutableArray<PublicationPolicySettingMutation> effects, Guid? actor, DateTime occurred, CancellationToken token)
    {
        Require(Mutation);
        await PublicationPolicyMutationBoundary.RequireCurrentWriteAsync(tenant, effects, actor, occurred, token);
        if (!ReferenceEquals(writer, services.GetRequiredService<ICoordinatedSettingMutationStore>())
            || tenant != Mutation.TenantId || actor != Mutation.Actor || occurred != OccurredAt
            || effects.Any(effect => effect.Kind != PublicationPolicyMutationKind.Set
                || !PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal)
                || !Mutation.Accepted.TryGetValue(effect.Key, out string? value) || effect.JsonValue != value))
            throw new UnauthorizedAccessException("Only the original compiled publication writer can complete its mixed slice.");
        var keys = effects.Select(effect => effect.Key).ToImmutableArray();
        var entries = context.ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();
        if (entries.Any(entry => entry.Entity switch
            {
                SystemSetting setting => tenant.HasValue || !keys.Contains(setting.SettingKey),
                TenantSetting setting => setting.TenantId != tenant || !keys.Contains(setting.SettingKey),
                _ => true
            }))
            throw new UnauthorizedAccessException("A foreign tracked row cannot join publication completion.");
        await EmitAsync("settings", keys, entries.Select(entry => (entry.Entity, entry.State)).ToImmutableArray(), true, token);
        foreach (string key in keys)
            completions.Add(key, new(writer, Mutation, key, Mutation.Accepted[key]));
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is SystemSetting or TenantSetting).ToArray())
            entry.State = EntityState.Detached;
    }

    internal async Task RequireSpecializedAsync(HumanCommitSubject? subject, SettingsMutationOperation? operation,
        Guid? actor, IEnumerable<string> keys, CancellationToken token)
    {
        Require(Mutation, allowCompleted: true);
        if (!ReferenceEquals(subject, source!.Subject) || actor != Mutation.Actor
            || operation != (Mutation.TenantId.HasValue
                ? SettingsMutationOperation.TenantSettings : SettingsMutationOperation.InstanceSettings)
            || keys.Any(key => EmailDeliverySettingKeys.Contains(key) || !Mutation.Accepted.ContainsKey(key)))
            throw new UnauthorizedAccessException("A specialized slice must retain its original mixed action and actor.");
        await source.RequireSourcesAsync(token);
    }

    internal async Task WriteResourceAsync(EventResourceSettingsWriter writer,
        ImmutableArray<EventResourceSettingMutation> effects,
        ImmutableArray<(object Entity, EntityState State)> writes, CancellationToken token)
    {
        Require(Mutation);
        if (!ReferenceEquals(writer, resourceWriter)
            || effects.Any(effect => effect.Kind != EventResourceSettingMutationKind.SetValue
                || !EventResourceSettingMutationGuard.Handles(effect.Key) || effect.TenantId != Mutation.TenantId
                || !Mutation.Accepted.TryGetValue(effect.Key, out string? value) || effect.Value != value))
            throw new UnauthorizedAccessException("Only the actual compiled Resource writer can complete its mixed slice.");
        var keys = effects.Select(effect => effect.Key).ToImmutableArray();
        await EmitAsync("settings", keys, writes, true, token);
        foreach (string key in keys)
            completions.Add(key, new(writer, Mutation, key, Mutation.Accepted[key]));
    }
}
