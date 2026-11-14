using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalVisitorRecoveryScopeTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PersistedRecoveryTargetSelectsFreshWritersWithoutBindingTheUnselectedParent(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var parent = request.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var originalId = parent.ContextId;
        var recovery = request.ServiceProvider.GetRequiredService<ILocalVisitorEnrollmentRecoveryScopeFactory>();
        Guid tenantId = Guid.CreateVersion7();
        await using (var selected = await recovery.CreateAsync(tenantId, fixture.CancellationToken))
        {
            var lease = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
            var application = selected.Services.GetRequiredService<ExploreDbContext>();
            await Assert.That(ReferenceEquals(application, parent)).IsFalse();
            await Assert.That(application.TenantFilterTenantId).IsEqualTo(tenantId);
            await Assert.That(application.IsTenantFilterBypassed).IsFalse();
            await Assert.That(application.CurrentUserService).IsNull();
            await Assert.That(ReferenceEquals(application, lease.Identity))
                .IsEqualTo(topology == IdentityDatabaseTopology.Colocated);
            lease.RequireCurrent(application, lease.Identity!);
            selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().RequireOriginalOwner(application);
            var store = selected.Services.GetRequiredService<ILocalIdentityExecutionScopeFactory>();
            await using var nested = await store.CreateAsync(fixture.CancellationToken);
            await Assert.That(nested.Services).IsSameReferenceAs(selected.Services);
            var retainedRecovery = selected.Services
                .GetRequiredService<ILocalVisitorEnrollmentRecoveryScopeFactory>();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                retainedRecovery.CreateAsync(Guid.CreateVersion7(), fixture.CancellationToken).AsTask());
            lease.RequireCurrent(application, lease.Identity!);
        }
        await Assert.That(parent.ContextId).IsEqualTo(originalId);
        await Assert.That(parent.TenantFilterTenantId).IsNull();
        await Assert.That(parent.Database.GetDbConnection().State).IsEqualTo(System.Data.ConnectionState.Closed);
        await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();

        Guid secondTenant = Guid.CreateVersion7();
        await using var next = await recovery.CreateAsync(secondTenant, fixture.CancellationToken);
        await Assert.That(next.Services.GetRequiredService<ExploreDbContext>().TenantFilterTenantId)
            .IsEqualTo(secondTenant);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task EmptyRecoveryTargetNeverSelectsAWriter(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        var factory = request.ServiceProvider.GetRequiredService<ILocalVisitorEnrollmentRecoveryScopeFactory>();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            factory.CreateAsync(Guid.Empty, fixture.CancellationToken).AsTask());
        await Assert.That(request.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>().Application).IsNull();
    }
}
