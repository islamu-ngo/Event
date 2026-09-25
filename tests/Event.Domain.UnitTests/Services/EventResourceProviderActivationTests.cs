using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Services;

public sealed class EventResourceProviderActivationTests
{
    private static readonly DateTime CreatedAt =
        new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Create_IsGlobalAndInitiallyFailClosed()
    {
        Guid deploymentId = Guid.CreateVersion7();

        EventResourceProviderActivation activation =
            EventResourceProviderActivation.Create(deploymentId, CreatedAt);

        await Assert.That(activation.Id).IsEqualTo(deploymentId);
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Failed);
        await Assert.That(activation.Epoch).IsEqualTo(0);
        await Assert.That(activation.CurrentOperationId).IsEqualTo(Guid.Empty);
        await Assert.That(activation.HasActiveAuthority(0, Guid.Empty)).IsFalse();
        await Assert.That(activation.HasActiveAuthority(1, Guid.CreateVersion7())).IsFalse();
        await Assert.That(activation).IsAssignableTo<IAuditableEntity>();
        await Assert.That(activation).IsAssignableTo<IConcurrencyAware>();
        await Assert.That(activation).IsNotAssignableTo<ITenantEntity>();
        await Assert.That(activation.ConcurrencyStamp).IsEqualTo(Guid.Empty);
        await Assert.That(Enum.GetValues<EventResourceProviderActivationStateEnum>())
            .IsEquivalentTo([
                EventResourceProviderActivationStateEnum.Transitioning,
                EventResourceProviderActivationStateEnum.Active,
                EventResourceProviderActivationStateEnum.Failed]);
    }

    [Test]
    public async Task BeginOperation_AdvancesLocalEpochAndImmediatelyClosesActiveAuthority()
    {
        EventResourceProviderActivation activation = Create();
        Guid firstOperation = Guid.CreateVersion7();
        long firstEpoch = activation.BeginOperation(firstOperation, CreatedAt.AddMinutes(1));
        activation.TryActivate(
            firstOperation,
            firstEpoch,
            previousWritersStopped: true,
            reachableReplicaCount: 2,
            declaredPolicyReplicaCount: 2,
            CreatedAt.AddMinutes(2));
        Guid stamp = Guid.CreateVersion7();
        activation.ConcurrencyStamp = stamp;

        Guid secondOperation = Guid.CreateVersion7();
        long secondEpoch = activation.BeginOperation(secondOperation, CreatedAt.AddMinutes(3));

        await Assert.That(firstEpoch).IsEqualTo(1);
        await Assert.That(secondEpoch).IsEqualTo(2);
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
        await Assert.That(activation.CurrentOperationId).IsEqualTo(secondOperation);
        await Assert.That(activation.HasActiveAuthority(firstEpoch, firstOperation)).IsFalse();
        await Assert.That(activation.HasActiveAuthority(secondEpoch, secondOperation)).IsFalse();
        await Assert.That(activation.ConcurrencyStamp).IsEqualTo(stamp);
    }

    [Test]
    [Arguments(false, 2, 2)]
    [Arguments(true, 0, 0)]
    [Arguments(true, 2, 1)]
    [Arguments(true, 2, 3)]
    public async Task Activate_WithoutCompletePositiveEvidence_RemainsClosed(
        bool previousWritersStopped,
        int reachableReplicaCount,
        int declaredPolicyReplicaCount)
    {
        EventResourceProviderActivation activation = Create();
        Guid operationId = Guid.CreateVersion7();
        long epoch = activation.BeginOperation(operationId, CreatedAt.AddMinutes(1));

        bool activated = activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped,
            reachableReplicaCount,
            declaredPolicyReplicaCount,
            CreatedAt.AddMinutes(2));

        bool laterPositiveEvidence = activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped: true,
            reachableReplicaCount: 2,
            declaredPolicyReplicaCount: 2,
            CreatedAt.AddMinutes(3));

        await Assert.That(activated).IsFalse();
        await Assert.That(laterPositiveEvidence).IsFalse();
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Failed);
        await Assert.That(activation.HasActiveAuthority(epoch, operationId)).IsFalse();
    }

    [Test]
    public async Task Activate_WithMatchingFenceAndCompleteEvidence_OpensOnlyThatEpoch()
    {
        EventResourceProviderActivation activation = Create();
        Guid operationId = Guid.CreateVersion7();
        long epoch = activation.BeginOperation(operationId, CreatedAt.AddMinutes(1));

        bool activated = activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped: true,
            reachableReplicaCount: 3,
            declaredPolicyReplicaCount: 3,
            CreatedAt.AddMinutes(2));

        await Assert.That(activated).IsTrue();
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Active);
        await Assert.That(activation.HasActiveAuthority(epoch, operationId)).IsTrue();
        await Assert.That(activation.HasActiveAuthority(epoch - 1, operationId)).IsFalse();
        await Assert.That(activation.HasActiveAuthority(epoch, Guid.CreateVersion7())).IsFalse();
    }

    [Test]
    public async Task StaleCompletionAndRepeatedOldOperation_CannotReopenNewerAuthority()
    {
        EventResourceProviderActivation activation = Create();
        Guid repeatedOperation = Guid.CreateVersion7();
        long oldEpoch = activation.BeginOperation(repeatedOperation, CreatedAt.AddMinutes(1));
        long currentEpoch = activation.BeginOperation(repeatedOperation, CreatedAt.AddMinutes(2));

        bool wrongOwnerActivation = activation.TryActivate(
            Guid.CreateVersion7(),
            currentEpoch,
            previousWritersStopped: true,
            reachableReplicaCount: 1,
            declaredPolicyReplicaCount: 1,
            CreatedAt.AddMinutes(3));
        bool staleActivation = activation.TryActivate(
            repeatedOperation,
            oldEpoch,
            previousWritersStopped: true,
            reachableReplicaCount: 1,
            declaredPolicyReplicaCount: 1,
            CreatedAt.AddMinutes(3));
        bool staleFailure = activation.TryFail(
            repeatedOperation,
            oldEpoch,
            CreatedAt.AddMinutes(3));

        await Assert.That(wrongOwnerActivation).IsFalse();
        await Assert.That(staleActivation).IsFalse();
        await Assert.That(staleFailure).IsFalse();
        await Assert.That(activation.Epoch).IsEqualTo(currentEpoch);
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Transitioning);
        await Assert.That(activation.HasActiveAuthority(currentEpoch, repeatedOperation)).IsFalse();
    }

    [Test]
    public async Task MatchingFailure_ClosesAuthorityAndLateSuccessCannotReopenIt()
    {
        EventResourceProviderActivation activation = Create();
        Guid operationId = Guid.CreateVersion7();
        long epoch = activation.BeginOperation(operationId, CreatedAt.AddMinutes(1));
        activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped: true,
            reachableReplicaCount: 1,
            declaredPolicyReplicaCount: 1,
            CreatedAt.AddMinutes(2));

        bool failed = activation.TryFail(operationId, epoch, CreatedAt.AddMinutes(3));
        bool lateActivation = activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped: true,
            reachableReplicaCount: 1,
            declaredPolicyReplicaCount: 1,
            CreatedAt.AddMinutes(4));

        await Assert.That(failed).IsTrue();
        await Assert.That(lateActivation).IsFalse();
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Failed);
        await Assert.That(activation.HasActiveAuthority(epoch, operationId)).IsFalse();
    }

    [Test]
    public async Task ElapsedTimeAndUnknownPersistedState_DoNotOpenAuthority()
    {
        EventResourceProviderActivation activation = Create();
        Guid operationId = Guid.CreateVersion7();
        long epoch = activation.BeginOperation(operationId, CreatedAt.AddMinutes(1));
        DateTime unchangedAt = activation.UpdatedAt!.Value;

        await Assert.That(activation.HasActiveAuthority(epoch, operationId)).IsFalse();
        await Assert.That(activation.UpdatedAt).IsEqualTo(unchangedAt);

        typeof(EventResourceProviderActivation)
            .GetProperty(nameof(EventResourceProviderActivation.State))!
            .SetValue(activation, (EventResourceProviderActivationStateEnum)999);
        bool activatedFromUnknown = activation.TryActivate(
            operationId,
            epoch,
            previousWritersStopped: true,
            reachableReplicaCount: 1,
            declaredPolicyReplicaCount: 1,
            CreatedAt.AddYears(10));

        await Assert.That(activatedFromUnknown).IsFalse();
        await Assert.That(activation.HasActiveAuthority(epoch, operationId)).IsFalse();
    }

    [Test]
    public async Task InvalidIdentitiesAndNonUtcTimestamps_AreRejectedWithoutMutation()
    {
        await Assert.That(() => EventResourceProviderActivation.Create(Guid.NewGuid(), CreatedAt))
            .Throws<ArgumentException>();
        await Assert.That(() => EventResourceProviderActivation.Create(
                Guid.CreateVersion7(),
                DateTime.SpecifyKind(CreatedAt, DateTimeKind.Local)))
            .Throws<ArgumentException>();

        EventResourceProviderActivation activation = Create();
        await Assert.That(() => activation.BeginOperation(Guid.NewGuid(), CreatedAt.AddMinutes(1)))
            .Throws<ArgumentException>();
        await Assert.That(activation.Epoch).IsEqualTo(0);
        await Assert.That(activation.State)
            .IsEqualTo(EventResourceProviderActivationStateEnum.Failed);
    }

    private static EventResourceProviderActivation Create() =>
        EventResourceProviderActivation.Create(Guid.CreateVersion7(), CreatedAt);
}
