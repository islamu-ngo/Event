using System.Collections.Immutable;
using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>Owns the first provider/participation sources and only the canonical fixed effects.</summary>
public sealed class ProviderConfigurationSettingsWriter(ExploreDbContext context, IUnitOfWork unit,
    IServiceProvider services, IProviderConfigurationMutationAuthority authority,
    IVisitorAccessSettingsWriter visitors, IHumanCommitScopePreparation human,
    IHumanSettingsMutationPreparation preparation) : IProviderConfigurationSettingsWriter, IDisposable
{
    private ExploreDbContext Context => context;
    private static readonly AsyncLocal<ProviderHandoff?> Current = new();
    private IProviderConfigurationMutationSource? selected;
    private ProviderConfigurationState? observed;
    private RelationalConnectionIdentity? connection;
    private Action? requireSelection;
    private PostgresNativeQueueEnrollment? enrollment;
    private bool released;
    private DateTime? represented;
    private ProviderConfigurationMutation? applied;
    private Func<CancellationToken, Task>? completion;
    private ProviderHandoff? handoff;
    private Action? requireAttempt;

    private void OnState(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) released = true;
    }

    public void Dispose()
    {
        released = true;
        if (connection is not null) connection.StateChange -= OnState;
    }

    public void RequireIdle()
    {
        if (selected is null || requireSelection is null || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("Provider external work requires the original idle owner.");
        requireSelection();
    }

    public DateTime RepresentTimestamp(DateTime timestamp)
    {
        RequireIdle();
        if (represented.HasValue || timestamp.Kind != DateTimeKind.Utc) throw new UnauthorizedAccessException();
        represented = enrollment is null ? timestamp : new DateTime(timestamp.Ticks - timestamp.Ticks % 10, DateTimeKind.Utc);
        return represented.Value;
    }

    public async Task<ProviderConfigurationState> ObserveAsync(IProviderConfigurationMutationSource source,
        IUnitOfWork owner, CancellationToken token)
    {
        authority.RequireIssued(source);
        if (selected is not null || context.Database.CurrentTransaction is not null
            || !ReferenceEquals(unit, owner) || unit is not EfCoreUnitOfWork actual)
            throw new UnauthorizedAccessException("Provider observation requires its selected idle physical owner.");
        if (!(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.Sqlite) && !(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            throw new NotSupportedException("provider_configuration_native_custody_not_supported");
        actual.RequireTrustedSetupSettingsOwner(context);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        var originalConnection = connection;
        int? backend = null;
        selected = source;
        requireSelection = () =>
        {
            lease.RequireOriginalOwner(context);
            authority.RequireIssued(source);
            bool originalAuthority = source switch
            {
                IProviderConfigurationHumanSource person => ReferenceEquals(human.Subject, person.Subject)
                    && ReferenceEquals(lease.HumanScope?.Subject, person.Subject)
                    && ReferenceEquals(lease.HumanLifetime, person.Lifetime)
                    && lease.HumanScope?.Purpose == HumanAdministratorCommitPurpose.GlobalInstance,
                IProviderConfigurationSetupSource => human.Subject is null && lease.HumanScope is null
                    && lease.HumanLifetime is null && !context.HasHumanCommitContextLease,
                _ => false
            };
            if (!originalAuthority || released || context.ContextId != contextId || context.TenantFilterTenantId is not null
                || lease.SelectedTenantId is not null || (lease.PolicyPurpose ?? PolicyMutationPurpose.Standard) != PolicyMutationPurpose.Standard
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || !originalConnection.Matches(context)
                || backend.HasValue && (originalConnection.State != ConnectionState.Open
                    || originalConnection.BackendProcessId != backend))
                throw new UnauthorizedAccessException("Provider custody lost its original context, backend or authority.");
        };
        requireSelection();
        await source.RequireSourcesAsync(token);
        await context.Database.OpenConnectionAsync(token);
        connection.StateChange += OnState;
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
        {
            backend = connection.PostgresProcessId;
            enrollment = PostgresNativeQueueEnrollment.Bind(services.GetRequiredService<IConfiguration>());
        }
        observed = await ReadAsync(token);
        RequirePendingSetup(observed);
        await source.RequireSourcesAsync(token);
        source.RequireTime();
        return observed;
    }

    public async Task RequireUnchangedAsync(ProviderConfigurationState original, CancellationToken token)
    {
        RequireIdle();
        if (!ReferenceEquals(observed, original) || !original.Same(await ReadAsync(token)))
            throw new UnauthorizedAccessException("The first provider validation sources changed.");
        await selected!.RequireSourcesAsync(token);
        selected.RequireTime();
    }

    public void PrepareSetupAttempt(ProviderConfigurationMutation mutation)
    {
        if (selected is not IProviderConfigurationSetupSource || !ReferenceEquals(selected, mutation.Source))
            throw new UnauthorizedAccessException("The original setup source is required.");
        requireSelection!();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Setup completion belongs to its original attempt.");
        context.RegisterTrustedSetupSettingsAdmission(token => RequireAppliedAsync(mutation, token), () =>
        {
            requireSelection();
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction))
                throw new UnauthorizedAccessException("The provider setup transaction changed.");
            mutation.Source.RequireTime();
        });
    }

    public async Task<ImmutableArray<SettingChangedNotification>> ExecuteAsync(ProviderConfigurationMutation mutation, CancellationToken token)
    {
        mutation.RequireActive();
        if (!ReferenceEquals(selected, mutation.Source) || !ReferenceEquals(observed, mutation.Before)
            || !ReferenceEquals(unit, mutation.OriginalOwner) || represented != mutation.OccurredAt
            || mutation.Effects.IsDefault || mutation.Effects.Any(row => row.UpdatedAt != represented))
            throw new UnauthorizedAccessException("Provider writing requires its exact first observation and frozen effects.");
        mutation.ClaimOnce();
        var transaction = context.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new UnauthorizedAccessException("Provider writing requires its original attempt.");
        requireAttempt = () =>
        {
            requireSelection!();
            mutation.RequireActive();
            if (!ReferenceEquals(context.Database.CurrentTransaction?.GetDbTransaction(), transaction)
                || !connection!.Owns(transaction) || connection.State != ConnectionState.Open
                || transaction.IsolationLevel != IsolationLevel.Serializable
                || !mutation.IsSetup && !context.HumanCommitAttemptOwnsTransaction)
                throw new UnauthorizedAccessException("The original provider Serializable attempt changed.");
        };
        requireAttempt();
        var validation = await mutation.ValidateAsync(token);
        if (!validation.IsValid) throw new FluentValidation.ValidationException(validation.Errors);
        await selected!.RequireSourcesAsync(token);
        if (enrollment is not null)
            await PostgresProviderConfigurationContract.AcquireAsync(context, enrollment, mutation.Actor, observed!, token);
        if (!observed!.Same(await ReadAsync(token)))
            throw new UnauthorizedAccessException("The original provider or participation source changed.");
        RequirePendingSetup(observed);
        if (context.ChangeTracker.Entries().Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new UnauthorizedAccessException("Provider custody cannot flush unrelated tracked state.");
        if (!mutation.IsSetup)
            foreach (var effect in mutation.Effects)
            {
                await preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null, effect.Key,
                    HumanSettingsMutationKind.SetValue, effect.Value, cancellationToken: token);
                await preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null, effect.Key,
                    HumanSettingsMutationKind.SetLock, isLocked: effect.IsLocked, cancellationToken: token);
            }
        if (!mutation.IsAuthentication)
        {
            await WriteFixedAsync(mutation, token);
            return [];
        }
        var effects = mutation.Effects.Select(row => new VisitorAccessSettingMutation(null, row.Key,
            VisitorAccessSettingMutationKind.SetValue, row.Value, row.IsLocked)).ToImmutableArray();
        var previous = Current.Value;
        handoff = new ProviderHandoff(this, mutation, effects);
        Current.Value = handoff;
        try
        {
            var result = selected is IProviderConfigurationSetupSource
                ? await visitors.ApplyInternalAsync(effects, null, token)
                : await visitors.ApplyAsync(effects, ((IProviderConfigurationHumanSource)selected).Subject,
                    SettingsMutationOperation.InstanceSettings, mutation.Actor, token);
            result.EnsureAccepted();
            if (!ReferenceEquals(applied, mutation))
                throw new UnauthorizedAccessException("The actual visitor validator did not complete the provider handoff.");
            return result.DeferredNotifications;
        }
        finally { handoff = null; Current.Value = previous; }
    }

    private async Task WriteFixedAsync(ProviderConfigurationMutation mutation, CancellationToken token)
    {
        requireAttempt!();
        await selected!.RequireSourcesAsync(token);
        if (!observed!.Same(await ReadAsync(token)))
            throw new UnauthorizedAccessException("The original source changed between validation and fixed writing.");
        if (enrollment is not null)
            await PostgresProviderConfigurationContract.WriteAsync(context, enrollment, mutation, token);
        else
        {
            foreach (var effect in mutation.Effects)
            {
                var existing = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == effect.Key, token);
                if (existing is null) context.SystemSettings.Add(effect.ToEntity());
                else context.Entry(existing).CurrentValues.SetValues(effect.ToEntity());
            }
            await context.SaveChangesAsync(token);
        }
        var expected = observed with
        {
            System = observed.System.SetItems(mutation.Effects.Select(row => KeyValuePair.Create<string, ProviderConfigurationRow?>(row.Key, row))),
            Revision = enrollment is null ? observed.Revision : checked(observed.Revision + mutation.Effects.Length)
        };
        async Task RequireFinal(CancellationToken cancellationToken)
        {
            requireAttempt();
            await selected.RequireSourcesAsync(cancellationToken);
            if (!expected.Same(await ReadAsync(cancellationToken)))
                throw new UnauthorizedAccessException("Provider identity, metadata, audit, participation or absence changed.");
            requireAttempt();
        }
        await RequireFinal(token);
        selected.RequireTime();
        applied = mutation;
        completion = RequireFinal;
    }

    public Task RequireAppliedAsync(ProviderConfigurationMutation mutation, CancellationToken token)
    {
        if (!ReferenceEquals(applied, mutation) || completion is null)
            throw new UnauthorizedAccessException("The private selected provider writer did not complete.");
        return completion(token);
    }

    private void RequirePendingSetup(ProviderConfigurationState state)
    {
        if (selected is not IProviderConfigurationSetupSource) return;
        var current = JsonSerializer.Deserialize<JsonElement>(state.Bootstrap).EnumerateArray()
            .OrderByDescending(row => row.GetProperty("Generation").GetInt64())
            .ThenByDescending(row => row.GetProperty("CreatedAt").GetDateTime())
            .ThenByDescending(row => row.GetProperty("Id").GetGuid()).FirstOrDefault();
        if (current.ValueKind != JsonValueKind.Undefined
            && current.GetProperty("Status").GetInt32() != (int)InstanceBootstrapStatus.Pending)
            throw new UnauthorizedAccessException("Provider setup requires its original pending generation or original absence.");
    }

    private async Task<ProviderConfigurationState> ReadAsync(CancellationToken token)
    {
        requireSelection!();
        Guid? actor = selected is IProviderConfigurationHumanSource person ? person.Subject.PrincipalId : null;
        if (enrollment is not null)
        {
            var native = await PostgresProviderConfigurationContract.ReadAsync(context, enrollment, actor, token);
            requireSelection();
            return native;
        }
        var keys = ProviderConfigurationState.Keys.ToArray();
        var secretKeys = ProviderConfigurationState.SecretKeys.ToArray();
        var systems = await context.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey)).ToListAsync(token);
        async Task<string> Rows<T>(IQueryable<T> query, params string[] properties) where T : class
        {
            var model = context.Model.FindEntityType(typeof(T))!;
            var columns = properties.Length == 0 ? model.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray() : properties;
            var rows = await query.AsNoTracking().ToListAsync(token);
            return JsonSerializer.Serialize(rows.OrderBy(row => (Guid)model.FindProperty("Id")!.GetGetter().GetClrValue(row)!)
                .Select(row => columns.ToDictionary(name => name, name => model.FindProperty(name)!.GetGetter().GetClrValue(row), StringComparer.Ordinal)));
        }
        var state = new ProviderConfigurationState(keys.ToImmutableDictionary(key => key, key =>
        {
            var row = systems.SingleOrDefault(item => item.SettingKey == key);
            return row is null ? null : ProviderConfigurationRow.From(row);
        }, StringComparer.Ordinal),
            await Rows(context.Set<SecretBinding>().Where(row => secretKeys.Contains(row.SettingKey)
                && row.SettingScopeId == 1 && row.ScopeId == null && row.Qualifier == "")),
            await Rows(context.TenantSettingOverrides.IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead)
                .Where(row => keys.Contains(row.SettingKey))),
            await Rows(context.Set<EventParticipationConfiguration>()
                .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead), "Id", "TenantId", "IsDeleted", "IdentityAccessModeId"),
            await Rows(context.Set<Explore.Domain.Event>()
                .IgnoreTenantFilter(TenantFilterBypassReasons.VisitorPolicyAuthorityInheritanceSafetyRead), "Id", "TenantId", "IsDeleted"),
            await Rows(context.InstanceBootstrapStates),
            await Rows(context.Users.Where(row => row.Id == actor), "Id", "IsDeleted"),
            await Rows(context.UserExternalLogins.Where(row => row.UserId == actor)), 0);
        state.RequireShape();
        requireSelection();
        return state;
    }

    internal static ProviderHandoff? GetHandoff(ExploreDbContext context, ImmutableArray<VisitorAccessSettingMutation> effects,
        HumanCommitSubject? subject, Guid? actor)
    {
        var value = Current.Value;
        if (value is null) return null;
        value.RequireCurrent();
        if (!ReferenceEquals(value.Owner.Context, context) || !value.Effects.SequenceEqual(effects)
            || actor != value.Mutation.Actor
            || value.Mutation.Source is IProviderConfigurationHumanSource person && !ReferenceEquals(person.Subject, subject)
            || value.Mutation.Source is IProviderConfigurationSetupSource && subject is not null)
            throw new UnauthorizedAccessException("The provider visitor handoff cannot change source, effects or owner.");
        return value;
    }

    public static async Task RequireSetupVisitorAsync(ExploreDbContext context, ImmutableArray<VisitorAccessSettingMutation> effects,
        Guid? actor, CancellationToken token)
    {
        var value = GetHandoff(context, effects, null, actor)
            ?? throw new UnauthorizedAccessException("Provider setup requires its original live visitor handoff.");
        if (value.Mutation.Source is not IProviderConfigurationSetupSource) throw new UnauthorizedAccessException();
        await value.Mutation.Source.RequireSourcesAsync(token);
        value.RequireCurrent();
    }

    internal sealed class ProviderHandoff(ProviderConfigurationSettingsWriter owner, ProviderConfigurationMutation mutation,
        ImmutableArray<VisitorAccessSettingMutation> effects)
    {
        internal ProviderConfigurationSettingsWriter Owner => owner;
        internal ProviderConfigurationMutation Mutation => mutation;
        internal ImmutableArray<VisitorAccessSettingMutation> Effects => effects;
        internal void RequireCurrent()
        {
            if (!ReferenceEquals(owner.handoff, this) || !ReferenceEquals(Current.Value, this))
                throw new UnauthorizedAccessException("The provider handoff has ended.");
            owner.requireAttempt!();
        }
        internal Dictionary<string, SystemSetting> Systems => mutation.Before.System.Where(pair => pair.Value is not null
            && VisitorAccessCapabilityResolver.AuthoritySettingKeys.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value!.ToEntity(), StringComparer.Ordinal);
        internal List<TenantSetting> Tenants => JsonSerializer.Deserialize<JsonElement>(mutation.Before.Tenants).EnumerateArray().Select(row => new TenantSetting
        {
            Id = row.GetProperty("Id").GetGuid(),
            TenantId = row.GetProperty("TenantId").GetGuid(),
            Tenant = null!,
            SettingKey = row.GetProperty("SettingKey").GetString()!,
            Value = row.GetProperty("Value").GetString()!,
            IsLocked = row.GetProperty("IsLocked").GetBoolean(),
            CreatedAt = row.GetProperty("CreatedAt").GetDateTime(),
            CreatedBy = row.GetProperty("CreatedBy").ValueKind == JsonValueKind.Null ? null : row.GetProperty("CreatedBy").GetGuid(),
            UpdatedAt = row.GetProperty("UpdatedAt").ValueKind == JsonValueKind.Null ? null : row.GetProperty("UpdatedAt").GetDateTime(),
            UpdatedBy = row.GetProperty("UpdatedBy").ValueKind == JsonValueKind.Null ? null : row.GetProperty("UpdatedBy").GetGuid()
        }).ToList();
        internal List<Guid> Affected => (
            from participation in JsonSerializer.Deserialize<JsonElement>(mutation.Before.Participation).EnumerateArray()
            from source in JsonSerializer.Deserialize<JsonElement>(mutation.Before.Events).EnumerateArray()
            where participation.GetProperty("Id").GetGuid() == source.GetProperty("Id").GetGuid()
                && participation.GetProperty("TenantId").GetGuid() == source.GetProperty("TenantId").GetGuid()
                && participation.GetProperty("IdentityAccessModeId").ValueKind == JsonValueKind.Number
                && participation.GetProperty("IdentityAccessModeId").GetInt32() == (int)IdentityAccessModeEnum.AccountRequired
                && !participation.GetProperty("IsDeleted").GetBoolean() && !source.GetProperty("IsDeleted").GetBoolean()
            select participation.GetProperty("TenantId").GetGuid()).Distinct().ToList();
        internal SystemSetting Canonical(string key) => mutation.Effects.Single(row => row.Key == key).ToEntity();
        internal async Task<VisitorAccessSettingsWriteResult> ApplyValidatedAsync(VisitorAccessSettingsWriter validator,
            IReadOnlyDictionary<string, SystemSetting> proposed, ImmutableArray<SettingChangedNotification> notifications, CancellationToken token)
        {
            RequireCurrent();
            validator.RequireProviderValidated(this);
            if (mutation.Effects.Any(effect => !proposed.TryGetValue(effect.Key, out var row) || ProviderConfigurationRow.From(row) != effect))
                throw new UnauthorizedAccessException("Visitor validation must retain the canonical provider rows.");
            await owner.WriteFixedAsync(mutation, token);
            RequireCurrent();
            return new(true, null, notifications);
        }
    }
}
