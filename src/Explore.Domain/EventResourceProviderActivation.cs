using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class EventResourceProviderActivation : IAuditableEntity, IConcurrencyAware
{
    private EventResourceProviderActivation()
    {
    }

    public Guid Id { get; private set; }
    public long Epoch { get; private set; }
    public Guid CurrentOperationId { get; private set; }
    public EventResourceProviderActivationStateEnum State { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static EventResourceProviderActivation Create(
        Guid deploymentId,
        DateTime createdAt)
    {
        RequireVersion7(deploymentId, nameof(deploymentId));
        RequireUtc(createdAt, nameof(createdAt));

        return new EventResourceProviderActivation
        {
            Id = deploymentId,
            Epoch = 0,
            CurrentOperationId = Guid.Empty,
            State = EventResourceProviderActivationStateEnum.Failed,
            CreatedAt = createdAt
        };
    }

    public long BeginOperation(Guid operationId, DateTime transitionedAt)
    {
        RequireVersion7(operationId, nameof(operationId));
        RequireForwardTimestamp(transitionedAt, nameof(transitionedAt));

        long nextEpoch = checked(Epoch + 1);
        Epoch = nextEpoch;
        CurrentOperationId = operationId;
        State = EventResourceProviderActivationStateEnum.Transitioning;
        UpdatedAt = transitionedAt;
        return nextEpoch;
    }

    public bool TryActivate(
        Guid operationId,
        long epoch,
        bool previousWritersStopped,
        int reachableReplicaCount,
        int declaredPolicyReplicaCount,
        DateTime activatedAt)
    {
        if (!Owns(operationId, epoch)
            || State != EventResourceProviderActivationStateEnum.Transitioning)
        {
            return false;
        }

        RequireForwardTimestamp(activatedAt, nameof(activatedAt));
        if (!previousWritersStopped
            || reachableReplicaCount <= 0
            || declaredPolicyReplicaCount != reachableReplicaCount)
        {
            State = EventResourceProviderActivationStateEnum.Failed;
            UpdatedAt = activatedAt;
            return false;
        }

        State = EventResourceProviderActivationStateEnum.Active;
        UpdatedAt = activatedAt;
        return true;
    }

    public bool TryFail(Guid operationId, long epoch, DateTime failedAt)
    {
        if (!Owns(operationId, epoch)
            || State == EventResourceProviderActivationStateEnum.Failed)
        {
            return false;
        }

        RequireForwardTimestamp(failedAt, nameof(failedAt));
        State = EventResourceProviderActivationStateEnum.Failed;
        UpdatedAt = failedAt;
        return true;
    }

    public bool HasActiveAuthority(long expectedEpoch, Guid expectedOperationId) =>
        State == EventResourceProviderActivationStateEnum.Active
        && Epoch == expectedEpoch
        && CurrentOperationId == expectedOperationId
        && expectedEpoch > 0
        && expectedOperationId != Guid.Empty;

    private bool Owns(Guid operationId, long epoch) =>
        operationId != Guid.Empty
        && operationId == CurrentOperationId
        && epoch > 0
        && epoch == Epoch;

    private void RequireForwardTimestamp(DateTime value, string parameterName)
    {
        RequireUtc(value, parameterName);
        if (value < (UpdatedAt ?? CreatedAt))
        {
            throw new ArgumentException(
                "Activation transitions cannot move backwards in time.",
                parameterName);
        }
    }

    private static void RequireVersion7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7)
        {
            throw new ArgumentException("A UUIDv7 identity is required.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("A UTC timestamp is required.", parameterName);
        }
    }
}
