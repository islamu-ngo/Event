using Explore.Domain.Keycloak;

namespace Event.Domain.UnitTests.Keycloak;

public sealed class KeycloakOperationTests
{
    private static readonly Guid InstanceId =
        Guid.Parse("11111111-1111-7111-8111-111111111111");
    private static readonly DateTimeOffset Created =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Active = Created.AddMinutes(1);
    private static readonly DateTimeOffset Settled = Created.AddMinutes(2);

    [Test]
    public async Task AuthorizeApply_WithExactApprovalBinding_EntersApplying()
    {
        KeycloakOperation operation = CreateOperation();
        Guid originalStamp = operation.ConcurrencyStamp;

        operation.AuthorizeApply(
            "actor",
            7,
            Target(),
            "digest",
            Active);

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(operation.ConcurrencyStamp)
            .IsNotEqualTo(originalStamp);
        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task AuthorizeApply_WhenAlreadyApplying_RejectsReplay()
    {
        KeycloakOperation operation = ApplyingOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(),
                "digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Applying);
    }

    [Test]
    public async Task AuthorizeApply_WhenTargetChanges_FailsClosed()
    {
        KeycloakOperation operation = CreateOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(authority: "https://other.example.test"),
                "digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task AuthorizeApply_WhenActorChanges_FailsClosed()
    {
        KeycloakOperation operation = CreateOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "different-actor",
                7,
                Target(),
                "digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task AuthorizeApply_WhenSetupGenerationChanges_FailsClosed()
    {
        KeycloakOperation operation = CreateOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                8,
                Target(),
                "digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task AuthorizeApply_WhenDigestChanges_FailsClosed()
    {
        KeycloakOperation operation = CreateOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(),
                "different-digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task AuthorizeApply_AfterExpiry_SettlesExpiredAndRejectsSend()
    {
        KeycloakOperation operation = CreateOperation();
        DateTimeOffset expiredAt = Created.AddHours(1);

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(),
                "digest",
                expiredAt));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Expired);
        await Assert.That(operation.SettledAtUtc).IsEqualTo(expiredAt);
    }

    [Test]
    public async Task RequestCancellation_BeforeIntent_SettlesCancelled()
    {
        KeycloakOperation operation = CreateOperation();

        operation.RequestCancellation(Active);

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Cancelled);
        await Assert.That(operation.IsCancellationRequested).IsTrue();
        await Assert.That(operation.SettledAtUtc).IsEqualTo(Active);
    }

    [Test]
    public async Task RequestCancellation_DuringOutstandingIntent_RemainsApplying()
    {
        KeycloakOperation operation = ApplyingOperation();

        operation.RequestCancellation(Active);

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(operation.IsCancellationRequested).IsTrue();
        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task Expire_DuringOutstandingIntent_PreservesUncertainty()
    {
        KeycloakOperation operation = ApplyingOperation();

        operation.Expire(Created.AddHours(2));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Applying);
        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task ChangeSet_SnapshotsCallerCollection()
    {
        var callerSteps = new List<KeycloakChangeStep>
        {
            Step(KeycloakStep.CreateClient)
        };
        var changeSet = new KeycloakChangeSet(callerSteps);

        callerSteps.Add(Step(
            KeycloakStep.CreateMapper,
            stepId: "step-2"));

        await Assert.That(changeSet.Steps.Select(step => step.Kind))
            .IsEquivalentTo([KeycloakStep.CreateClient]);
        var published = (IList<KeycloakChangeStep>)changeSet.Steps;
        await Assert.That(published.IsReadOnly).IsTrue();
        Assert.Throws<NotSupportedException>(() =>
            published[0] = Step(KeycloakStep.CreateMapper));
    }

    [Test]
    public async Task ChangeSet_ExposesOnlyApprovedOperationKinds()
    {
        await Assert.That(Enum.GetValues<KeycloakStep>())
            .IsEquivalentTo(
            [
                KeycloakStep.CreateRealm,
                KeycloakStep.CreateClient,
                KeycloakStep.CreateMapper,
                KeycloakStep.UpdateMapper
            ]);
    }

    [Test]
    public async Task ChangeSet_RejectsDuplicateMutationSteps()
    {
        Assert.Throws<ArgumentException>(() =>
            new KeycloakChangeSet(
            [
                Step(KeycloakStep.UpdateMapper, stepId: "duplicate"),
                Step(KeycloakStep.UpdateMapper, stepId: "duplicate")
            ]));

        await Task.CompletedTask;
    }

    [Test]
    public async Task AuthorizeApply_BeforeCreation_FailsClosed()
    {
        KeycloakOperation operation = CreateOperation();

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(),
                "digest",
                Created.AddTicks(-1)));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Previewed);
    }

    [Test]
    public async Task OutcomeUnknown_CannotAuthorizeASecondSend()
    {
        KeycloakOperation operation = ApplyingOperation();
        RecordUnknownOutcome(operation);

        Assert.Throws<InvalidOperationException>(() =>
            operation.AuthorizeApply(
                "actor",
                7,
                Target(),
                "digest",
                Active));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task RecordStepOutcome_SnapshotsApprovedRecoveryEvidence()
    {
        KeycloakOperation operation = ApplyingOperation();
        var outcome = new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.Applied,
            providerResourceId: "mapper-42",
            observedFingerprint: "observed-fingerprint");

        operation.RecordStepOutcome(outcome);

        await Assert.That(operation.StepOutcomes.Items)
            .IsEquivalentTo([outcome]);
        var published =
            (IList<KeycloakStepOutcome>)operation.StepOutcomes.Items;
        await Assert.That(published.IsReadOnly).IsTrue();
        Assert.Throws<NotSupportedException>(() =>
            published[0] = new KeycloakStepOutcome(
                "step-1",
                KeycloakStepOutcomeKind.Conflict));
    }

    [Test]
    public async Task RecordStepOutcome_RejectsUnknownOrDuplicateStep()
    {
        KeycloakOperation operation = ApplyingOperation();
        var outcome = new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.Applied);
        operation.RecordStepOutcome(outcome);

        Assert.Throws<InvalidOperationException>(() =>
            operation.RecordStepOutcome(outcome));
        Assert.Throws<InvalidOperationException>(() =>
            operation.RecordStepOutcome(new KeycloakStepOutcome(
                "unapproved-step",
                KeycloakStepOutcomeKind.Applied)));

        await Task.CompletedTask;
    }

    [Test]
    public async Task Reconcile_OutcomeUnknown_UsesReadBackToSettle()
    {
        KeycloakOperation operation = ApplyingOperation();
        RecordUnknownOutcome(operation);

        operation.ReconcileStepOutcome(new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.Verified,
            providerResourceId: "mapper-42",
            observedFingerprint: "verified-fingerprint"));
        operation.Reconcile(KeycloakOperationState.Verified, Settled);

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.Verified);
        await Assert.That(operation.SettledAtUtc).IsEqualTo(Settled);
        await Assert.That(operation.StepOutcomes.Items.Single().Kind)
            .IsEqualTo(KeycloakStepOutcomeKind.Verified);
    }

    [Test]
    public async Task Reconcile_UndefinedState_PreservesOutcomeUnknown()
    {
        KeycloakOperation operation = ApplyingOperation();
        RecordUnknownOutcome(operation);

        Assert.Throws<InvalidOperationException>(() =>
            operation.Reconcile(
                (KeycloakOperationState)999,
                Settled));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task Reconcile_WithUncertainStepEvidence_CannotSettleAggregate()
    {
        KeycloakOperation operation = ApplyingOperation();
        RecordUnknownOutcome(operation);

        Assert.Throws<InvalidOperationException>(() =>
            operation.Reconcile(
                KeycloakOperationState.Verified,
                Settled));

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.OutcomeUnknown);
        await Assert.That(operation.StepOutcomes.Items.Single().Kind)
            .IsEqualTo(KeycloakStepOutcomeKind.OutcomeUnknown);
    }

    [Test]
    public async Task TerminalTransition_RequiresExplicitSettlementTime()
    {
        KeycloakOperation operation = CreateOperation(
        [
            KeycloakStep.CreateClient,
            KeycloakStep.CreateMapper
        ]);
        operation.AuthorizeApply(
            "actor",
            7,
            Target(),
            "digest",
            Active);
        operation.RecordStepOutcome(new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.Applied));
        operation.RecordStepOutcome(new KeycloakStepOutcome(
            "step-2",
            KeycloakStepOutcomeKind.Conflict));

        operation.MarkPartiallyApplied(Settled);

        await Assert.That(operation.State)
            .IsEqualTo(KeycloakOperationState.PartiallyApplied);
        await Assert.That(operation.SettledAtUtc).IsEqualTo(Settled);
    }

    [Test]
    public async Task TerminalTransition_BeforeCreation_IsRejected()
    {
        KeycloakOperation operation = ApplyingOperation();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            operation.MarkVerified(Created.AddTicks(-1)));

        await Assert.That(operation.SettledAtUtc).IsNull();
    }

    [Test]
    public async Task Constructor_RejectsMissingInstanceAndSetupGeneration()
    {
        Assert.Throws<ArgumentException>(() =>
            new KeycloakTarget(
                Guid.Empty,
                "https://identity.example.test",
                "operators",
                "event-bff"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KeycloakOperation(
                new KeycloakChangeSet([Step(KeycloakStep.CreateClient)]),
                Target(),
                "actor",
                0,
                "digest",
                Created,
                Created.AddHours(1)));

        await Task.CompletedTask;
    }

    private static KeycloakOperation ApplyingOperation()
    {
        KeycloakOperation operation = CreateOperation();
        operation.AuthorizeApply(
            "actor",
            7,
            Target(),
            "digest",
            Active);
        return operation;
    }

    private static void RecordUnknownOutcome(KeycloakOperation operation)
    {
        operation.RecordStepOutcome(new KeycloakStepOutcome(
            "step-1",
            KeycloakStepOutcomeKind.OutcomeUnknown));
        operation.MarkOutcomeUnknown();
    }

    private static KeycloakOperation CreateOperation(
        IEnumerable<KeycloakStep>? steps = null) =>
        new(
            new KeycloakChangeSet(
                (steps ?? [KeycloakStep.CreateClient])
                .Select((step, index) => Step(
                    step,
                    $"step-{index + 1}"))),
            Target(),
            "actor",
            7,
            "digest",
            Created,
            Created.AddHours(1));

    private static KeycloakTarget Target(
        string authority = "https://identity.example.test") =>
        new(
            InstanceId,
            authority,
            "operators",
            "event-bff");

    private static KeycloakChangeStep Step(
        KeycloakStep kind,
        string stepId = "step-1") =>
        new(
            stepId,
            kind,
            kind == KeycloakStep.CreateRealm
                ? KeycloakResourceKind.Realm
                : kind is KeycloakStep.CreateMapper or KeycloakStep.UpdateMapper
                    ? KeycloakResourceKind.ProtocolMapper
                    : KeycloakResourceKind.Client,
            kind == KeycloakStep.CreateRealm
                ? "operators"
                : kind is KeycloakStep.CreateMapper or KeycloakStep.UpdateMapper
                    ? "event-bff:audience"
                    : "event-bff",
            kind is KeycloakStep.CreateRealm
                or KeycloakStep.CreateClient
                or KeycloakStep.CreateMapper
                    ? KeycloakStepPrecondition.MustBeAbsent
                    : KeycloakStepPrecondition.MustMatchFingerprint,
            kind == KeycloakStep.UpdateMapper
                ? "expected-fingerprint"
                : null,
            kind == KeycloakStep.UpdateMapper
                ? "expected-identity-fingerprint"
                : null,
            "desired-fingerprint",
            "binding-fingerprint");
}
