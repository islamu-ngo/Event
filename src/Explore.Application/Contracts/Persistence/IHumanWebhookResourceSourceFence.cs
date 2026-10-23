using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.Features.Webhooks.Requests.Commands;

namespace Explore.Application.Contracts.Persistence;

public interface IHumanWebhookResourceSourceFactory
{
    IHumanWebhookResourceSourceFence Create(HumanWebhookResourceTarget target);
}

public interface IHumanWebhookResourceSourceFence
{
    Task<HumanWebhookResourceSourceFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task<IHumanWebhookResourceSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, IHumanSupportAccessSourceLease support,
        IHumanEventAssignmentSourceLease? assignments = null, CancellationToken cancellationToken = default);
}

public interface IHumanWebhookResourceSourceLease
{
    HumanWebhookResourceSourceFacts Facts { get; }
    /// <summary>Returns new persisted facts, including this original owner's own writes.</summary>
    Task<HumanWebhookResourceSourceFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>Original PDP input, not a decision or an administrator/delegated-action shortcut.</summary>
public sealed record HumanWebhookResourceInput(string ResourceKind, string ResourceId, string Action, IAuthorizationFacts Facts);

public enum HumanWebhookResourceOperation
{
    CreateConsumer = 1, CreateEndpoint = 2, UpdateConsumer = 3, UpdateEndpoint = 4,
    ArchiveEndpoint = 5, PauseEndpoint = 6, ResumeEndpoint = 7, RotateEndpointSecret = 8,
    TestEndpoint = 9, RetryAttempt = 10, ScheduleBulkReplay = 11, CancelBulkReplay = 12,
    RepairProviderBinding = 13, ReconcilePublication = 14, AbandonPublication = 15, OpenPortal = 16
}

/// <summary>
/// Closed selectors copied from known command types. IDs select persisted facts; they confer no authority.
/// ActorUserId, audit metadata, credentials, portal SessionId and arbitrary resource/action tuples are not inputs.
/// </summary>
public sealed class HumanWebhookResourceTarget
{
    private HumanWebhookResourceTarget(HumanWebhookResourceOperation operation, Guid? id,
        int? ownerKind = null, Guid? ownerId = null, Guid? tenantId = null,
        Guid? consumerId = null, Guid? endpointId = null)
    {
        if (id == Guid.Empty || ownerId == Guid.Empty || tenantId == Guid.Empty
            || consumerId == Guid.Empty || endpointId == Guid.Empty
            || operation != HumanWebhookResourceOperation.CreateConsumer && id is null)
            throw new ArgumentException("Webhook source selectors must be nonempty.");
        if (operation == HumanWebhookResourceOperation.CreateConsumer
            && (ownerKind is null || !Enum.IsDefined(typeof(Explore.Domain.WebhookConsumerKind), ownerKind.Value)))
            throw new ArgumentException("Webhook creation requires a defined owner kind.");
        Operation = operation;
        Id = id;
        OwnerKindId = ownerKind;
        RequestedOwnerId = ownerId;
        RequestedTenantId = tenantId;
        ConsumerFilterId = consumerId;
        EndpointFilterId = endpointId;
    }

    public HumanWebhookResourceOperation Operation { get; }
    public Guid? Id { get; }
    public int? OwnerKindId { get; }
    public Guid? RequestedOwnerId { get; }
    public Guid? RequestedTenantId { get; }
    public Guid? ConsumerFilterId { get; }
    public Guid? EndpointFilterId { get; }

