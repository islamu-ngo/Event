using System.Data;
using System.Collections.Immutable;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

/// <summary>
/// Original Global command/data custody. No tracked entity is flushed: the private
/// compiler fixes the complete binding/activation after-images before any write.
/// </summary>
public sealed class EventResourceProviderOriginalStore(ExploreDbContext context, IServiceProvider services)
    : IEventResourceProviderOriginalStore
{
    private EventResourceProviderOriginal? original;
    private IEventResourceProviderOriginalSource? source;
    private ExploreDbContextExecutionLease? lease;
    private DbContextId contextId;
    private RelationalConnectionIdentity? connection;
    private DbTransaction? transaction;
    private int? backend;
    private string? database;
    private string? dataSource;
    private object? lifetime;
    private string? before;
    private string? nativeBefore;
    private string? requestImage;
    private JsonObject? expected;
    private JsonObject? settingBefore;
    private JsonObject? settingAfter;
    private JsonObject? activationBefore;
    private JsonObject? activationAfter;
    private EventResourceProviderOperation? operation;
    private bool activated;
    private bool compiled;
    private bool applied;
    private bool completed;
    private bool closed;

    public async Task ObserveAsync(EventResourceProviderOriginal request, IEventResourceProviderOriginalSource selected,
        CancellationToken token)
    {
        if (original is not null) throw Denied();
        original = request;
        source = selected;
        lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lifetime = lease.HumanLifetime;
        connection = RelationalConnectionIdentity.Capture(context);
        contextId = context.ContextId;
        lease.RequireOriginalOwner(context);
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw Denied();
        if (connection.State != ConnectionState.Open)
            await context.Database.OpenConnectionAsync(token);
        database = connection.Database;
        dataSource = connection.DataSource;
        backend = connection.BackendProcessId;
        RequireSelection();
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
        {
            nativeBefore = await PostgresEventResourceProviderContract.ObserveAsync(context, lease.HumanScope!.Enrollment!, token);
            before = JsonNode.Parse(nativeBefore)!["Data"]!.ToJsonString();
        }
        else before = await ReadSqliteAsync(token);
        expected = JsonNode.Parse(before)!.AsObject();
        RequireSelection();
    }

    public async Task AcquireAndCompileAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        RequireOriginal(request);
        if (compiled || transaction is not null) throw Denied();
        transaction = RequireTransaction();
        await source!.RequireSourcesAsync(token);
        requestImage = request.Serialize();
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresEventResourceProviderContract.AcquireAsync(context, lease!.HumanScope!.Enrollment!,
                request, nativeBefore!, token);
        if (!Same(before!, await ReadAsync(token))) throw Conflict();
        Compile();
        compiled = true;
        await source.RequireSourcesAsync(token);
    }

    public EventResourceProviderPublicationInput ReadPublicationInput(EventResourceProviderOriginal request)
    {
        RequireOriginal(request);
        if (request.Publication is null || transaction is not null || compiled || expected is null) throw Denied();
        var system = expected[nameof(SystemSetting)]!.AsArray().OfType<JsonObject>().ToArray();
        var tenant = expected[nameof(TenantSetting)]!.AsArray().OfType<JsonObject>()
            .Where(row => row["TenantId"]!.GetValue<string>() == request.AmbientTenant.ToString("D")).ToArray();
        return new(request.AmbientTenant, HumanAuthorizationRouteFacts.Keys.Select(key =>
        {
            var s = system.SingleOrDefault(row => row["SettingKey"]!.GetValue<string>() == key);
            var t = tenant.SingleOrDefault(row => row["SettingKey"]!.GetValue<string>() == key);
            return new HumanAuthorizationRouteSettingFact(key,
                s is null ? null : new(s["Value"]!.GetValue<string>(),
                    (SettingValueType)s["SettingValueTypeId"]!.GetValue<int>(), s["IsLocked"]!.GetValue<bool>(),
                    s["Description"]?.GetValue<string>(), s["Category"]?.GetValue<string>(), s["AllowedValues"]?.GetValue<string>()),
                t is null ? null : new(t["Value"]!.GetValue<string>(), t["IsLocked"]!.GetValue<bool>()));
        }).ToImmutableArray());
    }

    private void Compile()
    {
        var request = original!;
        settingBefore = expected![nameof(SystemSetting)]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(row => row[nameof(SystemSetting.SettingKey)]!.GetValue<string>()
                == EventResourceProviderBindingDocument.SettingKey);
        var document = EventResourceProviderBindingDocument.Parse(settingBefore?["Value"]?.GetValue<string>());
        Guid deployment;
        EventResourceDeploymentBinding binding;
        if (request.Bind is { } bind)
        {
            if (document.Revision != bind.ExpectedRevision) throw Conflict();
            binding = bind.Binding;
            deployment = binding.DeploymentId;
            var previous = document.Deployments.SingleOrDefault(item => item.DeploymentId == deployment);
            if (previous is not null && previous.Endpoints.Except(binding.Endpoints, StringComparer.Ordinal).Any())
                throw new ArgumentException("Previously registered aliases cannot be removed.");
            if (document.Deployments.Where(item => item.DeploymentId != deployment)
                .SelectMany(item => item.Endpoints).Intersect(binding.Endpoints, StringComparer.Ordinal).Any())
                throw new ArgumentException("An endpoint alias cannot be reassigned to another deployment.");
            document = new(request.Revision, document.Deployments.Where(item => item.DeploymentId != deployment)
                .Append(binding).ToArray());
        }
        else
        {
            if (request.Publication is not null)
            {
                var endpoint = request.PublicationSelection?.GrpcEndpoint ?? throw Denied();
                var selected = Explore.Application.Utilities.GrpcEndpointNormalizer.IsValid(endpoint)
                    ? document.Deployments.SingleOrDefault(item => item.Endpoints.Contains(
                        EventResourceProviderBindingDocument.NormalizeEndpoint(endpoint), StringComparer.Ordinal)) : null;
                if (selected is null) return;
                deployment = selected.DeploymentId;
            }
            else deployment = request.Begin?.DeploymentId ?? request.Activate!.DeploymentId;
            binding = document.Deployments.SingleOrDefault(item => item.DeploymentId == deployment)
                ?? throw new ArgumentException("The deployment is not bound.");
        }
        activationBefore = expected[nameof(EventResourceProviderActivation)]!.AsArray().OfType<JsonObject>()
            .SingleOrDefault(row => row["Id"]!.GetValue<string>() == deployment.ToString("D"));
        if (activationBefore is null && request.Bind is null) throw Conflict();
        var activation = EventResourceProviderActivation.Create(deployment,
            activationBefore?["CreatedAt"]?.GetValue<DateTime>() ?? request.OccurredAt);
        if (activationBefore is not null)
            foreach (var property in context.Model.FindEntityType(typeof(EventResourceProviderActivation))!.GetProperties())
                property.PropertyInfo!.SetValue(activation, activationBefore[property.Name]?.Deserialize(property.ClrType));
        else activation.CreatedBy = request.Actor;
        if (request.Activate is { } activate)
        {
            var state = activation.State;
            activated = activation.TryActivate(activate.OperationId, activate.Epoch,
                activate.PreviousWritersStopped && activate.Scope == binding.Scope
                    && activate.PolicyVersion == binding.PolicyVersion && activate.FrozenParentPolicyContractConfirmed,
                activate.ReachableReplicaCount, activate.DeclaredPolicyReplicaCount, request.OccurredAt);
            if (state == activation.State) return;
        }
        else
        {
            long epoch = activation.BeginOperation(request.OperationId, request.OccurredAt);
            operation = new(deployment, request.OperationId, epoch, request.Revision);
        }
        activation.UpdatedBy = request.Actor;
        activation.ConcurrencyStamp = request.ConcurrencyStamp;
        activationAfter = Snapshot(activation);
        var next = document with { Revision = request.Revision };
        settingAfter = settingBefore?.DeepClone().AsObject() ?? Snapshot(new SystemSetting
        {
            Id = request.SettingId,
            SettingKey = EventResourceProviderBindingDocument.SettingKey,
            Value = "",
            ValueType = SettingValueType.Json,
            Category = "Cerbos",
            IsLocked = true,
            CreatedAt = request.OccurredAt,
            CreatedBy = request.Actor
        });
        settingAfter["Value"] = JsonSerializer.Serialize(next, EventResourceProviderBindingDocument.JsonOptions);
        settingAfter["IsLocked"] = true;
        settingAfter["UpdatedAt"] = Instant(request.OccurredAt);
        settingAfter["UpdatedBy"] = request.Actor.ToString("D");
        Replace(typeof(SystemSetting), settingBefore, settingAfter);
        Replace(typeof(EventResourceProviderActivation), activationBefore, activationAfter);
    }

    public async Task<EventResourceProviderOperation> ApplyBindAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        if (request.Bind is null) throw Denied();
        await ApplyAsync(request, token);
        return operation ?? throw Denied();
    }

    public async Task<EventResourceProviderOperation> ApplyBeginAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        if (request.Begin is null) throw Denied();
        await ApplyAsync(request, token);
        return operation ?? throw Denied();
    }

    public async Task<bool> ApplyActivateAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        if (request.Activate is null) throw Denied();
        await ApplyAsync(request, token);
        return activated;
    }

    public async Task<IEventResourceProviderPublicationSelection> ApplyPublicationAsync(
        EventResourceProviderOriginal request, CancellationToken token)
    {
        if (request.Publication is null || request.PublicationSelection is null) throw Denied();
        await ApplyAsync(request, token);
        return request.PublicationSelection;
    }

    private async Task ApplyAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        try
        {
            Require(request);
            if (applied || !Same(before!, await ReadAsync(token))) throw Denied();
            await source!.RequireSourcesAsync(token);
            if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
                await PostgresEventResourceProviderContract.ApplyOriginalAsync(context, lease!.HumanScope!.Enrollment!,
                    request, expected!.ToJsonString(), token);
            else if (settingAfter is not null)
            {
                await EventResourceProviderSqliteWriter.WriteAsync(
                    context, connection!, transaction, typeof(SystemSetting), settingBefore, settingAfter, token);
                await EventResourceProviderSqliteWriter.WriteAsync(
                    context, connection!, transaction, typeof(EventResourceProviderActivation), activationBefore, activationAfter!, token);
            }
            applied = true;
            await VerifyAsync(request, token);
        }
        catch { closed = true; throw; }
    }

    public async Task VerifyAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        Require(request);
        var image = applied ? expected!.ToJsonString() : before!;
        if (!Same(image, await ReadAsync(token))) throw Conflict();
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresEventResourceProviderContract.VerifyAsync(context, lease!.HumanScope!.Enrollment!, request, image, token);
        await source!.RequireSourcesAsync(token);
        Require(request);
    }

    public async Task RequireCompleteAsync(EventResourceProviderOriginal request, CancellationToken token)
    {
        Require(request);
        if (!applied || completed) throw Denied();
        await VerifyAsync(request, token);
        if ((RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql))
            await PostgresEventResourceProviderContract.CompleteAsync(context, lease!.HumanScope!.Enrollment!,
                request, expected!.ToJsonString(), token);
        // The last asynchronous admission work is a source read, never a deadline sample.
        await source!.RequireSourcesAsync(token);
        completed = true;
    }

    public void Close(EventResourceProviderOriginal request)
    {
        if (original is not null && !ReferenceEquals(request, original)) throw Denied();
        closed = true;
        transaction = null;
    }

    private void RequireSelection()
    {
        if (lease is null || original is null || source is null || connection is null) throw Denied();
        lease.RequireOriginalOwner(context);
        services.GetRequiredService<IHumanSettingsMutationPreparation>().RequireEventResourceProviderSource(source!);
        if (closed || !ReferenceEquals(context, services.GetRequiredService<ExploreDbContext>())
            || context.ContextId != contextId || !connection.Matches(context)
            || connection!.State != ConnectionState.Open || connection.Database != database || connection.DataSource != dataSource
            || connection.BackendProcessId is { } processId && processId != backend
            || lease.SelectedTenantId is not null || lease.HumanScope?.Purpose != HumanAdministratorCommitPurpose.GlobalInstance
            || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || !ReferenceEquals(lease.HumanScope.Subject, source!.Subject) || source.Subject.PrincipalId != original!.Actor
            || lifetime is null || !ReferenceEquals(lease.HumanLifetime, lifetime)
            || context.HumanCommitContextLease != contextId || !ReferenceEquals(context.HumanCommitAuditSubject, source.Subject)
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
            throw Denied();
    }

    private void RequireOriginal(EventResourceProviderOriginal request)
    {
        RequireSelection();
        if (!ReferenceEquals(original, request)) throw Denied();
    }

    private DbTransaction RequireTransaction()
    {
        RequireSelection();
        return context.Database.CurrentTransaction?.GetDbTransaction() is { } selected
            && selected.IsolationLevel == IsolationLevel.Serializable && connection!.Owns(selected)
            && context.HumanCommitAttemptOwnsTransaction && System.Transactions.Transaction.Current is null
            ? selected : throw Denied();
    }

    private void Require(EventResourceProviderOriginal request)
    {
        RequireOriginal(request);
        if (!compiled || !ReferenceEquals(transaction, RequireTransaction()) || request.Serialize() != requestImage)
            throw Denied();
    }

    private Task<string> ReadAsync(CancellationToken token) => (RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.PostgreSql)
        ? PostgresEventResourceProviderContract.ReadDataAsync(context, lease!.HumanScope!.Enrollment!, token)
        : ReadSqliteAsync(token);

    private async Task<string> ReadSqliteAsync(CancellationToken token)
    {
        if (!(RelationalProviderClassifier.Classify(context.Database) == RelationalProvider.Sqlite)) throw new NotSupportedException("resource_provider_original_provider_not_supported");
        var settings = await context.Set<SystemSetting>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(token);
        var activations = await context.Set<EventResourceProviderActivation>().AsNoTracking().OrderBy(row => row.Id).ToListAsync(token);
        var tenants = await context.Set<TenantSetting>().IgnoreAllFilters(TenantFilterBypassReasons.EventResourceProviderOriginalSnapshot).AsNoTracking().OrderBy(row => row.Id).ToListAsync(token);
        return new JsonObject
        {
            [nameof(SystemSetting)] = new JsonArray(settings.Select(row => (JsonNode)Snapshot(row)).ToArray()),
            [nameof(TenantSetting)] = new JsonArray(tenants.Select(row => (JsonNode)Snapshot(row)).ToArray()),
            [nameof(EventResourceProviderActivation)] = new JsonArray(activations.Select(row => (JsonNode)Snapshot(row)).ToArray())
        }.ToJsonString();
    }

    private JsonObject Snapshot(object entity)
    {
        var row = new JsonObject();
        foreach (var property in context.Model.FindEntityType(entity.GetType())!.GetProperties())
        {
            object? value = property.GetGetter().GetClrValue(entity);
            if (value is Guid id) value = id.ToString("D");
            if (value is DateTime time) value = Instant(time);
            row[property.Name] = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
        }
        return row;
    }

    private void Replace(Type type, JsonObject? old, JsonObject value)
    {
        var rows = expected![type.Name]!.AsArray();
        if (old is not null) rows.Remove(old);
        rows.Add(value.DeepClone());
        expected[type.Name] = new JsonArray(rows.OfType<JsonObject>().OrderBy(row => row["Id"]!.ToString(), StringComparer.Ordinal)
            .Select(row => row.DeepClone()).ToArray());
    }

    private static string Instant(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture);
    private static bool Same(string left, string right) => MixedSettingsDataImage.Same(left, right);
    private static UnauthorizedAccessException Denied() => new("The original resource-provider action and selected data owner are required.");
    private static ConcurrencyConflictException Conflict() => new(ConcurrencyConflictException.ConcurrentUpdate,
        "The resource provider binding or activation changed; reload and retry.");
}
