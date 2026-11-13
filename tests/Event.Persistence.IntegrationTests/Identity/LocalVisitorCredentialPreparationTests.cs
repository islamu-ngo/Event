using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalVisitorCredentialPreparationTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task PreparationAndExactReplayKeepCredentialsPendingAndEmailUnconfirmed(
        IdentityDatabaseTopology topology, bool hasEmail)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        Guid tenant = Guid.CreateVersion7();
        var reservation = await ReserveAsync(fixture, tenant);
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(
            fixture, topology, tenantContext: new SelectedTenant(tenant));
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var lease = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var application = lease.Application ?? throw new InvalidOperationException("The selected application owner is absent.");
        var identity = lease.Identity ?? throw new InvalidOperationException("The selected identity owner is absent.");
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        await using var policy = fixture.Provider.CreateAsyncScope();
        var installed = policy.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        manager.PasswordHasher = installed.PasswordHasher;
        manager.PasswordValidators.Clear();
        foreach (var validator in installed.PasswordValidators) manager.PasswordValidators.Add(validator);
        manager.UserValidators.Clear();
        foreach (var validator in installed.UserValidators) manager.UserValidators.Add(validator);
        var store = new LocalIdentityCredentialStateStore(identity, application, manager, fixture.Clock,
            NativeIdentityRecognitionBoundary.For(application, fixture.Clock), lease);
        string username = $"visitor-{Guid.CreateVersion7():N}";
        string password = Password();
        string? email = hasEmail ? $"visitor-{Guid.CreateVersion7():N}@example.test" : null;

        var prepared = await store.PrepareVisitorAsync(reservation.Id, username, password, email, fixture.CancellationToken);
        await Assert.That(prepared).IsNotNull();
        var snapshot = prepared ?? throw new InvalidOperationException("Visitor preparation was rejected.");
        await Assert.That(snapshot.Receipt.Kind).IsEqualTo(LocalCredentialOperationKind.VisitorCreate);
        await Assert.That(snapshot.Receipt.OperationId).IsEqualTo(reservation.CredentialOperationId);
        await Assert.That(snapshot.Receipt.VisitorEnrollmentOperationId).IsEqualTo(reservation.Id);
        await Assert.That(snapshot.Receipt.InitiatingApplicationUserId).IsNull();
        await Assert.That(snapshot.EmailVerified).IsFalse();
        var user = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == reservation.LocalSubjectId, fixture.CancellationToken);
        string? hash = user.PasswordHash;
        await Assert.That(user.EmailConfirmed).IsFalse();
        var metadata = await identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
            .SingleAsync(row => row.UserId == user.Id
                && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                && row.Name == LocalCredentialStateMetadata.TokenName, fixture.CancellationToken);
        await Assert.That(JsonSerializer.Deserialize<LocalCredentialStateMetadata>(metadata.Value!)!.State)
            .IsEqualTo(LocalCredentialState.RegistrationPending);
        var eligibility = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == user.Id, fixture.CancellationToken);
        await Assert.That(eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);
        await Assert.That(eligibility.CurrentOperationId).IsEqualTo(reservation.CredentialOperationId);
        await Assert.That(await application.Users.AnyAsync(row => row.Id == user.Id, fixture.CancellationToken)).IsFalse();
        await Assert.That(await application.Set<TenantUser>().AnyAsync(row => row.UserId == user.Id, fixture.CancellationToken)).IsFalse();
        var authentication = await fixture.Authentication(selected.Services, store).AuthenticateAsync(
            new LocalAuthRequestDto(username, password), fixture.CancellationToken);
        await Assert.That(authentication.Outcome == LocalAuthOutcome.Authenticated).IsFalse();
        await Assert.That(authentication.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);
        var incorrect = await fixture.Authentication(selected.Services, store).AuthenticateAsync(
            new LocalAuthRequestDto(username, Password()), fixture.CancellationToken);
        await Assert.That(incorrect.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);
        var stillPending = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == user.Id, fixture.CancellationToken);
        await Assert.That(stillPending.Generation).IsEqualTo(eligibility.Generation);
        await Assert.That(stillPending.State).IsEqualTo(LocalIdentityEligibilityState.ProvisioningPending);

        var replay = await store.PrepareVisitorAsync(reservation.Id, username, password, email, fixture.CancellationToken);
        await Assert.That(replay).IsEqualTo(snapshot);
        await Assert.That(await store.PrepareVisitorAsync(reservation.Id, username, Password(), email,
            fixture.CancellationToken)).IsNull();
        var after = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == user.Id, fixture.CancellationToken);
        await Assert.That(after.PasswordHash == hash).IsTrue();
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>()
            .CountAsync(row => row.LocalSubjectId == user.Id, fixture.CancellationToken)).IsEqualTo(1);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task UnselectedOwnerCannotPrepareVisitorCredentials(IdentityDatabaseTopology topology)
    {
        await using var fixture = await Fixture.CreateAsync(topology);
        await using var ordinary = fixture.Provider.CreateAsyncScope();
        var store = fixture.StateStore(ordinary);
        int before = await fixture.Identity(ordinary).Set<LocalIdentityCredentialOperation>()
            .CountAsync(fixture.CancellationToken);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => store.PrepareVisitorAsync(
            Guid.CreateVersion7(), $"visitor-{Guid.CreateVersion7():N}", Password(), null, fixture.CancellationToken));
        await Assert.That(await fixture.Identity(ordinary).Set<LocalIdentityCredentialOperation>()
            .CountAsync(fixture.CancellationToken)).IsEqualTo(before);
    }

    private static async Task<VisitorEnrollmentOperation> ReserveAsync(Fixture fixture, Guid tenant)
    {
        await using var scope = fixture.Provider.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ExploreDbContext>>();
        await using var seed = new ExploreDbContext(options) { TenantContext = new SelectedTenant(tenant) };
        seed.BindTenantLease();
        DateTime at = fixture.Clock.GetUtcNow().UtcDateTime;
        seed.Tenants.Add(new Tenant
        {
            Id = tenant, FullName = "Visitor preparation tenant", Slug = $"visitor-{tenant:N}",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = at
        });
        var row = VisitorEnrollmentOperation.Reserve(Guid.CreateVersion7(), tenant, Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(), at);
        seed.Set<VisitorEnrollmentOperation>().Add(row);
        await seed.SaveChangesAsync(fixture.CancellationToken);
        return row;
    }

    private static string Password() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
    private sealed record SelectedTenant(Guid TenantId) : ITenantContext;
}