    public static HumanWebhookResourceTarget From(ISecureRequest request) => request switch
    {
        CreateWebhookConsumerCommand value => new(HumanWebhookResourceOperation.CreateConsumer, null,
            value.ConsumerKindId, value.OwnerId),
        CreateWebhookEndpointCommand value => new(HumanWebhookResourceOperation.CreateEndpoint, value.ConsumerId),
        UpdateWebhookConsumerProviderModeCommand value => new(HumanWebhookResourceOperation.UpdateConsumer, value.ConsumerId),
        UpdateWebhookEndpointCommand value => new(HumanWebhookResourceOperation.UpdateEndpoint, value.EndpointId),
        ArchiveWebhookEndpointCommand value => new(HumanWebhookResourceOperation.ArchiveEndpoint, value.EndpointId),
        PauseWebhookEndpointCommand value => new(HumanWebhookResourceOperation.PauseEndpoint, value.EndpointId),
        ResumeWebhookEndpointCommand value => new(HumanWebhookResourceOperation.ResumeEndpoint, value.EndpointId),
        RotateWebhookEndpointSecretCommand value => new(HumanWebhookResourceOperation.RotateEndpointSecret, value.EndpointId),
        TestWebhookEndpointCommand value => new(HumanWebhookResourceOperation.TestEndpoint, value.EndpointId),
        RetryWebhookDeliveryAttemptCommand value => new(HumanWebhookResourceOperation.RetryAttempt, value.AttemptId),
        ScheduleWebhookBulkReplayCommand value => new(HumanWebhookResourceOperation.ScheduleBulkReplay, value.OperationKey,
            tenantId: value.TenantId, consumerId: value.WebhookConsumerId, endpointId: value.WebhookEndpointId),
        CancelWebhookBulkReplayCommand value => new(HumanWebhookResourceOperation.CancelBulkReplay, value.OperationId,
            tenantId: value.TenantId),
        RepairWebhookProviderBindingCommand value => new(HumanWebhookResourceOperation.RepairProviderBinding, value.ConsumerId),
        ReconcileWebhookProviderPublicationCommand value => new(HumanWebhookResourceOperation.ReconcilePublication, value.PublicationId),
        AbandonWebhookProviderPublicationCommand value => new(HumanWebhookResourceOperation.AbandonPublication, value.PublicationId),
        OpenSvixAppPortalCommand value => new(HumanWebhookResourceOperation.OpenPortal, value.ConsumerId),
        _ => throw new NotSupportedException("This command has no human webhook resource source selector.")
    };
}

/// <summary>Tracker-free immutable rows. Initial, acquired and final arrays are distinct observations.</summary>
public sealed record HumanWebhookResourceSourceFacts(
    Guid PrincipalId, Guid? SelectedTenantId, HumanWebhookResourceTarget Target,
    ImmutableArray<HumanWebhookConsumerFact> Consumers,
    ImmutableArray<HumanWebhookEndpointFact> Endpoints,
    ImmutableArray<HumanWebhookAttemptFact> Attempts,
    ImmutableArray<HumanWebhookMessageFact> Messages,
    ImmutableArray<HumanWebhookLocalTargetFact> LocalTargets,
    ImmutableArray<HumanWebhookPlanFact> Plans,
    ImmutableArray<HumanWebhookPublicationFact> Publications,
    ImmutableArray<HumanWebhookBulkReplayFact> BulkReplays,
    ImmutableArray<HumanWebhookBindingFact> Bindings,
    ImmutableArray<HumanWebhookInstanceFact> Instances,
    ImmutableArray<HumanWebhookOwnerFact> Organizations,
    ImmutableArray<HumanWebhookPlacementFact> OrganizationPlacements,
    ImmutableArray<HumanWebhookOwnerFact> Groups,
    ImmutableArray<HumanWebhookPlacementFact> GroupPlacements,
    ImmutableArray<HumanWebhookTenantUserFact> TenantUsers)
{
    public override string ToString() => nameof(HumanWebhookResourceSourceFacts);
}

public sealed record HumanWebhookConsumerFact(Guid Id, Guid? TenantId, Guid? InstanceId, Guid? OrganizationId,
    Guid? GroupId, Guid? OwnerUserId, Guid ConfigurationScopeId, int ConsumerKindId, int StatusId,
    int ProviderModeId, int ConfigurationVersion);
public sealed record HumanWebhookEndpointFact(Guid Id, Guid? TenantId, Guid? InstanceId, Guid ConfigurationScopeId,
    Guid ConsumerId, int StatusId, int ConfigurationVersion, long DeliveryStateVersion);
public sealed record HumanWebhookAttemptFact(Guid Id, Guid TenantId, Guid MessageId, Guid EndpointId,
    int OutcomeId, int AttemptNumber, long ProcessingFence, DateTime? CompletedAt);
public sealed record HumanWebhookMessageFact(Guid Id, Guid TenantId, Guid? ConsumerId,
    DateTime PayloadRetentionUntil, DateTime? PayloadClearedAt, bool HasPayload);
public sealed record HumanWebhookLocalTargetFact(Guid Id, Guid TenantId, Guid WebhookMessageId,
    Guid DeliveryPlanSnapshotId, Guid WebhookEndpointId, int DeliveryStatusId, long ConcurrencyVersion,
    long DeliveryFence, int EndpointConfigurationVersion);
public sealed record HumanWebhookPlanFact(Guid Id, Guid TenantId, Guid WebhookMessageId,
    Guid WebhookConsumerId, int ProviderModeId, string ConfigurationVersion);
public sealed record HumanWebhookPublicationFact(Guid Id, Guid TenantId, Guid WebhookMessageId,
    Guid WebhookDeliveryPlanSnapshotId, Guid ProviderBindingId, int StatusId, long ConcurrencyVersion,
    long PublicationFence, DateTime? AbandonedAt);
public sealed record HumanWebhookBulkReplayFact(Guid Id, Guid TenantId, Guid OperationKey, Guid? WebhookConsumerId,
    Guid? WebhookEndpointId, int StatusId, long ConcurrencyVersion, DateTime? CancelledAt, DateTime? CompletedAt);
public sealed record HumanWebhookBindingFact(Guid Id, Guid? TenantId, Guid WebhookConsumerId, Guid InstanceId,
    Guid ConfigurationScopeId, int ProviderKindId, string ProviderVersion, string ProviderEnvironment, string NormalizedEnvironment,
    string ApplicationUid, string NormalizedApplicationUid, string? ExternalApplicationId, string? NormalizedExternalApplicationId,
    int VerificationStateId, Guid? VerifiedTenantId,
    Guid? VerifiedWebhookConsumerId, DateTimeOffset? VerifiedAtUtc, long Capabilities, long GovernanceAllowedCapabilities,
    string CapabilityResolutionVersion, DateTimeOffset CapabilitiesResolvedAtUtc, bool IsEnabled,
    long ConcurrencyVersion, long VerificationFence);
public sealed record HumanWebhookInstanceFact(Guid Id, int Status, long Generation, DateTime CreatedAt);
public sealed record HumanWebhookOwnerFact(Guid Id, bool IsDeleted);
public sealed record HumanWebhookPlacementFact(Guid Id, Guid TenantId, Guid OwnerId, bool IsDeleted);
public sealed record HumanWebhookTenantUserFact(Guid Id, Guid TenantId, Guid UserId, int StatusId, bool IsDeleted);
