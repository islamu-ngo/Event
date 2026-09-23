using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed record EventResourceProviderOperation(Guid DeploymentId, Guid OperationId, long Epoch, Guid BindingRevision);

/// <summary>
/// Serializes binding and activation changes using the native setting lock and database transaction.
/// Publication is deliberately outside this service: only an explicit convergence attestation can activate.
/// </summary>
public sealed class EventResourceProviderControlPlane(
    ISystemSettingRepository settings,
    IEventResourceProviderActivationRepository activations,
    ISettingMutationLock mutationLock,
    IUnitOfWork unitOfWork,
    IAdminContext adminContext,
    TimeProvider timeProvider) : IEventResourcePolicyPublicationFence
{
    public async Task<EventResourceProviderBindingDocument> ReadBindingsAsync(CancellationToken cancellationToken)
    {
        await RequireAdministratorAsync(cancellationToken);
        return await ReadAsync(cancellationToken);
    }

    public Task<EventResourceProviderOperation> BindAsync(
        EventResourceDeploymentBinding binding, Guid expectedRevision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(binding.Endpoints);
        binding = binding with
        {
            Endpoints = binding.Endpoints.Select(EventResourceProviderBindingDocument.NormalizeEndpoint)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
        binding.Validate();
        var operationId = Guid.CreateVersion7();
        var revision = Guid.CreateVersion7();
        var settingId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return MutateAsync(async token =>
        {
            await RequireAdministratorAsync(token);
            var document = await ReadAsync(token);
            if (document.Revision != expectedRevision)
                throw Conflict();
            var previous = document.Deployments.SingleOrDefault(item => item.DeploymentId == binding.DeploymentId);
            if (previous is not null && previous.Endpoints.Except(binding.Endpoints, StringComparer.Ordinal).Any())
                throw new ArgumentException("Previously registered aliases cannot be removed.");
            if (document.Deployments.Where(item => item.DeploymentId != binding.DeploymentId)
                .SelectMany(item => item.Endpoints).Intersect(binding.Endpoints, StringComparer.Ordinal).Any())
                throw new ArgumentException("An endpoint alias cannot be reassigned to another deployment.");
            var activation = await GetForMutationAsync(binding.DeploymentId, token);
            if (activation is null)
            {
                activation = EventResourceProviderActivation.Create(binding.DeploymentId, now);
                await activations.AddAsync(activation, token);
            }
            var epoch = activation.BeginOperation(operationId, now);
            var next = new EventResourceProviderBindingDocument(revision,
                document.Deployments.Where(item => item.DeploymentId != binding.DeploymentId).Append(binding).ToArray());
            await PersistAsync(next, settingId, now, token);
            return new EventResourceProviderOperation(binding.DeploymentId, operationId, epoch, revision);
        }, cancellationToken);
    }

    public Task<EventResourceProviderOperation> BeginAsync(Guid deploymentId, CancellationToken cancellationToken)
    {
        var operationId = Guid.CreateVersion7();
        var revision = Guid.CreateVersion7();
        var settingId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return MutateAsync(async token =>
        {
            await RequireAdministratorAsync(token);
            var document = await ReadAsync(token);
            RequireBinding(document, deploymentId);
            var activation = await GetForMutationAsync(deploymentId, token) ?? throw Conflict();
            var epoch = activation.BeginOperation(operationId, now);
            await PersistAsync(document with { Revision = revision }, settingId, now, token);
            return new EventResourceProviderOperation(deploymentId, operationId, epoch, revision);
        }, cancellationToken);
    }

    public Task<bool> ActivateAsync(Guid deploymentId, Guid operationId, long epoch,
        string scope, string policyVersion, bool previousWritersStopped,
        int reachableReplicaCount, int declaredPolicyReplicaCount, bool frozenParentPolicyContractConfirmed,
        CancellationToken cancellationToken)
    {
        var revision = Guid.CreateVersion7();
        var settingId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return MutateAsync(async token =>
        {
            await RequireAdministratorAsync(token);
            var document = await ReadAsync(token);
            var binding = RequireBinding(document, deploymentId);
            var activation = await GetForMutationAsync(deploymentId, token) ?? throw Conflict();
            // A mismatched declaration is ambiguous evidence, not a retryable activation attempt.
            var matches = binding.Scope == scope && binding.PolicyVersion == policyVersion
                && frozenParentPolicyContractConfirmed;
            var previousState = activation.State;
            var activated = activation.TryActivate(operationId, epoch, previousWritersStopped && matches,
                reachableReplicaCount, declaredPolicyReplicaCount, now);
            if (activation.State != previousState)
                await PersistAsync(document with { Revision = revision }, settingId, now, token);
            return activated;
        }, cancellationToken);
    }

    public async Task BeginPublicationAsync(string grpcEndpoint, CancellationToken cancellationToken)
    {
        // An absent/invalid PDP route cannot be registered for resource access. Preserve the
        // existing Admin-only, non-resource publication path for those unbound configurations.
        if (!Utilities.GrpcEndpointNormalizer.IsValid(grpcEndpoint))
            return;
        var endpoint = EventResourceProviderBindingDocument.NormalizeEndpoint(grpcEndpoint);
        var operationId = Guid.CreateVersion7();
        var revision = Guid.CreateVersion7();
        var settingId = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await MutateAsync(async token =>
        {
            var document = await ReadAsync(token);
            var binding = document.Deployments.SingleOrDefault(item => item.Endpoints.Contains(endpoint, StringComparer.Ordinal));
            if (binding is null)
                return false; // Unbound providers have no resource authority to revoke.
            var activation = await GetForMutationAsync(binding.DeploymentId, token) ?? throw Conflict();
            activation.BeginOperation(operationId, now);
            await PersistAsync(document with { Revision = revision }, settingId, now, token);
            return true;
        }, cancellationToken);
    }

    private Task<T> MutateAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteReadCommittedAsync(token => mutationLock.ExecuteAsync(
            EventResourceProviderBindingDocument.SettingKey, operation, token), cancellationToken);

    private async Task RequireAdministratorAsync(CancellationToken cancellationToken)
    {
        if (!await adminContext.IsInstanceAdminAsync(cancellationToken))
            throw new AuthorizationException("Resource provider control-plane operations require current instance administrator authority.");
    }

    private async Task<EventResourceProviderBindingDocument> ReadAsync(CancellationToken cancellationToken) =>
        EventResourceProviderBindingDocument.Parse((await settings.GetByKey(
            EventResourceProviderBindingDocument.SettingKey, cancellationToken))?.Value);

    private async Task<EventResourceProviderActivation?> GetForMutationAsync(Guid deploymentId, CancellationToken cancellationToken)
    {
        var fresh = await activations.GetAsync(deploymentId, cancellationToken);
        var tracked = await activations.GetForUpdateAsync(deploymentId, cancellationToken);
        if (fresh?.ConcurrencyStamp != tracked?.ConcurrencyStamp)
            throw Conflict();
        return tracked;
    }

    private static EventResourceDeploymentBinding RequireBinding(EventResourceProviderBindingDocument document, Guid deploymentId) =>
        document.Deployments.SingleOrDefault(binding => binding.DeploymentId == deploymentId)
        ?? throw new ArgumentException("The deployment is not bound.", nameof(deploymentId));

    private async Task PersistAsync(EventResourceProviderBindingDocument document, Guid settingId, DateTime now, CancellationToken token)
    {
        // Native current-transaction upsert saves all tracked state: document and activation commit together.
        await settings.UpsertInCurrentTransactionAsync(new SystemSetting
        {
            Id = settingId,
            SettingKey = EventResourceProviderBindingDocument.SettingKey,
            Value = JsonSerializer.Serialize(document, EventResourceProviderBindingDocument.JsonOptions),
            ValueType = SettingValueType.Json,
            Category = "Cerbos",
            IsLocked = true,
            CreatedAt = now,
            UpdatedAt = now
        }, token);
    }

    private static ConcurrencyConflictException Conflict() => new(
        ConcurrencyConflictException.ConcurrentUpdate, "The resource provider binding or activation changed; reload and retry.");
}
