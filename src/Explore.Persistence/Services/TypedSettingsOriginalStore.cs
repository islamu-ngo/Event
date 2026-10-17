using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings;
using Explore.Application.Notifications;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Closed compiler and writer for actual typed originals. Expected rows are constructed here,
/// never read back as authority. The complete data image stays on the original physical owner.
/// </summary>
public sealed partial class TypedSettingsOriginalStore(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock) : ITypedSettingsOriginalStore, ITypedTenantPlanOriginalStore
{
    private TypedSettingsOriginal? original;
    private IHumanMixedSettingsSource? source;
    private DbContextId contextId;
    private RelationalConnectionIdentity? connection;
    private DbTransaction? transaction;
    private string? before;
    private JsonObject? expected;
    private ImmutableArray<TypedSettingsEffect> effects;
    private readonly HashSet<string> written = new(StringComparer.Ordinal);
    private TypedSettingsPublication? publication;
    private PostgresNativeQueueEnrollment? enrollment;
    private DateTime occurred;
    private bool invoked;
    private bool rejected;
    private bool completed;
    private bool closed;

    public Task ObserveAsync(TypedSettingsOriginal request, IHumanMixedSettingsSource originalSource,
        CancellationToken cancellationToken)
    {
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(context);
        services.GetRequiredService<IHumanSettingsMutationPreparation>().RequireTypedSource(originalSource);
        if (original is not null || !ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || lease.PolicyPurpose != PolicyMutationPurpose.MixedSettings
            || request.TenantId != lease.SelectedTenantId || request.Actor != originalSource.Subject.PrincipalId
            || !ReferenceEquals(lease.HumanScope?.Subject, originalSource.Subject))
            throw new UnauthorizedAccessException("Typed Settings observation requires the original idle selection.");
        original = request;
        source = originalSource;
        connection = RelationalConnectionIdentity.Capture(context);
        contextId = context.ContextId;
        enrollment = lease.HumanScope!.Enrollment;
        before = originalSource.OriginalData;
        expected = JsonNode.Parse(before)!.AsObject();
        if (request.PlanAssignment is not null)
            ObservePlan();
        occurred = clock.GetUtcNow().UtcDateTime;
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            occurred = new DateTime(occurred.Ticks - occurred.Ticks % 10, DateTimeKind.Utc);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task<ImmutableArray<TypedSettingsEffect>> AcquireAndCompileAsync(
        TypedSettingsOriginal request, CancellationToken cancellationToken)
    {
        RequireSelection(request);
        if (transaction is not null || !effects.IsDefault)
            throw new UnauthorizedAccessException("Typed compilation belongs to one acquired physical attempt.");
        transaction = RequireTransaction();
        if (!MixedSettingsDataImage.Same(before!, await ReadDataAsync(cancellationToken)))
            throw new UnauthorizedAccessException("Original typed Settings data changed before compilation.");
        var system = Rows<SystemSetting>().ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        if (request.PlanAssignment is not null)
            effects = CompilePlan();
        else if (request.EventPolicy is not null)
            effects = InstanceGovernanceSettingService.CompileEventPolicyOriginal(request, system);
        else if (request.Governance is not null)
        {
            effects = InstanceGovernanceSettingService.CompileGovernanceOriginal(request, system);
        }
        else if (request.Onboarding is { } onboarding)
        {
            string? slug = expected![nameof(Tenant)]!.AsArray().Single(row =>
                row![nameof(Tenant.Id)]!.GetValue<string>() == request.TenantId!.Value.ToString("D"))![nameof(Tenant.Slug)]?.GetValue<string>();
            string? revision = Rows<TenantSetting>().SingleOrDefault(row => row.TenantId == request.TenantId
                && row.SettingKey == GovernanceSettingKeys.PublicExperience.AnnouncementBarRevision)?.Value;
            effects = TenantPolicySettingService.CompileOriginal(onboarding.Settings, system, slug, revision);
        }
        else if (request.Reporting is { } reporting)
            effects = [new(GovernanceSettingKeys.EventReporting.IntakeEnabled,
                SettingValueSerializer.Serialize(reporting.Policy.Enabled))];
        else
            throw new UnauthorizedAccessException("Only a closed typed original can compile Settings.");
        if (effects.IsDefault || effects.IsEmpty && request.PlanAssignment is null
            || effects.Select(effect => effect.Key).Distinct(StringComparer.Ordinal).Count() != effects.Length)
            throw new UnauthorizedAccessException("The original typed compiler must produce exact distinct effects.");
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            occurred = await PostgresMixedSettingsContract.CompileTypedAsync(context, enrollment!, request, effects, cancellationToken);
        if (request.Governance is not null)
        {
            CompileGovernanceProjections();
            CompileGovernanceEmailRevisions();
        }
        await source!.RequireSourcesAsync(cancellationToken);
        source.RequireTime(clock.GetUtcNow());
        if (request.PlanAssignment is not null)
            Plans.Add(context, this);
        return effects;
    }

    public Task<PublicationPolicyMutationResult> ApplyEventPolicyAsync(
        TypedSettingsOriginal request, CancellationToken cancellationToken)
    {
        if (request.EventPolicy is null)
            throw new UnauthorizedAccessException("The actual event-policy original is required.");
        return ApplyAsync(request, cancellationToken);
    }

    public Task<PublicationPolicyMutationResult> ApplyReportingAsync(
        TypedSettingsOriginal request, CancellationToken cancellationToken)
    {
        if (request.Reporting is null)
            throw new UnauthorizedAccessException("The actual reporting original is required.");
        return ApplyAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingChangedNotification>> ApplyOnboardingAsync(
        TypedSettingsOriginal request, CancellationToken cancellationToken)
    {
        if (request.Onboarding is null)
            throw new UnauthorizedAccessException("The actual onboarding original is required.");
        var result = await ApplyAsync(request, cancellationToken);
        if (!result.Success)
            throw new FluentValidation.ValidationException([
                new FluentValidation.Results.ValidationFailure(nameof(request.Onboarding.Settings), string.Empty)
                { ErrorCode = result.FailureCode ?? PublicationPolicyMutationFailureCodes.InvalidPolicy }
            ]);
        return result.DeferredNotifications;
    }

    private async Task<PublicationPolicyMutationResult> ApplyAsync(TypedSettingsOriginal request, CancellationToken token)
    {
        try { return await ApplyCoreAsync(request, token); }
        catch { closed = true; throw; }
    }

    private async Task<PublicationPolicyMutationResult> ApplyCoreAsync(TypedSettingsOriginal request, CancellationToken token)
    {
        Require(request);
        if (invoked)
            throw new UnauthorizedAccessException("The typed original writer cannot be replayed.");
        invoked = true;
        var notifications = ImmutableArray.CreateBuilder<SettingChangedNotification>();
        var publicationEffects = effects.Where(effect => PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal))
            .Select(effect => new PublicationPolicySettingMutation(effect.Key,
                effect.Remove ? PublicationPolicyMutationKind.Remove : PublicationPolicyMutationKind.Set,
                effect.Value, request.TenantId, effect.IsLocked)).ToImmutableArray();
        if (!publicationEffects.IsEmpty)
        {
            publication = new(request, this, new(
                Rows<SystemSetting>().Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal))
                    .Select(row => new PublicationPolicySystemValueSnapshot(row.SettingKey, row.Value, row.IsLocked)).ToImmutableArray(),
                Rows<TenantSetting>().Where(row => PublicationPolicySettingKeys.All.Contains(row.SettingKey, StringComparer.Ordinal))
                    .Select(row => new PublicationPolicyTenantValueSnapshot(row.TenantId, row.SettingKey, row.Value)).ToImmutableArray()),
                publicationEffects, occurred);
            var result = await services.GetRequiredService<IPublicationPolicyMutationBoundary>()
                .ApplyTypedInCurrentTransactionAsync(publication, token);
            if (!result.Success)
            {
                rejected = true;
                return result;
            }
            notifications.AddRange(result.DeferredNotifications);
        }
        foreach (var effect in effects.Where(effect => !PublicationPolicySettingKeys.All.Contains(effect.Key, StringComparer.Ordinal)))
        {
            var change = await WriteAsync(effect, token);
            if (change is not null)
                notifications.Add(new(effect.Key, change.OldValue, change.NewValue,
                    request.TenantId.HasValue ? SettingSource.TenantOverride
                        : effect.IsLocked == true ? SettingSource.SystemLocked : SettingSource.SystemDefault,
                    request.TenantId, request.Actor, occurred));
        }
        return new(true, null, "The publication policy was updated.", notifications.ToImmutable());
    }

    public async Task RequirePublicationAsync(TypedSettingsPublication candidate, CancellationToken token)
    {
        Require(candidate.Original);
        if (!ReferenceEquals(publication, candidate) || !ReferenceEquals(candidate.Store, this))
            throw new UnauthorizedAccessException("Only the original private publication compilation is admitted.");
        await VerifyAsync(candidate.Original, token);
    }

    public async Task<CoordinatedSettingMutationWriteResult> WritePublicationAsync(
        TypedSettingsPublication candidate, CancellationToken token)
    {
        await RequirePublicationAsync(candidate, token);
        await PublicationPolicyMutationBoundary.RequireCurrentWriteAsync(original!.TenantId,
            candidate.Mutations, original.Actor, candidate.OccurredAt, token);
        var changes = ImmutableArray.CreateBuilder<CoordinatedSettingValueChange>();
        foreach (var item in candidate.Mutations)
        {
            var effect = effects.Single(effect => effect.Key == item.Key);
            var change = await WriteAsync(effect, token, publicationEffect: true);
            if (change is not null) changes.Add(change);
        }
        return new(changes.ToImmutable());
    }

    private async Task<CoordinatedSettingValueChange?> WriteAsync(
        TypedSettingsEffect effect, CancellationToken token, bool publicationEffect = false)
    {
        try { return await WriteCoreAsync(effect, token, publicationEffect); }
        catch { closed = true; throw; }
    }

    private async Task<CoordinatedSettingValueChange?> WriteCoreAsync(
        TypedSettingsEffect effect, CancellationToken token, bool publicationEffect)
    {
        Require(original!);
        if (!effects.Contains(effect) || !written.Add(effect.Key))
            throw new UnauthorizedAccessException("A typed writer cannot widen or repeat its original effect.");
        await VerifyAsync(original!, token);
        Type type = original!.TenantId.HasValue ? typeof(TenantSetting) : typeof(SystemSetting);
        var rows = expected![type.Name]!.AsArray();
        var old = rows.OfType<JsonObject>().SingleOrDefault(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>() == effect.Key
            && (!original.TenantId.HasValue || row[nameof(TenantSetting.TenantId)]!.GetValue<string>() == original.TenantId.Value.ToString("D")));
        Guid id = old is not null ? Guid.Parse(old["Id"]!.GetValue<string>())
            : publicationEffect && original.TenantId is null ? effect.Key switch
            {
                GovernanceSettingKeys.EventReporting.IntakeEnabled => SeedIds.SystemSettingEventReportingIntakeEnabledId,
                GovernanceSettingKeys.Events.RequireApproval => SeedIds.SystemSettingRequireApprovalId,
                GovernanceSettingKeys.Events.UserSubmissionEnabled => SeedIds.SystemSettingUserSubmissionEnabledId,
                GovernanceSettingKeys.Events.OrganizationSubmissionEnabled => SeedIds.SystemSettingOrgSubmissionEnabledId,
                GovernanceSettingKeys.Events.GroupSubmissionEnabled => SeedIds.SystemSettingGroupSubmissionEnabledId,
                _ => throw new UnauthorizedAccessException("Only an original publication key has a stable identity.")
            } : Guid.CreateVersion7();
        bool unchangedPublication = publicationEffect && old is not null && !effect.Remove
            && old["Value"]!.GetValue<string>() == effect.Value
            && (original.TenantId.HasValue || old["IsLocked"]!.GetValue<bool>() == effect.IsLocked);
        JsonObject? after = null;
        if (!effect.Remove)
        {
            if (old is not null)
                after = old.DeepClone().AsObject();
            else
            {
                object entity = original.TenantId is { } tenant ? new TenantSetting
                {
                    Id = id,
                    TenantId = tenant,
                    Tenant = null!,
                    SettingKey = effect.Key,
                    Value = effect.Value!,
                    IsLocked = false,
                    CreatedAt = occurred,
                    CreatedBy = original.Actor
                } : new SystemSetting
                {
                    Id = id,
                    SettingKey = effect.Key,
                    Value = effect.Value!,
                    IsLocked = effect.IsLocked!.Value,
                    ValueType = SettingRegistry.Get(effect.Key)!.ValueType,
                    Description = SettingRegistry.Get(effect.Key)!.Description,
                    Category = SettingRegistry.Get(effect.Key)!.Category,
                    AllowedValues = SettingRegistry.Get(effect.Key)!.AllowedValues is { } allowed ? JsonSerializer.Serialize(allowed) : null,
                    DisplayOrder = publicationEffect ? PublicationPolicySettingKeys.All.ToList().IndexOf(effect.Key) + 1 : 0,
                    CreatedAt = occurred,
                    CreatedBy = original.Actor
                };
                after = Snapshot(entity, type);
            }
            after["Value"] = effect.Value;
            if (original.Governance is { } governance && governance.SuppliesValue(effect.Key))
            {
                var definition = SettingRegistry.Get(effect.Key)!;
                after["SettingValueTypeId"] = (int)definition.ValueType;
                after["AllowedValues"] = null;
                after["Description"] = definition.Description;
                after["Category"] = definition.Category;
                after["DisplayOrder"] = 0;
            }
            if (!original.TenantId.HasValue || original.PlanAssignment is not null && effect.IsLocked.HasValue)
                after["IsLocked"] = effect.IsLocked;
            if (old is not null && !unchangedPublication)
            {
                after["UpdatedAt"] = occurred.ToString("O", CultureInfo.InvariantCulture);
                after["UpdatedBy"] = original.Actor.ToString("D");
            }
        }
        var change = new JsonObject
        {
            ["Type"] = type.Name,
            ["Id"] = id.ToString("D"),
            ["Key"] = effect.Key,
            ["TenantId"] = original.TenantId?.ToString("D"),
            ["Before"] = old?.DeepClone(),
            ["After"] = after?.DeepClone()
        };
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.WriteTypedAsync(context, enrollment!, original,
                new JsonArray(change).ToJsonString(), token);
        else
            await TypedSettingsSqliteWriter.WriteAsync(context, transaction, type, id, old, after, token);
        if (old is not null) rows.Remove(old);
        if (after is not null) rows.Add(after);
        expected[type.Name] = new JsonArray(rows.OfType<JsonObject>().OrderBy(row => row["Id"]!.ToString(), StringComparer.Ordinal)
            .Select(row => row.DeepClone()).ToArray());
        await source!.RequireSourcesAsync(token);
        source.RequireTime(clock.GetUtcNow());
        return unchangedPublication || old is null && after is null ? null
            : new(effect.Key, old?["Value"]?.GetValue<string>(), effect.Value);
    }

    public async Task VerifyAsync(TypedSettingsOriginal request, CancellationToken token)
    {
        Require(request);
        if (!MixedSettingsDataImage.Same(expected!.ToJsonString(), await ReadDataAsync(token)))
            throw new UnauthorizedAccessException("The original typed Settings image changed outside its private writer.");
        await source!.RequireSourcesAsync(token);
        source.RequireTime(clock.GetUtcNow());
    }

    public void RequireObservedData(TypedSettingsOriginal request, string observedData)
    {
        Require(request);
        if (!MixedSettingsDataImage.Same(expected!.ToJsonString(), observedData))
            throw new UnauthorizedAccessException("The terminal typed source differs from its privately compiled image.");
    }

    public async Task RequireCompleteAsync(TypedSettingsOriginal request, CancellationToken token)
    {
        Require(request);
        if (completed || !invoked || rejected && written.Count != 0
            || !rejected && effects.Any(effect => !written.Contains(effect.Key)))
            throw new UnauthorizedAccessException("Every original typed effect requires private completion.");
        if (request.PlanAssignment is not null && !rejected && PlanRequiresAudit && !planAuditWritten)
            throw new UnauthorizedAccessException("The original plan assignment audit has not completed.");
        if (request.Governance is not null && !rejected
            && (governanceProjections.Any(projection => !projection.Written)
                || governanceEmailRevisions.Any(revision => !revision.Written)
                || effects.Any(effect => EmailDeliverySettingKeys.Contains(effect.Key)) && !governanceEmailCompleted))
            throw new UnauthorizedAccessException("The original governance projections and SMTP writer must complete.");
        await VerifyAsync(request, token);
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresMixedSettingsContract.CompleteTypedAsync(context, enrollment!, request,
                new JsonObject { ["Data"] = expected!.DeepClone(), ["Rejected"] = rejected }.ToJsonString(), token);
        completed = true;
    }

    public void Close(TypedSettingsOriginal request)
    {
        if (!ReferenceEquals(original, request))
            throw new UnauthorizedAccessException("Only the original typed owner can release its source.");
        closed = true;
        if (request.PlanAssignment is not null)
            Plans.Remove(context);
        transaction = null;
    }

    private void RequireSelection(TypedSettingsOriginal request)
    {
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(context);
        services.GetRequiredService<IHumanSettingsMutationPreparation>().RequireTypedSource(
            source ?? throw new UnauthorizedAccessException("Typed original observation is required."));
        if (closed || !ReferenceEquals(request, original) || context.ContextId != contextId
            || connection is null || !connection.Matches(context)
            || lease.PolicyPurpose != PolicyMutationPurpose.MixedSettings || lease.SelectedTenantId != request.TenantId
            || !ReferenceEquals(lease.HumanScope?.Subject, source.Subject))
            throw new UnauthorizedAccessException("The original typed data custody changed.");
        if (request.PlanAssignment is not null && planDeploymentValues is not null
            && !planDeploymentValues.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .SequenceEqual(CapturePlanDeployment().OrderBy(pair => pair.Key, StringComparer.Ordinal)))
            throw new UnauthorizedAccessException("Original plan deployment inputs changed.");
    }

    private DbTransaction RequireTransaction() =>
        context.Database.CurrentTransaction?.GetDbTransaction() is { } physical
            && physical.IsolationLevel == IsolationLevel.Serializable && connection is not null && connection.Owns(physical)
            && System.Transactions.Transaction.Current is null ? physical
            : throw new UnauthorizedAccessException("The original typed Serializable owner is required.");

    private void Require(TypedSettingsOriginal request)
    {
        RequireSelection(request);
        if (effects.IsDefault || !ReferenceEquals(transaction, RequireTransaction()))
            throw new UnauthorizedAccessException("The original acquired typed compilation is required.");
    }

    private Task<string> ReadDataAsync(CancellationToken token) => (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        ? PostgresMixedSettingsContract.ReadTypedDataAsync(context, enrollment!, original!, token)
        : MixedSettingsDataImage.ReadTypedSqliteAsync(context, original!, token);

    private ImmutableArray<T> Rows<T>() => MixedSettingsDataImage.Rows<T>(expected!.ToJsonString());

    private JsonObject Snapshot(object entity, Type type)
    {
        var row = new JsonObject();
        foreach (var property in context.Model.FindEntityType(type)!.GetProperties())
        {
            object? value = property.GetGetter().GetClrValue(entity);
            if (type == typeof(Explore.Domain.Settings.Documents.TenantSettingsDocument)
                && property.Name == nameof(Explore.Domain.Settings.Documents.TenantSettingsDocument.PayloadJson)
                || type == typeof(Explore.Domain.Modules.TenantCapability)
                && property.Name == nameof(Explore.Domain.Modules.TenantCapability.ConfigurationJson)
                || property.GetRelationalTypeMapping().StoreType is "json" or "jsonb")
            {
                row[property.Name] = value is string json ? JsonNode.Parse(json) : null;
                continue;
            }
            if (value is Guid guid) value = guid.ToString("D");
            else if (value is DateTime date) value = DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
            row[property.Name] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
        }
        return row;
    }
}
