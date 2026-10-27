namespace Event.Domain.UnitTests.Moderation;

public class ModerationAuthoritySourceInvariantTests
{
    [Test]
    public async Task InstallationCoordinatesAndSelectedPrincipalArePreserved()
    {
        var ids = Ids();
        var binding = Create(ids);

        await Assert.That(binding.Id).IsEqualTo(ids[0]);
        await Assert.That(binding.TrustDomainId).IsEqualTo(ids[1]);
        await Assert.That(binding.SolutionId).IsEqualTo(ids[2]);
        await Assert.That(binding.InstanceId).IsEqualTo(ids[3]);
        await Assert.That(binding.MaintenanceServicePrincipalId).IsEqualTo(ids[4]);
        binding.RequireMatchingCoordinates(ids[1], ids[2], ids[3], ids[4]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task EmptyInstallationIdentifiersAreRejected(int coordinate)
    {
        var ids = Ids();
        ids[coordinate] = Guid.Empty;
        await Assert.That(() => Create(ids)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task NonVersionSevenInstallationIdentifiersAreRejected(int coordinate)
    {
        var ids = Ids();
        ids[coordinate] = Guid.Parse("12345678-1234-4234-8234-123456789abc");
        await Assert.That(() => Create(ids)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task ConflictingCoordinatesCannotReparentHistoricalBinding(int coordinate)
    {
        var ids = Ids();
        var binding = Create(ids);
        var conflicting = ids.ToArray();
        conflicting[coordinate] = Guid.CreateVersion7();
        await Assert.That(() => binding.RequireMatchingCoordinates(
            conflicting[1], conflicting[2], conflicting[3], conflicting[4])).Throws<InvalidOperationException>();
        binding.RequireMatchingCoordinates(ids[1], ids[2], ids[3], ids[4]);
        await Assert.That(binding.Id).IsEqualTo(ids[0]);
    }

    [Test]
    public async Task ExplicitInitializationUsesActualTenantAndStartsAtOne()
    {
        var binding = Create(Ids());
        var tenant = ActualTenant();
        var tenantId = tenant.Id;
        var id = Guid.CreateVersion7();
        var epoch = ModerationQualificationEpoch.Initialize(id, binding, tenant);
        tenant.Id = Guid.CreateVersion7();

        await Assert.That(epoch.Id).IsEqualTo(id);
        await Assert.That(epoch.HostBindingId).IsEqualTo(binding.Id);
        await Assert.That(epoch.TenantId).IsEqualTo(tenantId);
        await Assert.That(epoch.Revision).IsEqualTo(1L);
        epoch.Advance(1);
        await Assert.That(epoch.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task EmptyTenantCannotInitializeAnEpoch()
    {
        var tenant = ActualTenant();
        tenant.Id = Guid.Empty;
        await Assert.That(() => ModerationQualificationEpoch.Initialize(
            Guid.CreateVersion7(), Create(Ids()), tenant)).Throws<ArgumentException>();
    }

    [Test]
    public async Task InvalidEpochIdentityCannotInitialize()
    {
        await Assert.That(() => ModerationQualificationEpoch.Initialize(
            Guid.Empty, Create(Ids()), ActualTenant())).Throws<ArgumentException>();
        await Assert.That(() => ModerationQualificationEpoch.Initialize(
            Guid.Parse("12345678-1234-4234-8234-123456789abc"), Create(Ids()), ActualTenant()))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(-1L)]
    [Arguments(2L)]
    public async Task NonmatchingExpectedRevisionLeavesEpochUntouched(long expected)
    {
        var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Create(Ids()), ActualTenant());
        await Assert.That(() => epoch.Advance(expected)).Throws<InvalidOperationException>();
        await Assert.That(epoch.Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task ReplayedTransitionCannotAdvanceAgain()
    {
        var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Create(Ids()), ActualTenant());
        epoch.Advance(1);
        await Assert.That(() => epoch.Advance(1)).Throws<InvalidOperationException>();
        await Assert.That(epoch.Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task OverflowLeavesHydratedEpochIdentityAndRevisionUntouched()
    {
        var epoch = ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), Create(Ids()), ActualTenant());
        typeof(ModerationQualificationEpoch).GetProperty(nameof(ModerationQualificationEpoch.Revision))!
            .SetValue(epoch, long.MaxValue);
        var id = epoch.Id;
        var binding = epoch.HostBindingId;
        var tenant = epoch.TenantId;

        await Assert.That(() => epoch.Advance(long.MaxValue)).Throws<OverflowException>();
        await Assert.That(epoch.Revision).IsEqualTo(long.MaxValue);
        await Assert.That(epoch.Id).IsEqualTo(id);
        await Assert.That(epoch.HostBindingId).IsEqualTo(binding);
        await Assert.That(epoch.TenantId).IsEqualTo(tenant);
    }

    private static Guid[] Ids() =>
        [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];

    private static ModerationHostBinding Create(Guid[] ids) =>
        ModerationHostBinding.Establish(ids[0], ids[1], ids[2], ids[3], ids[4]);

    private static Tenant ActualTenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Actual jurisdiction",
        Slug = "actual-jurisdiction",
        TenantStatus = new TenantStatus { Id = 1, MasterCode = "Active", FullName = "Active" }
    };
}
