using Cutover = Explore.Domain.Secrets.AuthenticationCredentialCutover;

namespace Event.Domain.UnitTests.Secrets;

public sealed class AuthenticationCredentialCutoverTests
{
    private static readonly DateTimeOffset Created = new(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Instance = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid Operation = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
    private static readonly Guid Actor = Guid.Parse("018e4e5c-7f00-7000-8000-000000000003");
    private static readonly Guid Deployment = Guid.Parse("018e4e5c-7f00-7000-8000-000000000004");
    private static readonly Guid ProfileRevision = Guid.Parse("018e4e5c-7f00-7000-8000-000000000005");

    [Test]
    public async Task ExactActivationsAndOperatorFencePermitAtomicCleanup()
    {
        var operation = Create();
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Prepared);
        operation.StartCollecting(operation.ConcurrencyStamp, Created);
        ActivateAll(operation);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Collecting);
        await Assert.That(() => operation.EnsureCleanupAllowed(Operation, operation.ConcurrencyStamp, Created))
            .Throws<InvalidOperationException>();

        operation.AttestFence(Fence(), operation.ConcurrencyStamp, Created.AddSeconds(1));
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
        var stamp = operation.ConcurrencyStamp;
        operation.EnsureCleanupAllowed(Operation, stamp, Created.AddSeconds(2));
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        operation.CompleteCleanup(Operation, stamp, 3, 2, Created.AddSeconds(2));

        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Cleaned);
        await Assert.That(operation.ActivationCount).IsEqualTo(2);
        await Assert.That(operation.CleanedSettingCount).IsEqualTo(3);
        await Assert.That(operation.CleanedBindingCount).IsEqualTo(2);
        await Assert.That(operation.InstanceId).IsEqualTo(Instance);
        await Assert.That(operation.Id).IsEqualTo(Operation);
        await Assert.That(operation.CreatedBy).IsEqualTo(Actor);
        await Assert.That(operation.DeploymentRevision).IsEqualTo(Deployment);
        await Assert.That(operation.Authority).IsEqualTo(Cutover.CredentialAuthority.Infisical);
        await Assert.That(operation.AuthorityProfileRevision).IsEqualTo(ProfileRevision);
    }

    [Test]
    public async Task RequiredMembershipIsOwnedAndCannotBeEmptiedByCaller()
    {
        var members = Members().ToList();
        var operation = Create(members);
        members.Clear();
        await Assert.That(operation.RequiredReplacements.Count).IsEqualTo(2);
        await Assert.That(() => ((IList<Cutover.Replacement>)operation.RequiredReplacements).Clear())
            .Throws<NotSupportedException>();
        operation.StartCollecting(operation.ConcurrencyStamp, Created);
        operation.AttestFence(Fence(), operation.ConcurrencyStamp, Created);
        operation.ReportActivation(Report(operation.RequiredReplacements[0]), operation.ConcurrencyStamp, Created);
        await Assert.That(operation.ActivationCount).IsEqualTo(1);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Collecting);
        operation.ReportActivation(Report(operation.RequiredReplacements[1]), operation.ConcurrencyStamp, Created);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
    }

    [Test]
    public async Task WrongTargetDeploymentMemberConsumerBootOrGenerationNeverCounts()
    {
        var operation = Collecting();
        var exact = Report(operation.RequiredReplacements[0]);
        Cutover.ActivationReport[] wrong =
        [
            exact with { InstanceId = Guid.Empty },
            exact with { OperationId = Guid.Empty },
            exact with { DeploymentRevision = Guid.Empty },
            exact with { ReplicaId = Guid.Empty },
            exact with { Consumer = Cutover.CredentialConsumer.KeycloakOidc },
            exact with { Consumer = (Cutover.CredentialConsumer)99 },
            exact with { ProcessBootId = operation.RequiredReplacements[0].PreviousProcessBootId },
            exact with { Generation = Guid.Empty }
        ];
        var stamp = operation.ConcurrencyStamp;
        foreach (var report in wrong)
            await Assert.That(() => operation.ReportActivation(report, stamp, Created))
                .Throws<InvalidOperationException>();
        await Assert.That(operation.ActivationCount).IsEqualTo(0);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Collecting);
    }

    [Test]
    public async Task IdenticalEvidenceIsIdempotentButConflictingDuplicateCannotReplaceIt()
    {
        var operation = Collecting();
        var exact = Report(operation.RequiredReplacements[0]);
        operation.ReportActivation(exact, operation.ConcurrencyStamp, Created);
        var stamp = operation.ConcurrencyStamp;
        operation.ReportActivation(exact, stamp, Created.AddSeconds(1));
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(() => operation.ReportActivation(exact with { Generation = Guid.Empty }, stamp, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.ActivationCount).IsEqualTo(1);
        await Assert.That(operation.Activations[0]).IsEqualTo(exact);
        await Assert.That(() => ((IList<Cutover.ActivationReport>)operation.Activations).Clear())
            .Throws<NotSupportedException>();
        ActivateAll(operation);
        operation.AttestFence(Fence(), operation.ConcurrencyStamp, Created);
        stamp = operation.ConcurrencyStamp;
        operation.ReportActivation(exact, stamp, Created);
        operation.AttestFence(Fence(), stamp, Created);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
    }

    [Test]
    public async Task FenceRequiresExactOperatorTargetAndEveryExplicitProof()
    {
        var operation = Collecting();
        ActivateAll(operation);
        var exact = Fence();
        Cutover.FenceAttestation[] wrong =
        [
            exact with { InstanceId = Guid.Empty },
            exact with { OperationId = Guid.Empty },
            exact with { DeploymentRevision = Guid.Empty },
            exact with { AttestedBy = Guid.Empty },
            exact with { OldProcessesTerminated = false },
            exact with { OldWritersFenced = false },
            exact with { OldSessionsFenced = false }
        ];
        var stamp = operation.ConcurrencyStamp;
        foreach (var proof in wrong)
            await Assert.That(() => operation.AttestFence(proof, stamp, Created))
                .Throws<InvalidOperationException>();
        await Assert.That(operation.Fence).IsNull();
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Collecting);
        operation.AttestFence(exact, stamp, Created);
        stamp = operation.ConcurrencyStamp;
        await Assert.That(() => operation.AttestFence(exact with { OldSessionsFenced = false }, stamp, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.Fence).IsEqualTo(exact);
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
    }

    [Test]
    public async Task CleanupRechecksCurrentOperationStampAndExactDeadline()
    {
        var operation = Ready();
        var stamp = operation.ConcurrencyStamp;
        await Assert.That(() => operation.CompleteCleanup(Guid.Empty, stamp, 1, 1, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.CompleteCleanup(Operation, Guid.Empty, 1, 1, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.CompleteCleanup(Operation, stamp, -1, 1, Created))
            .Throws<ArgumentException>();
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
        await Assert.That(operation.CleanedSettingCount).IsNull();
        await Assert.That(() => operation.CompleteCleanup(Operation, stamp, 1, 1, operation.Deadline))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Expired);
        await Assert.That(operation.ActivationCount).IsEqualTo(2);
        await Assert.That(operation.CleanedBindingCount).IsNull();
    }

    [Test]
    public async Task ExpiredAndLateCallbacksCannotSupplyMissingEvidence()
    {
        var operation = Collecting();
        var report = Report(operation.RequiredReplacements[0]);
        operation.ReportActivation(report, operation.ConcurrencyStamp, Created.AddSeconds(2));
        var stamp = operation.ConcurrencyStamp;
        await Assert.That(() => operation.ReportActivation(Report(operation.RequiredReplacements[1]), stamp,
            Created.AddSeconds(1))).Throws<InvalidOperationException>();
        await Assert.That(operation.ActivationCount).IsEqualTo(1);
        await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(() => operation.ReportActivation(report, stamp, operation.Deadline))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Expired);
        await Assert.That(operation.ActivationCount).IsEqualTo(1);
        await Assert.That(() => operation.AttestFence(Fence(), operation.ConcurrencyStamp, operation.Deadline))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PreparedCallbacksAndStaleMutationsAreRejected()
    {
        var operation = Create();
        var stamp = operation.ConcurrencyStamp;
        await Assert.That(() => operation.ReportActivation(Report(operation.RequiredReplacements[0]), stamp, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.AttestFence(Fence(), stamp, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(() => operation.StartCollecting(stamp, Created.AddTicks(-1)))
            .Throws<InvalidOperationException>();
        operation.StartCollecting(stamp, Created);
        await Assert.That(() => operation.ReportActivation(Report(operation.RequiredReplacements[0]), stamp, Created))
            .Throws<InvalidOperationException>();
        await Assert.That(operation.ActivationCount).IsEqualTo(0);
    }

    [Test]
    public async Task NoTerminalStateCanBeReopenedOrReplacedByExpiry()
    {
        foreach (var state in new[] { Cutover.CutoverState.Cleaned, Cutover.CutoverState.Failed,
                     Cutover.CutoverState.Expired, Cutover.CutoverState.Superseded })
        {
            var operation = Ready();
            switch (state)
            {
                case Cutover.CutoverState.Cleaned:
                    operation.CompleteCleanup(Operation, operation.ConcurrencyStamp, 0, 0, Created);
                    break;
                case Cutover.CutoverState.Failed:
                    operation.Fail(operation.ConcurrencyStamp, Created);
                    break;
                case Cutover.CutoverState.Expired:
                    await Assert.That(() => operation.EnsureCleanupAllowed(Operation,
                        operation.ConcurrencyStamp, operation.Deadline)).Throws<InvalidOperationException>();
                    break;
                case Cutover.CutoverState.Superseded:
                    operation.Supersede(operation.ConcurrencyStamp, Created);
                    break;
            }
            var stamp = operation.ConcurrencyStamp;
            var late = operation.Deadline.AddSeconds(1);
            await Assert.That(() => operation.ReportActivation(Report(operation.RequiredReplacements[0]), stamp, late))
                .Throws<InvalidOperationException>();
            await Assert.That(() => operation.AttestFence(Fence(), stamp, late)).Throws<InvalidOperationException>();
            await Assert.That(() => operation.StartCollecting(stamp, late)).Throws<InvalidOperationException>();
            await Assert.That(() => operation.CompleteCleanup(Operation, stamp, 0, 0, late))
                .Throws<InvalidOperationException>();
            await Assert.That(() => operation.Fail(stamp, late)).Throws<InvalidOperationException>();
            await Assert.That(() => operation.Supersede(stamp, late)).Throws<InvalidOperationException>();
            await Assert.That(operation.State).IsEqualTo(state);
            await Assert.That(operation.ConcurrencyStamp).IsEqualTo(stamp);
            await Assert.That(operation.ActivationCount).IsEqualTo(2);
        }
    }

    [Test]
    public async Task PreparationRejectsEmptyDuplicateAndReusedProcessIncarnations()
    {
        var member = Members()[0];
        await Assert.That(() => Create([])).Throws<ArgumentException>();
        await Assert.That(() => Create([member, member])).Throws<ArgumentException>();
        await Assert.That(() => Create([member with { ProcessBootId = member.PreviousProcessBootId }]))
            .Throws<ArgumentException>();
        await Assert.That(() => Create([member with { Generation = Guid.Empty }])).Throws<ArgumentException>();
        await Assert.That(() => Create([member with { Consumer = (Cutover.CredentialConsumer)99 }]))
            .Throws<ArgumentException>();
        await Assert.That(() => Create([member, member with
        {
            Consumer = Cutover.CredentialConsumer.KeycloakOidc,
            ProcessBootId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000099")
        }])).Throws<ArgumentException>();
        var second = Members()[1];
        await Assert.That(() => Create([member, second with { ProcessBootId = member.PreviousProcessBootId }]))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task FenceAloneNeverAuthorizesCleanupAndLastMomentEvidenceStillNeedsGate()
    {
        var operation = Collecting();
        operation.AttestFence(Fence(), operation.ConcurrencyStamp, Created);
        await Assert.That(() => operation.EnsureCleanupAllowed(Operation, operation.ConcurrencyStamp, Created))
            .Throws<InvalidOperationException>();
        ActivateAll(operation, operation.Deadline.AddTicks(-1));
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.ReadyForCleanup);
        await Assert.That(() => operation.EnsureCleanupAllowed(Operation, operation.ConcurrencyStamp,
            operation.Deadline)).Throws<InvalidOperationException>();
        await Assert.That(operation.State).IsEqualTo(Cutover.CutoverState.Expired);
    }

    [Test]
    public async Task PreparationRequiresFixedIdentitiesKnownAuthorityAndFutureDeadline()
    {
        await Assert.That(() => Cutover.Create(Guid.Empty, Operation, Actor, Created, Created.AddMinutes(5),
            Deployment, Cutover.CredentialAuthority.Infisical, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Guid.Empty, Actor, Created, Created.AddMinutes(5),
            Deployment, Cutover.CredentialAuthority.Infisical, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Operation, Guid.Empty, Created, Created.AddMinutes(5),
            Deployment, Cutover.CredentialAuthority.Infisical, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Operation, Actor, Created, Created,
            Deployment, Cutover.CredentialAuthority.Infisical, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Operation, Actor, Created, Created.AddMinutes(5),
            Guid.Empty, Cutover.CredentialAuthority.Infisical, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Operation, Actor, Created, Created.AddMinutes(5),
            Deployment, (Cutover.CredentialAuthority)99, ProfileRevision, Members())).Throws<ArgumentException>();
        await Assert.That(() => Cutover.Create(Instance, Operation, Actor, Created, Created.AddMinutes(5),
            Deployment, Cutover.CredentialAuthority.Infisical, Guid.Empty, Members())).Throws<ArgumentException>();
        await Assert.That(() => Create(Enumerable.Repeat(Members()[0], 1025))).Throws<ArgumentException>();
    }

    [Test]
    public async Task ExpiryAtStartOrFenceDoesNotAcceptEvidence()
    {
        var prepared = Create();
        await Assert.That(() => prepared.StartCollecting(prepared.ConcurrencyStamp, prepared.Deadline))
            .Throws<InvalidOperationException>();
        await Assert.That(prepared.State).IsEqualTo(Cutover.CutoverState.Expired);
        var collecting = Collecting();
        ActivateAll(collecting);
        await Assert.That(() => collecting.AttestFence(Fence(), collecting.ConcurrencyStamp, collecting.Deadline))
            .Throws<InvalidOperationException>();
        await Assert.That(collecting.State).IsEqualTo(Cutover.CutoverState.Expired);
        await Assert.That(collecting.Fence).IsNull();
        await Assert.That(collecting.ActivationCount).IsEqualTo(2);
    }

    private static Cutover Create(IEnumerable<Cutover.Replacement>? members = null) =>
        Cutover.Create(Instance, Operation, Actor, Created, Created.AddMinutes(5), Deployment,
            Cutover.CredentialAuthority.Infisical, ProfileRevision, members ?? Members());

    private static Cutover Collecting()
    {
        var operation = Create();
        operation.StartCollecting(operation.ConcurrencyStamp, Created);
        return operation;
    }

    private static Cutover Ready()
    {
        var operation = Collecting();
        ActivateAll(operation);
        operation.AttestFence(Fence(), operation.ConcurrencyStamp, Created);
        return operation;
    }

    private static void ActivateAll(Cutover operation, DateTimeOffset? now = null)
    {
        foreach (var member in operation.RequiredReplacements)
            operation.ReportActivation(Report(member), operation.ConcurrencyStamp, now ?? Created);
    }

    private static Cutover.Replacement[] Members() =>
    [
        new(Guid.Parse("018e4e5c-7f00-7000-8000-000000000010"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000011"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000012"),
            Cutover.CredentialConsumer.GoogleOidc, Guid.Parse("018e4e5c-7f00-7000-8000-000000000013")),
        new(Guid.Parse("018e4e5c-7f00-7000-8000-000000000020"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000021"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000022"),
            Cutover.CredentialConsumer.KeycloakOidc, Guid.Parse("018e4e5c-7f00-7000-8000-000000000023"))
    ];

    private static Cutover.ActivationReport Report(Cutover.Replacement member) =>
        new(Instance, Operation, Deployment, member.ReplicaId, member.ProcessBootId, member.Consumer, member.Generation);

    private static Cutover.FenceAttestation Fence() => new(Instance, Operation, Deployment, Actor, true, true, true);
}
