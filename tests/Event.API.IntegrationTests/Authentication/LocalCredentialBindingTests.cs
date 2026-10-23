using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Responses;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
[Timeout(180_000)]
public sealed partial class LocalCredentialBindingTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReconciliationRespectsOwnerTopologyAndPreservesExactCredentials(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        if (topology == IdentityDatabaseTopology.External)
        {
            await AssertUnsupportedOwnerHasNoEffectsAsync(fixture);
            return;
        }

        BaseCommandResponse<Guid> first = await fixture.ReconcileAsync();

        await Assert.That(first.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser activated = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(activated.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(activated.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        BaseCommandResponse<Guid> replay = await fixture.ReconcileAsync();
        await Assert.That(replay.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, activated.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, activated.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task RejectedOwnerOrGraphFailureDoesNotAdvanceIdentity(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        fixture.Faults.RejectGraph = true;
        if (topology == IdentityDatabaseTopology.External)
        {
            await AssertUnsupportedOwnerHasNoEffectsAsync(fixture);
            return;
        }

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ReconcileAsync());

        await Assert.That(fixture.Faults.GraphFaultObserved).IsTrue();
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActivationRecoveryRequiresTheSupportedOriginalOwner(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        fixture.Faults.RejectActivationCommit = true;
        if (topology == IdentityDatabaseTopology.External)
        {
            await AssertUnsupportedOwnerHasNoEffectsAsync(fixture);
            return;
        }

        // Once physical commit starts, even an interceptor rejection is an unknown
        // outcome to the caller. Recovery must observe durable state in a new request.
        if (topology == IdentityDatabaseTopology.Colocated)
        {
            var failure = await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => fixture.ReconcileAsync());
            await Assert.That(failure?.InnerException is InjectedBindingFailure).IsTrue();
        }
        else
        {
            // Preserve the unresolved External control's original failure expectation.
            await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ReconcileAsync());
        }

        await Assert.That(fixture.Faults.ActivationCommitFaultObserved).IsTrue();
        await Assert.That(fixture.Faults.CommitsObserved)
            .IsEqualTo(topology == IdentityDatabaseTopology.Colocated ? 3 : 2);
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser failed = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(failed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(failed.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(failed.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        BaseCommandResponse<Guid> retry = await fixture.ReconcileAsync();
        await Assert.That(retry.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That((await fixture.ReconcileAsync()).IsSuccess).IsTrue();
        LocalIdentityUser replayed = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(replayed.SecurityStamp, after.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(replayed.ConcurrencyStamp, after.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(replayed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task LostActivationAcknowledgementRequiresFreshAuthenticationWithoutSecondStampRotation()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        var command = new ReconcileLocalCredentialOperationCommand(fixture.Receipt.OperationId);
        await using (var request = new Request(fixture.Lifecycle.Host.Services))
        {
            await request.AuthenticateLocalAsync(await fixture.Lifecycle.SignInAsync(), fixture.InitiatorId);
            var preparation = request.Services.GetRequiredService<ILocalAdministrationPreparation>();
            var administration = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
            var original = await preparation.PrepareAsync(command, fixture.InitiatorId, CancellationToken);
            var snapshot = (await administration.ReadProvisioningAsync(original, command, CancellationToken))!;
            await Assert.That((await administration.BindAsync(original, command, snapshot, CancellationToken)).IsSuccess).IsTrue();
            fixture.Faults.Observe(request.Services.GetRequiredService<ExploreDbContext>(), fixture.Receipt.PersonalActorId);
            fixture.Faults.LoseNextAcknowledgement = true;

            var failure = await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() =>
                administration.ActivateAsync(original, command, snapshot, CancellationToken));
            await Assert.That(failure?.InnerException is InjectedBindingFailure).IsTrue();
            await Assert.That(fixture.Faults.LostAcknowledgementObserved).IsTrue();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                administration.ActivateAsync(original, command, snapshot, CancellationToken));
        }
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        await fixture.AssertExactGraphAsync();
        LocalIdentityUser committed = await fixture.ReadIdentityUserAsync();
        var committedOperation = await fixture.ReadOperationAsync();
        await Assert.That(committed.SecurityStamp == before.SecurityStamp).IsFalse();
        await Assert.That(committed.ConcurrencyStamp == before.ConcurrencyStamp).IsFalse();
        await Assert.That(committed.PasswordHash).IsEqualTo(before.PasswordHash);

        await Assert.That((await fixture.ReconcileAsync()).IsSuccess).IsTrue();

        LocalIdentityUser recovered = await fixture.ReadIdentityUserAsync();
        await Assert.That(recovered.SecurityStamp).IsEqualTo(committed.SecurityStamp);
        await Assert.That(recovered.ConcurrencyStamp).IsEqualTo(committed.ConcurrencyStamp);
        await Assert.That(recovered.PasswordHash).IsEqualTo(before.PasswordHash);
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp).IsEqualTo(committedOperation.ConcurrencyStamp);
        await fixture.AssertExactGraphAsync();
    }
    private static async Task AssertUnsupportedOwnerHasNoEffectsAsync(Fixture fixture)
    {
        var user = await fixture.ReadIdentityUserAsync();
        var operation = await fixture.ReadOperationAsync();
        await Assert.ThrowsAsync<NotSupportedException>(() => fixture.ReconcileAsync());
        await Assert.That(string.Equals(JsonSerializer.Serialize(await fixture.ReadIdentityUserAsync()),
            JsonSerializer.Serialize(user), StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(JsonSerializer.Serialize(await fixture.ReadOperationAsync()),
            JsonSerializer.Serialize(operation), StringComparison.Ordinal)).IsTrue();
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }
}
