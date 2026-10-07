using System.Security.Claims;
using System.Data.Common;
using Explore.Application.Contracts.Secrets;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Secrets;

public sealed class AuthenticationCredentialCutoverCoordinatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task RejectedEvidenceReleasesLockOnExternallyOwnedConnection()
    {
        await using var database = await TestDatabase.CreateAsync(sharedConnection: true);
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (before, head) = await ReadAsync(database, operation);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.ReportActivationAsync(
                Activation(before) with { Generation = Guid.CreateVersion7() },
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now));
        await AssertUnchangedAsync(database, before, head);
        var accepted = await database.Coordinator.ReportActivationAsync(Activation(before),
            head.ConcurrencyStamp, before.ConcurrencyStamp, Now).WaitAsync(TimeSpan.FromSeconds(15));
        await Assert.That(accepted.ActivationCount).IsEqualTo(1);
    }

    [Test]
    public async Task AnonymousPreparationCannotCommitReceiptOrHead()
    {
        await using var database = await TestDatabase.CreateAsync(authenticated: false);
        var operation = database.CreateOperation();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            database.Coordinator.PrepareAsync(operation, null, null, null, Now));
        await AssertAbsentAsync(database, operation);
    }

    [Test]
    public async Task FirstPreparationPersistsReceiptAndCurrentHeadTogether()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();

        var prepared = await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (receipt, head) = await ReadAsync(database, operation);

        await Assert.That(prepared.Id).IsEqualTo(operation.Id);
        await Assert.That(head.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(receipt.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Prepared);
        await Assert.That(receipt.ConcurrencyStamp).IsEqualTo(operation.ConcurrencyStamp);
        await Assert.That(receipt.RequiredReplacements).IsEquivalentTo(operation.RequiredReplacements);
        await Assert.That(receipt.Activations).IsEmpty();
        await Assert.That(receipt.Fence).IsNull();
    }

    [Test]
    public async Task FailedHeadInsertionRollsBackFirstReceipt()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await using (var context = database.CreateDbContext())
        {
            // Fail after the receipt insert has been flushed, at the second write in preparation.
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER reject_cutover_head BEFORE INSERT ON ie_authentication_credential_cutover_heads
                BEGIN SELECT RAISE(ABORT, 'cutover_head_insert_rejected'); END;
                """);
        }

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.Coordinator.PrepareAsync(operation, null, null, null, Now));

        await AssertAbsentAsync(database, operation);
    }

    [Test]
    public async Task PreparationAtDeadlineRejectsWithoutReceiptOrHead()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.PrepareAsync(operation, null, null, null, operation.Deadline));

        await AssertAbsentAsync(database, operation);
    }

    [Test]
    public async Task ReplacementSupersedesPredecessorAndRetainsItsEvidence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = database.CreateOperation();
        await database.Coordinator.PrepareAsync(original, null, null, null, Now);
        var (before, firstHead) = await ReadAsync(database, original);
        await database.Coordinator.ReportActivationAsync(Activation(before), firstHead.ConcurrencyStamp,
            before.ConcurrencyStamp, Now.AddSeconds(1));
        var (activated, activatedHead) = await ReadAsync(database, original);
        var fence = Fence(activated);
        await database.Coordinator.AttestFenceAsync(fence, activatedHead.ConcurrencyStamp,
            activated.ConcurrencyStamp, Now.AddSeconds(2));
        var (ready, readyHead) = await ReadAsync(database, original);
        var replacement = database.CreateOperation(original.InstanceId);

        await database.Coordinator.PrepareAsync(replacement, original.Id, readyHead.ConcurrencyStamp,
            ready.ConcurrencyStamp, Now.AddSeconds(3));

        var (superseded, head) = await ReadAsync(database, original);
        var (current, _) = await ReadAsync(database, replacement);
        await Assert.That(superseded.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Superseded);
        await Assert.That(superseded.Activations).IsEquivalentTo(ready.Activations);
        await Assert.That(superseded.Fence).IsEqualTo(fence);
        await Assert.That(superseded.ConcurrencyStamp).IsNotEqualTo(ready.ConcurrencyStamp);
        await Assert.That(head.CurrentOperationId).IsEqualTo(replacement.Id);
        await Assert.That(head.ConcurrencyStamp).IsNotEqualTo(readyHead.ConcurrencyStamp);
        await Assert.That(current.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Prepared);
        await Assert.That(current.Activations).IsEmpty();
        await Assert.That(current.Fence).IsNull();
    }

    [Test]
    public async Task StalePredecessorStampRollsBackGuardedHeadAndDoesNotInsertReplacement()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = database.CreateOperation();
        await database.Coordinator.PrepareAsync(original, null, null, null, Now);
        var (before, head) = await ReadAsync(database, original);
        var replacement = database.CreateOperation(original.InstanceId);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.PrepareAsync(replacement, original.Id, head.ConcurrencyStamp,
                Guid.CreateVersion7(), Now.AddSeconds(1)));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_stale_stamp");
        await AssertUnchangedAsync(database, before, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(replacement.InstanceId, replacement.Id)).IsNull();
    }

    [Test]
    public async Task ReusingReceiptIdRollsBackSupersessionAndHeadGuard()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = database.CreateOperation();
        var occupied = database.CreateOperation(Guid.CreateVersion7());
        await database.Coordinator.PrepareAsync(original, null, null, null, Now);
        await database.SeedHistoricalAsync(occupied);
        var (before, head) = await ReadAsync(database, original);
        var (occupiedReceipt, occupiedHead) = await ReadAsync(database, occupied);
        var duplicate = database.CreateOperation(original.InstanceId, occupied.Id);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.Coordinator.PrepareAsync(duplicate, original.Id, head.ConcurrencyStamp,
                before.ConcurrencyStamp, Now.AddSeconds(1)));

        await AssertUnchangedAsync(database, before, head);
        await AssertUnchangedAsync(database, occupiedReceipt, occupiedHead);
    }

    [Test]
    [Arguments("current", "authentication_cutover_head_conflict")]
    [Arguments("head", "authentication_cutover_head_conflict")]
    [Arguments("operation", "authentication_cutover_stale_stamp")]
    [Arguments("profile", "authentication_cutover_profile_conflict")]
    [Arguments("replica", "authentication_cutover_unexpected_activation")]
    [Arguments("boot", "authentication_cutover_unexpected_activation")]
    [Arguments("generation", "authentication_cutover_unexpected_activation")]
    [Arguments("consumer", "authentication_cutover_unexpected_activation")]
    [Arguments("deployment", "authentication_cutover_wrong_target")]
    public async Task RejectedActivationRollsBackHeadAndEvidence(string mismatch, string expectedError)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (before, head) = await ReadAsync(database, operation);
        var report = Activation(before);
        report = mismatch switch
        {
            "current" => report with { OperationId = Guid.CreateVersion7() },
            "replica" => report with { ReplicaId = Guid.CreateVersion7() },
            "boot" => report with { ProcessBootId = before.RequiredReplacements[0].PreviousProcessBootId },
            "generation" => report with { Generation = Guid.CreateVersion7() },
            "consumer" => report with { Consumer = AuthenticationCredentialCutover.CredentialConsumer.KeycloakOidc },
            "deployment" => report with { DeploymentRevision = Guid.CreateVersion7() },
            _ => report
        };
        if (mismatch == "profile")
            database.Profile.Current = database.Profile.Current with { Revision = Guid.CreateVersion7() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.ReportActivationAsync(report,
                mismatch == "head" ? Guid.CreateVersion7() : head.ConcurrencyStamp,
                mismatch == "operation" ? Guid.CreateVersion7() : before.ConcurrencyStamp,
                Now.AddSeconds(1)));

        await Assert.That(exception?.Message).IsEqualTo(expectedError);
        await AssertUnchangedAsync(database, before, head);
    }

    [Test]
    public async Task IncompleteFenceRollsBackCollectingTransitionAndHeadGuard()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (before, head) = await ReadAsync(database, operation);
        var incomplete = Fence(before) with { OldSessionsFenced = false };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.AttestFenceAsync(incomplete, head.ConcurrencyStamp,
                before.ConcurrencyStamp, Now.AddSeconds(1)));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_incomplete_fence");
        await AssertUnchangedAsync(database, before, head);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ReadinessRequiresAllActivationsAndFenceInEitherOrder(bool fenceFirst)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation(memberCount: 2);
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (prepared, firstHead) = await ReadAsync(database, operation);
        var firstReport = Activation(prepared);
        await database.Coordinator.ReportActivationAsync(firstReport, firstHead.ConcurrencyStamp,
            prepared.ConcurrencyStamp, Now.AddSeconds(1));
        var (partial, partialHead) = await ReadAsync(database, operation);
        await Assert.That(partial.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Collecting);
        await Assert.That(partial.Activations).IsEquivalentTo(new[] { firstReport });
        await Assert.That(partial.Fence).IsNull();
        var secondReport = Activation(partial, 1);
        var fence = Fence(partial);

        if (fenceFirst)
            await database.Coordinator.AttestFenceAsync(fence, partialHead.ConcurrencyStamp,
                partial.ConcurrencyStamp, Now.AddSeconds(2));
        else
            await database.Coordinator.ReportActivationAsync(secondReport, partialHead.ConcurrencyStamp,
                partial.ConcurrencyStamp, Now.AddSeconds(2));
        var (pending, pendingHead) = await ReadAsync(database, operation);
        await Assert.That(pending.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Collecting);
        await Assert.That(pending.ActivationCount).IsEqualTo(fenceFirst ? 1 : 2);
        await Assert.That(pending.Fence).IsEqualTo(fenceFirst ? fence : null);

        if (fenceFirst)
            await database.Coordinator.ReportActivationAsync(secondReport, pendingHead.ConcurrencyStamp,
                pending.ConcurrencyStamp, Now.AddSeconds(3));
        else
            await database.Coordinator.AttestFenceAsync(fence, pendingHead.ConcurrencyStamp,
                pending.ConcurrencyStamp, Now.AddSeconds(3));

        var (ready, readyHead) = await ReadAsync(database, operation);
        await Assert.That(ready.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.ReadyForCleanup);
        await Assert.That(ready.Activations).IsEquivalentTo(new[] { firstReport, secondReport });
        await Assert.That(ready.Fence).IsEqualTo(fence);
        await Assert.That(ready.UpdatedAt).IsEqualTo(Now.AddSeconds(3));
        await Assert.That(ready.ConcurrencyStamp).IsNotEqualTo(pending.ConcurrencyStamp);
        await Assert.That(readyHead.ConcurrencyStamp).IsNotEqualTo(pendingHead.ConcurrencyStamp);
        ready.EnsureCleanupAllowed(readyHead.CurrentOperationId, ready.ConcurrencyStamp, Now.AddSeconds(4));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredCallbackPersistsTerminalReceiptDespiteException(bool fenceCallback)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (before, head) = await ReadAsync(database, operation);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fenceCallback
            ? database.Coordinator.AttestFenceAsync(Fence(before), head.ConcurrencyStamp,
                before.ConcurrencyStamp, before.Deadline)
            : database.Coordinator.ReportActivationAsync(Activation(before), head.ConcurrencyStamp,
                before.ConcurrencyStamp, before.Deadline));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_expired");
        var (expired, expiredHead) = await ReadAsync(database, operation);
        await Assert.That(expired.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Expired);
        await Assert.That(expired.UpdatedAt).IsEqualTo(before.Deadline);
        await Assert.That(expired.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(expired.Activations).IsEmpty();
        await Assert.That(expired.Fence).IsNull();
        await Assert.That(expiredHead.CurrentOperationId).IsEqualTo(operation.Id);
        await Assert.That(expiredHead.ConcurrencyStamp).IsNotEqualTo(head.ConcurrencyStamp);

        var terminal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.ReportActivationAsync(Activation(expired), expiredHead.ConcurrencyStamp,
                expired.ConcurrencyStamp, expired.Deadline.AddSeconds(1)));
        await Assert.That(terminal?.Message).IsEqualTo("authentication_cutover_terminal");
        await AssertUnchangedAsync(database, expired, expiredHead);
    }

    [Test]
    public async Task ExpiredPredecessorPersistsExpiryWithoutInstallingReplacement()
    {
        await using var database = await TestDatabase.CreateAsync();
        var original = database.CreateOperation();
        await database.Coordinator.PrepareAsync(original, null, null, null, Now);
        var (before, head) = await ReadAsync(database, original);
        var replacement = database.CreateOperation(original.InstanceId, createdAt: original.Deadline);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.PrepareAsync(replacement, original.Id, head.ConcurrencyStamp,
                before.ConcurrencyStamp, original.Deadline));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_expired");
        var (expired, currentHead) = await ReadAsync(database, original);
        await Assert.That(expired.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Expired);
        await Assert.That(expired.UpdatedAt).IsEqualTo(original.Deadline);
        await Assert.That(expired.ConcurrencyStamp).IsNotEqualTo(before.ConcurrencyStamp);
        await Assert.That(currentHead.CurrentOperationId).IsEqualTo(original.Id);
        await using var reader = database.CreateDbContext();
        await Assert.That(await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(replacement.InstanceId, replacement.Id)).IsNull();
    }

    [Test]
    public async Task CrossInstanceCallbackCannotChangeEitherInstancesHeadOrEvidence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = database.CreateOperation();
        var second = database.CreateOperation(Guid.CreateVersion7());
        await database.Coordinator.PrepareAsync(first, null, null, null, Now);
        var preparation = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.PrepareAsync(second, null, null, null, Now));
        await Assert.That(preparation?.Message).IsEqualTo("authentication_cutover_instance_conflict");
        await AssertAbsentAsync(database, second);
        await database.SeedHistoricalAsync(second);
        var (firstReceipt, firstHead) = await ReadAsync(database, first);
        var (secondReceipt, secondHead) = await ReadAsync(database, second);
        var foreign = Activation(firstReceipt) with { InstanceId = second.InstanceId };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.ReportActivationAsync(foreign, secondHead.ConcurrencyStamp,
                firstReceipt.ConcurrencyStamp, Now.AddSeconds(1)));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_instance_conflict");
        await AssertUnchangedAsync(database, firstReceipt, firstHead);
        await AssertUnchangedAsync(database, secondReceipt, secondHead);

        await database.Coordinator.ReportActivationAsync(Activation(firstReceipt), firstHead.ConcurrencyStamp,
            firstReceipt.ConcurrencyStamp, Now.AddSeconds(1));
        await AssertUnchangedAsync(database, secondReceipt, secondHead);
        var (activated, _) = await ReadAsync(database, first);
        await Assert.That(activated.ActivationCount).IsEqualTo(1);
        await using var reader = database.CreateDbContext();
        await Assert.That(await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(second.InstanceId, first.Id)).IsNull();
    }

    [Test]
    [Arguments("anonymous", "prepare")]
    [Arguments("anonymous", "activation")]
    [Arguments("anonymous", "fence")]
    [Arguments("identity", "prepare")]
    [Arguments("identity", "activation")]
    [Arguments("identity", "fence")]
    [Arguments("grant", "prepare")]
    [Arguments("grant", "activation")]
    [Arguments("grant", "fence")]
    [Arguments("actor", "prepare")]
    [Arguments("actor", "activation")]
    [Arguments("actor", "fence")]
    [Arguments("role-scope", "prepare")]
    [Arguments("role-scope", "activation")]
    [Arguments("role-scope", "fence")]
    [Arguments("bootstrap-missing", "prepare")]
    [Arguments("bootstrap-missing", "activation")]
    [Arguments("bootstrap-missing", "fence")]
    [Arguments("bootstrap-pending", "prepare")]
    [Arguments("bootstrap-pending", "activation")]
    [Arguments("bootstrap-pending", "fence")]
    [Arguments("bootstrap-foreign", "prepare")]
    [Arguments("bootstrap-foreign", "activation")]
    [Arguments("bootstrap-foreign", "fence")]
    public async Task LostOperatorAuthorityRejectsWritesWithoutChangingPersistedEvidence(
        string rejection, string mutation)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (prepared, preparedHead) = await ReadAsync(database, operation);
        await database.Coordinator.ReportActivationAsync(Activation(prepared), preparedHead.ConcurrencyStamp,
            prepared.ConcurrencyStamp, Now.AddSeconds(1));
        var (before, head) = await ReadAsync(database, operation);
        var replacement = database.CreateOperation();

        // Change committed authority after preparation; callbacks must not reuse its authorization.
        await database.RemoveAuthorityAsync(rejection);

        Func<Task> write = () => mutation switch
        {
            "prepare" => database.Coordinator.PrepareAsync(replacement, before.Id,
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(2)),
            "activation" => database.Coordinator.ReportActivationAsync(Activation(before),
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(2)),
            _ => database.Coordinator.AttestFenceAsync(Fence(before),
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(2))
        };
        if (rejection == "bootstrap-foreign")
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(write);
            await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_instance_conflict");
        }
        else
        {
            var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(write);
            await Assert.That(exception?.Message).IsEqualTo(rejection.StartsWith("bootstrap-", StringComparison.Ordinal)
                ? "authentication_cutover_instance_unavailable" : "authentication_cutover_operator_required");
        }

        await AssertUnchangedAsync(database, before, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(replacement.InstanceId, replacement.Id)).IsNull();
    }

    [Test]
    public async Task ForgedCreatorCannotCommitReceiptOrHead()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation(createdBy: Guid.CreateVersion7());

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            database.Coordinator.PrepareAsync(operation, null, null, null, Now));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_operator_conflict");
        await AssertAbsentAsync(database, operation);
    }

    [Test]
    public async Task ForeignFenceActorCannotChangeHeadOrExistingEvidence()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (prepared, preparedHead) = await ReadAsync(database, operation);
        await database.Coordinator.ReportActivationAsync(Activation(prepared), preparedHead.ConcurrencyStamp,
            prepared.ConcurrencyStamp, Now.AddSeconds(1));
        var (before, head) = await ReadAsync(database, operation);
        var forged = Fence(before) with { AttestedBy = Guid.CreateVersion7() };

        var exception = await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            database.Coordinator.AttestFenceAsync(forged, head.ConcurrencyStamp,
                before.ConcurrencyStamp, Now.AddSeconds(2)));

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_operator_conflict");
        await AssertUnchangedAsync(database, before, head);
    }

    [Test]
    [Arguments("authority", "prepare")]
    [Arguments("authority", "activation")]
    [Arguments("authority", "fence")]
    [Arguments("revision", "prepare")]
    [Arguments("revision", "activation")]
    [Arguments("revision", "fence")]
    public async Task ChangedDeploymentProfileRejectsWritesWithoutChangingPersistedEvidence(
        string mismatch, string mutation)
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        var (before, head) = await ReadAsync(database, operation);
        var replacement = database.CreateOperation();
        database.Profile.Current = mismatch == "authority"
            ? database.Profile.Current with { Authority = AuthenticationCredentialCutover.CredentialAuthority.Environment }
            : database.Profile.Current with { Revision = Guid.CreateVersion7() };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => mutation switch
        {
            "prepare" => database.Coordinator.PrepareAsync(replacement, before.Id,
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(1)),
            "activation" => database.Coordinator.ReportActivationAsync(Activation(before),
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(1)),
            _ => database.Coordinator.AttestFenceAsync(Fence(before),
                head.ConcurrencyStamp, before.ConcurrencyStamp, Now.AddSeconds(1))
        });

        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_profile_conflict");
        await AssertUnchangedAsync(database, before, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await new AuthenticationCredentialCutoverRepository(reader)
            .GetAsync(replacement.InstanceId, replacement.Id)).IsNull();
    }

    [Test]
    [Arguments("auth.google_client_secret")]
    [Arguments("auth.keycloak_client_secret")]
    public async Task RetiredCredentialSettingCannotBePersistedByTrackedWriter(string settingKey)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        if (await context.Set<Explore.Domain.SettingValueTypeLookup>().FindAsync(0) is null)
        {
            context.Add(new Explore.Domain.SettingValueTypeLookup
            {
                Id = 0, MasterCode = "string", FullName = "String"
            });
            await context.SaveChangesAsync();
        }
        context.SystemSettings.Add(new Explore.Domain.SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = settingKey,
            Value = System.Text.Json.JsonSerializer.Serialize(
                Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))),
            SettingValueTypeId = 0,
            CreatedAt = Now.UtcDateTime
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        await Assert.That(exception?.Message).IsEqualTo("authentication_credential_setting_retired");
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.SettingKey == settingKey)).IsFalse();
    }

    [Test]
    [Arguments("auth.google_client_secret", "/google")]
    [Arguments("keycloak.blazor_client_secret", "/keycloak")]
    public async Task RetiredRuntimeBindingCannotBeResurrectedByTrackedWriter(string settingKey, string legacyPath)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var context = database.CreateDbContext();
        if (await context.Set<Explore.Domain.SettingScopeLookup>().FindAsync(1) is null)
            context.Add(new Explore.Domain.SettingScopeLookup
                { Id = 1, MasterCode = "instance", FullName = "Instance" });
        if (await context.Set<Explore.Domain.SecretSourceTypeLookup>().FindAsync(0) is null)
            context.Add(new Explore.Domain.SecretSourceTypeLookup
                { Id = 0, MasterCode = "infisical", FullName = "Infisical" });
        if (await context.Set<Explore.Domain.SecretValidationStatus>().FindAsync(0) is null)
            context.Add(new Explore.Domain.SecretValidationStatus
                { Id = 0, MasterCode = "unknown", FullName = "Unknown" });
        await context.SaveChangesAsync();
        var definition = SecretDefinitionRegistry.GetRequired(settingKey);
        var binding = SecretBinding.CreateInfisical(settingKey, SecretScope.Instance, null,
            "testing", legacyPath, definition.DefaultInfisicalKey!);
        binding.Id = Guid.CreateVersion7();
        context.SecretBindings.Add(binding);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        await Assert.That(exception?.Message).IsEqualTo("authentication_runtime_reference_invalid");
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SecretBindings.AnyAsync(row => row.Id == binding.Id)).IsFalse();
    }

    [Test]
    public async Task CleanupCommitsExactCountsAndRepeatCannotDeleteOtherRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid google = await database.SeedLegacySettingAsync("auth.google_client_secret");
        Guid keycloak = await database.SeedLegacySettingAsync("auth.keycloak_client_secret");
        Guid unrelated = await database.SeedLegacySettingAsync("cutover.untouched");
        var inventory = await database.Coordinator.InspectCleanupAsync(ready.InstanceId, ready.Id, Now.AddSeconds(3));
        await Assert.That(inventory.Settings.Select(row => row.Id)).IsEquivalentTo(new[] { google, keycloak });
        await Assert.That(inventory.Bindings).IsEmpty();
        var cleaned = await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
            head.ConcurrencyStamp, ready.ConcurrencyStamp, [google, keycloak], [], Now.AddSeconds(3));
        await Assert.That(cleaned.State).IsEqualTo(AuthenticationCredentialCutover.CutoverState.Cleaned);
        await Assert.That(cleaned.CleanedSettingCount).IsEqualTo(2);
        await Assert.That(cleaned.CleanedBindingCount).IsEqualTo(0);
        var (retained, retainedHead) = await ReadAsync(database, ready);
        var repeated = await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
            head.ConcurrencyStamp, ready.ConcurrencyStamp, [unrelated], [], Now.AddSeconds(4));
        await Assert.That(repeated.ConcurrencyStamp).IsEqualTo(cleaned.ConcurrencyStamp);
        await Assert.That(repeated.CleanedSettingCount).IsEqualTo(2);
        await AssertUnchangedAsync(database, retained, retainedHead);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == google || row.Id == keycloak)).IsFalse();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == unrelated)).IsTrue();
    }

    [Test]
    public async Task ChangedCleanupApprovalCannotDeleteLegacyOrUnrelatedRows()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid legacy = await database.SeedLegacySettingAsync("auth.google_client_secret");
        Guid unrelated = await database.SeedLegacySettingAsync("cutover.untouched");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
                head.ConcurrencyStamp, ready.ConcurrencyStamp, [unrelated], [], Now.AddSeconds(3)));
        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_cleanup_targets_changed");
        await AssertUnchangedAsync(database, ready, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.CountAsync(row => row.Id == legacy || row.Id == unrelated)).IsEqualTo(2);
    }

    [Test]
    public async Task MissingProcessFenceCannotAuthorizeExactCleanup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var operation = database.CreateOperation();
        await database.Coordinator.PrepareAsync(operation, null, null, null, Now);
        Guid legacy = await database.SeedLegacySettingAsync("auth.google_client_secret");
        var (before, head) = await ReadAsync(database, operation);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Coordinator.CleanupAsync(before.InstanceId, before.Id, before.DeploymentRevision,
                head.ConcurrencyStamp, before.ConcurrencyStamp, [legacy], [], Now.AddSeconds(1)));
        await Assert.That(exception?.Message).IsEqualTo("authentication_cutover_not_ready");
        await AssertUnchangedAsync(database, before, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == legacy)).IsTrue();
    }

    [Test]
    public async Task CancellationDuringCleanupAfterActualDeletionRestoresTargetsReceiptAndHead()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid legacy = await database.SeedLegacySettingAsync("auth.google_client_secret");
        using var canceled = new CancellationTokenSource();
        var probe = new DeleteCancellationProbe(canceled);
        database.CommandProbe = probe;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
                head.ConcurrencyStamp, ready.ConcurrencyStamp, [legacy], [], Now.AddSeconds(3), canceled.Token));
        await Assert.That(await probe.DeletedRows.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(1);
        database.CommandProbe = null;
        await AssertUnchangedAsync(database, ready, head);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == legacy)).IsTrue();
    }

    [Test]
    public async Task CapturedOldWriterCannotResurrectCredentialDuringCleanup()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid legacy = await database.SeedLegacySettingAsync("auth.google_client_secret");
        await using var oldWriter = database.CreateDbContext();
        var stale = await oldWriter.SystemSettings.SingleAsync(row => row.Id == legacy);
        stale.Value = System.Text.Json.JsonSerializer.Serialize(
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> AttemptWriteAsync()
        {
            await release.Task;
            return await Assert.ThrowsAsync<InvalidOperationException>(() => oldWriter.SaveChangesAsync());
        }
        Task<Exception?> rejectedWriter = AttemptWriteAsync();
        async Task<AuthenticationCredentialCutover> CleanupAsync()
        {
            await release.Task;
            return await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
                head.ConcurrencyStamp, ready.ConcurrencyStamp, [legacy], [], Now.AddSeconds(3));
        }
        Task<AuthenticationCredentialCutover> cleanup = CleanupAsync();
        release.TrySetResult();
        await Task.WhenAll(rejectedWriter, cleanup).WaitAsync(TimeSpan.FromSeconds(30));
        await Assert.That((await rejectedWriter)?.Message).IsEqualTo("authentication_credential_setting_retired");
        await Assert.That((await cleanup).CleanedSettingCount).IsEqualTo(1);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == legacy)).IsFalse();
    }

    [Test]
    public async Task CleanupPreservesCanonicalTenantQualifiedAndInfrastructureBindings()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid legacy = await database.SeedHistoricalBindingAsync(
            SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret, "/keycloak");
        Guid canonical = await database.SeedHistoricalBindingAsync(
            SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret, "/runtime/bff/oidc/google");
        Guid tenant = await database.SeedHistoricalBindingAsync(
            SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret, "/keycloak", tenant: true);
        Guid qualified = await database.SeedHistoricalBindingAsync(
            SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret, "/keycloak", qualifier: "retained-other-purpose");
        Guid infrastructure = await database.SeedHistoricalBindingAsync(
            SecretDefinitionRegistry.Keys.Keycloak.DbPassword, "/keycloak");
        var inventory = await database.Coordinator.InspectCleanupAsync(ready.InstanceId, ready.Id, Now.AddSeconds(3));
        await Assert.That(inventory.Bindings.Select(row => row.Id)).IsEquivalentTo(new[] { legacy });
        var cleaned = await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
            head.ConcurrencyStamp, ready.ConcurrencyStamp, [], [legacy], Now.AddSeconds(3));
        await Assert.That(cleaned.CleanedSettingCount).IsEqualTo(0);
        await Assert.That(cleaned.CleanedBindingCount).IsEqualTo(1);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SecretBindings.AnyAsync(row => row.Id == legacy)).IsFalse();
        var preserved = await reader.SecretBindings.AsNoTracking().Where(row =>
            row.Id == canonical || row.Id == tenant || row.Id == qualified || row.Id == infrastructure)
            .Select(row => row.Id).ToListAsync();
        await Assert.That(preserved).IsEquivalentTo(new[] { canonical, tenant, qualified, infrastructure });
    }

    [Test]
    public async Task TerminalCleanupRecoverySurvivesReplacementAndProfileChangeWithoutMutatingEitherHead()
    {
        await using var database = await TestDatabase.CreateAsync();
        var (ready, head) = await database.PrepareReadyAsync();
        Guid legacy = await database.SeedLegacySettingAsync("auth.google_client_secret");
        var cleaned = await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
            head.ConcurrencyStamp, ready.ConcurrencyStamp, [legacy], [], Now.AddSeconds(3));
        var (_, cleanedHead) = await ReadAsync(database, ready);
        database.Profile.Current = database.Profile.Current with { Revision = Guid.CreateVersion7() };
        var replacement = database.CreateOperation();
        await database.Coordinator.PrepareAsync(replacement, ready.Id, cleanedHead.ConcurrencyStamp,
            cleaned.ConcurrencyStamp, Now.AddSeconds(4));
        var (current, currentHead) = await ReadAsync(database, replacement);
        Guid unrelated = await database.SeedLegacySettingAsync("cutover.untouched");
        var retained = await database.Coordinator.CleanupAsync(ready.InstanceId, ready.Id, ready.DeploymentRevision,
            head.ConcurrencyStamp, ready.ConcurrencyStamp, [unrelated], [], Now.AddMinutes(2));
        await Assert.That(retained.CleanedSettingCount).IsEqualTo(1);
        await Assert.That(retained.ConcurrencyStamp).IsEqualTo(cleaned.ConcurrencyStamp);
        await AssertUnchangedAsync(database, current, currentHead);
        await using var reader = database.CreateDbContext();
        await Assert.That(await reader.SystemSettings.AnyAsync(row => row.Id == unrelated)).IsTrue();
    }

    private sealed class DeleteCancellationProbe(CancellationTokenSource cancellation) : DbCommandInterceptor
    {
        public TaskCompletionSource<int> DeletedRows { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("system_settings", StringComparison.OrdinalIgnoreCase))
            {
                DeletedRows.TrySetResult(result);
                cancellation.Cancel();
            }
            return ValueTask.FromResult(result);
        }
    }

    private static AuthenticationCredentialCutover.ActivationReport Activation(
        AuthenticationCredentialCutover operation, int memberIndex = 0)
    {
        var member = operation.RequiredReplacements[memberIndex];
        return new(operation.InstanceId, operation.Id, operation.DeploymentRevision,
            member.ReplicaId, member.ProcessBootId, member.Consumer, member.Generation);
    }

    private static AuthenticationCredentialCutover.FenceAttestation Fence(
        AuthenticationCredentialCutover operation) =>
        new(operation.InstanceId, operation.Id, operation.DeploymentRevision,
            operation.CreatedBy, true, true, true);

    private static async Task<(AuthenticationCredentialCutover Receipt, AuthenticationCredentialCutoverHead Head)>
        ReadAsync(TestDatabase database, AuthenticationCredentialCutover operation)
    {
        await using var reader = database.CreateDbContext();
        var repository = new AuthenticationCredentialCutoverRepository(reader);
        var receipt = await repository.GetAsync(operation.InstanceId, operation.Id)
            ?? throw new InvalidOperationException("Expected persisted cutover receipt.");
        var head = await repository.GetHeadAsync(operation.InstanceId)
            ?? throw new InvalidOperationException("Expected persisted cutover head.");
        return (receipt, head);
    }

    private static async Task AssertAbsentAsync(TestDatabase database, AuthenticationCredentialCutover operation)
    {
        await using var reader = database.CreateDbContext();
        var repository = new AuthenticationCredentialCutoverRepository(reader);
        await Assert.That(await repository.GetAsync(operation.InstanceId, operation.Id)).IsNull();
        await Assert.That(await repository.GetHeadAsync(operation.InstanceId)).IsNull();
    }

    private static async Task AssertUnchangedAsync(TestDatabase database,
        AuthenticationCredentialCutover before, AuthenticationCredentialCutoverHead beforeHead)
    {
        var (after, afterHead) = await ReadAsync(database, before);
        await Assert.That(afterHead.CurrentOperationId).IsEqualTo(beforeHead.CurrentOperationId);
        await Assert.That(afterHead.ConcurrencyStamp).IsEqualTo(beforeHead.ConcurrencyStamp);
        await Assert.That(after.State).IsEqualTo(before.State);
        await Assert.That(after.ConcurrencyStamp).IsEqualTo(before.ConcurrencyStamp);
        await Assert.That(after.UpdatedAt).IsEqualTo(before.UpdatedAt);
        await Assert.That(after.RequiredReplacements).IsEquivalentTo(before.RequiredReplacements);
        await Assert.That(after.Activations).IsEquivalentTo(before.Activations);
        await Assert.That(after.Fence).IsEqualTo(before.Fence);
    }

    private sealed class TestDatabase(string connectionString, DbContextOptions<ExploreDbContext> options,
        bool sharedConnection)
        : IDbContextFactory<ExploreDbContext>, IAsyncDisposable
    {
        private readonly SqliteConnection _anchor = new(connectionString);
        private readonly ExploreDbContext _identityContext = new(options);
        public Guid ActorId { get; } = Guid.CreateVersion7();
        public Guid InstanceId { get; } = Guid.CreateVersion7();
        public IHttpContextAccessor HttpContext { get; } =
            new FixtureHttpContextAccessor { HttpContext = new DefaultHttpContext() };
        public DeploymentProfileReader Profile { get; } = new();
        public DbCommandInterceptor? CommandProbe { get; set; }
        public RelationalAuthenticationCredentialCutoverCoordinator Coordinator => new(this,
            new AdminContext(HttpContext, new PlatformUserRoleRepository(_identityContext),
                new TenantUserRoleGrantRepository(_identityContext), new OrganizationMemberRepository(_identityContext),
                new GroupMemberRepository(_identityContext), new UserExternalLoginRepository(_identityContext),
                NullLogger<AdminContext>.Instance), HttpContext, Profile);

        public AuthenticationCredentialCutover CreateOperation(Guid? instanceId = null,
            Guid? operationId = null, int memberCount = 1, DateTimeOffset? createdAt = null,
            Guid? createdBy = null) =>
            AuthenticationCredentialCutover.Create(instanceId ?? InstanceId,
                operationId ?? Guid.CreateVersion7(), createdBy ?? ActorId, createdAt ?? Now,
                (createdAt ?? Now).AddMinutes(10), Guid.CreateVersion7(),
                Profile.Current.Authority, Profile.Current.Revision,
                Enumerable.Range(0, memberCount).Select(_ => new AuthenticationCredentialCutover.Replacement(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    AuthenticationCredentialCutover.CredentialConsumer.GoogleOidc, Guid.CreateVersion7())));

        public async Task<(AuthenticationCredentialCutover Receipt, AuthenticationCredentialCutoverHead Head)>
            PrepareReadyAsync()
        {
            var operation = CreateOperation();
            await Coordinator.PrepareAsync(operation, null, null, null, Now);
            var (receipt, head) = await ReadAsync(this, operation);
            await Coordinator.ReportActivationAsync(Activation(receipt), head.ConcurrencyStamp,
                receipt.ConcurrencyStamp, Now.AddSeconds(1));
            (receipt, head) = await ReadAsync(this, operation);
            await Coordinator.AttestFenceAsync(Fence(receipt), head.ConcurrencyStamp,
                receipt.ConcurrencyStamp, Now.AddSeconds(2));
            return await ReadAsync(this, operation);
        }

        public async Task<Guid> SeedLegacySettingAsync(string settingKey)
        {
            await using var context = CreateDbContext();
            if (await context.Set<Explore.Domain.SettingValueTypeLookup>().FindAsync(0) is null)
            {
                context.Add(new Explore.Domain.SettingValueTypeLookup
                    { Id = 0, MasterCode = "string", FullName = "String" });
                await context.SaveChangesAsync();
            }
            Guid id = Guid.CreateVersion7();
            string value = System.Text.Json.JsonSerializer.Serialize(
                Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)));
            // The identifier comes only from this fixture's model. Values remain parameters.
            string table = context.Model.FindEntityType(typeof(Explore.Domain.SystemSetting))!.GetTableName()!;
            string sql = $"INSERT INTO \"{table}\" (id, setting_key, value, setting_value_type_id, is_locked, display_order, created_at) "
                + "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6})";
            await context.Database.ExecuteSqlRawAsync(sql,
                [id, settingKey, value, 0, true, 0, Now.UtcDateTime]);
            return id;
        }

        public async Task<Guid> SeedHistoricalBindingAsync(string settingKey, string path,
            bool tenant = false, string qualifier = "")
        {
            await using var context = CreateDbContext();
            foreach (int scope in new[] { 1, 2 })
                if (await context.Set<Explore.Domain.SettingScopeLookup>().FindAsync(scope) is null)
                    context.Add(new Explore.Domain.SettingScopeLookup
                    {
                        Id = scope, MasterCode = scope == 1 ? "instance" : "tenant",
                        FullName = scope == 1 ? "Instance" : "Tenant"
                    });
            if (await context.Set<Explore.Domain.SecretSourceTypeLookup>().FindAsync(0) is null)
                context.Add(new Explore.Domain.SecretSourceTypeLookup
                    { Id = 0, MasterCode = "infisical", FullName = "Infisical" });
            if (await context.Set<Explore.Domain.SecretValidationStatus>().FindAsync(0) is null)
                context.Add(new Explore.Domain.SecretValidationStatus
                    { Id = 0, MasterCode = "unknown", FullName = "Unknown" });
            await context.SaveChangesAsync();
            Guid id = Guid.CreateVersion7();
            var definition = SecretDefinitionRegistry.GetRequired(settingKey);
            string table = context.Model.FindEntityType(typeof(SecretBinding))!.GetTableName()!;
            string sql = $"INSERT INTO \"{table}\" "
                + "(id, setting_key, qualifier, setting_scope_id, scope_id, secret_source_type_id, "
                + "infisical_environment, infisical_path, infisical_key, is_locked, secret_validation_status_id, created_at) "
                + "VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11})";
            await context.Database.ExecuteSqlRawAsync(sql, new object?[]
            {
                id, settingKey, qualifier, tenant ? 2 : 1, tenant ? Guid.CreateVersion7() : null,
                0, "testing", path, definition.DefaultInfisicalKey, false, 0, Now.UtcDateTime
            });
            return id;
        }

        public async Task SeedHistoricalAsync(AuthenticationCredentialCutover operation)
        {
            await using var context = CreateDbContext();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var repository = new AuthenticationCredentialCutoverRepository(context);
            await repository.AddAsync(operation);
            await repository.AddHeadAsync(AuthenticationCredentialCutoverHead.Create(operation));
            await transaction.CommitAsync();
        }

        public async Task RemoveAuthorityAsync(string rejection)
        {
            if (rejection == "anonymous")
            {
                HttpContext.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity());
                return;
            }
            if (rejection == "identity")
            {
                HttpContext.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Role, "platform.admin")], "test-principal"));
                return;
            }
            await using var context = CreateDbContext();
            switch (rejection)
            {
                case "grant":
                    context.PlatformUserRoles.RemoveRange(
                        await context.PlatformUserRoles.Where(row => row.UserId == ActorId).ToListAsync());
                    break;
                case "actor":
                    var actor = await context.Users.SingleAsync(row => row.Id == ActorId);
                    actor.IsDeleted = true;
                    actor.DeletedAt = Now.UtcDateTime;
                    break;
                case "role-scope":
                    var scope = await context.RoleScopes.FindAsync((int)RoleScopeEnum.Tenant);
                    if (scope is null)
                    {
                        scope = new RoleScope
                        {
                            Id = (int)RoleScopeEnum.Tenant, MasterCode = "tenant", FullName = "Tenant"
                        };
                        context.RoleScopes.Add(scope);
                    }
                    await context.SaveChangesAsync();
                    // Scope is an alternate-key member. Exercise an actual native role update,
                    // not an EF tracked-key mutation that fails before authority is changed.
                    await context.Roles.Where(row => row.MasterCode == "platform.admin")
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RoleScopeId, scope.Id));
                    return;
                case "bootstrap-missing":
                case "bootstrap-pending":
                case "bootstrap-foreign":
                    context.InstanceBootstrapStates.RemoveRange(await context.InstanceBootstrapStates.ToListAsync());
                    await context.SaveChangesAsync();
                    if (rejection != "bootstrap-missing")
                    {
                        var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                            rejection == "bootstrap-foreign" ? Guid.CreateVersion7() : InstanceId,
                            DeploymentMode.MultiTenant, Now.UtcDateTime);
                        if (rejection == "bootstrap-foreign")
                            bootstrap.CompleteInteractive(ActorId, Now.UtcDateTime);
                        context.InstanceBootstrapStates.Add(bootstrap);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rejection));
            }
            await context.SaveChangesAsync();
        }

        public ExploreDbContext CreateDbContext()
        {
            var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
            if (sharedConnection)
                builder.UseSqlite(_anchor);
            if (CommandProbe is not null)
                builder.AddInterceptors(CommandProbe);
            return new(builder.Options);
        }

        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());

        public static async Task<TestDatabase> CreateAsync(bool authenticated = true, bool sharedConnection = false)
        {
            string connectionString = $"Data Source=file:cutover_coordinator_{Guid.CreateVersion7():N}?mode=memory&cache=shared";
            var options = new DbContextOptionsBuilder<ExploreDbContext>().UseSqlite(connectionString)
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
            var database = new TestDatabase(connectionString, options, sharedConnection);
            try
            {
                await database._anchor.OpenAsync();
                await using var context = database.CreateDbContext();
                await context.Database.EnsureCreatedAsync();
                var scope = await context.RoleScopes.FindAsync((int)RoleScopeEnum.Platform);
                if (scope is null)
                {
                    scope = new RoleScope
                    {
                        Id = (int)RoleScopeEnum.Platform, MasterCode = "platform", FullName = "Platform"
                    };
                    context.RoleScopes.Add(scope);
                }
                var role = await context.Roles.SingleOrDefaultAsync(row => row.MasterCode == "platform.admin");
                if (role is null)
                {
                    role = new Role
                    {
                        Id = (int)RoleEnum.Admin, MasterCode = "platform.admin",
                        FullName = "Platform administrator", RoleScope = scope, RoleScopeId = scope.Id
                    };
                    context.Roles.Add(role);
                }
                var user = new User
                {
                    Id = database.ActorId, CreatedAt = Now.UtcDateTime,
                    Pii = new UserPii
                    {
                        Email = $"cutover-{database.ActorId:N}@example.test",
                        FirstName = "Cutover", LastName = "Operator"
                    }
                };
                context.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), User = user, UserId = user.Id,
                    Role = role, RoleId = role.Id, GrantedAt = Now.UtcDateTime
                });
                var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                    database.InstanceId, DeploymentMode.MultiTenant, Now.UtcDateTime);
                bootstrap.CompleteInteractive(user.Id, Now.UtcDateTime);
                context.InstanceBootstrapStates.Add(bootstrap);
                await context.SaveChangesAsync();
                if (authenticated)
                    database.HttpContext.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, database.ActorId.ToString())], "test-principal"));
                return database;
            }
            catch
            {
                await database.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _identityContext.DisposeAsync();
            await _anchor.DisposeAsync();
        }
    }

    private sealed class DeploymentProfileReader : IAuthenticationCredentialProfileReader
    {
        public AuthenticationCredentialProfile Current { get; set; } = new(
            AuthenticationCredentialCutover.CredentialAuthority.Infisical, Guid.CreateVersion7());

        public AuthenticationCredentialProfile ReadCurrent() => Current;
    }

    // The fixture supplies the already-authenticated request frame directly. Creating the
    // production AsyncLocal accessor inside an awaited factory cannot set its caller's frame.
    private sealed class FixtureHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; }
    }
}
