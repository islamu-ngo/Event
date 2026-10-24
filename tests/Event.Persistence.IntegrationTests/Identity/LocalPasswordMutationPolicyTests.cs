using System.Buffers.Binary;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalPasswordMutationPolicyTests
{
    public enum Denial { Short, Overlong, BlockedLong }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, 15)]
    [Arguments(IdentityDatabaseTopology.Colocated, 128)]
    [Arguments(IdentityDatabaseTopology.External, 15)]
    [Arguments(IdentityDatabaseTopology.External, 128)]
    public async Task EveryCredentialMutationAcceptsSupplementaryScalarBoundary(
        IdentityDatabaseTopology topology, int count)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        string Password(int offset) => string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600 + offset), count));
        await using (AsyncServiceScope bootstrap = fixture.Provider.CreateAsyncScope())
        {
            LocalCredentialCreateRequest intent = Creation(fixture);
            await Assert.That(await fixture.StateStore(bootstrap).ValidateBootstrapCreationAsync(intent, Password(0), fixture.CancellationToken)).IsTrue();
            LocalCredentialCreateResult created = await fixture.StateStore(bootstrap).CreateBootstrapPendingAsync(
                intent, null, Password(0), fixture.CancellationToken);
            await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            LocalIdentityUser user = await fixture.Identity(bootstrap).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == created.Receipt!.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(bootstrap.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>()
                .PasswordHasher.VerifyHashedPassword(user, user.PasswordHash!, Password(0))).IsEqualTo(PasswordVerificationResult.Success);
        }
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(await fixture.ReadAsync()), Password(1)))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        var before = await fixture.ReadAsync();
        await using (AsyncServiceScope change = fixture.Provider.CreateAsyncScope())
        {
            await Assert.That(await Lifecycle(fixture, change).ChangePasswordAsync(new LocalIdentityPasswordChangeRequest(
                new LocalSessionAuthority(fixture.Receipt.LocalSubjectId, before.SecurityStamp!, true),
                fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId, Password(1), Password(2)), fixture.CancellationToken))
                .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        }
        LocalIdentityLifecycleTransport link = await Recovery(fixture);
        await using AsyncServiceScope recover = fixture.Provider.CreateAsyncScope();
        await Assert.That((await Lifecycle(fixture, recover).ConsumeAsync(new LocalIdentityLifecycleConsumption(
            link.Operation, link.Token, Password(3)), fixture.CancellationToken)).Outcome)
            .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        await Assert.That((await fixture.AuthenticateAsync(Password(3))).Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.Short)]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.Overlong)]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.BlockedLong)]
    [Arguments(IdentityDatabaseTopology.External, Denial.Short)]
    [Arguments(IdentityDatabaseTopology.External, Denial.Overlong)]
    [Arguments(IdentityDatabaseTopology.External, Denial.BlockedLong)]
    public async Task BootstrapAndReplacementDenialRetainCredentialAndOperation(
        IdentityDatabaseTopology topology, Denial denial)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        var before = await fixture.ReadAsync();
        string denied = Candidate(fixture, denial);
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(before), denied))
            .IsEqualTo(LocalCredentialReplacementOutcome.InvalidPassword);
        await fixture.AssertUnchangedAsync(before);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalCredentialCreateRequest request = Creation(fixture);
        await Assert.That(await fixture.StateStore(scope).ValidateBootstrapCreationAsync(request, denied, fixture.CancellationToken)).IsFalse();
        LocalCredentialCreateResult result = await fixture.StateStore(scope).CreateBootstrapPendingAsync(
            request, null, denied, fixture.CancellationToken);
        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Invalid);
        await Assert.That(await fixture.Identity(scope).Set<LocalIdentityCredentialOperation>()
            .AnyAsync(row => row.Id == request.OperationId, fixture.CancellationToken)).IsFalse();
        await Assert.That(await fixture.Identity(scope).Set<LocalIdentityUser>()
            .AnyAsync(row => row.UserName == request.Username, fixture.CancellationToken)).IsFalse();
        await fixture.AssertUnchangedAsync(before);
        // Policy denial does not consume the replacement authority.
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(before), NewPassword()))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.Short)]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.Overlong)]
    [Arguments(IdentityDatabaseTopology.Colocated, Denial.BlockedLong)]
    [Arguments(IdentityDatabaseTopology.External, Denial.Short)]
    [Arguments(IdentityDatabaseTopology.External, Denial.Overlong)]
    [Arguments(IdentityDatabaseTopology.External, Denial.BlockedLong)]
    public async Task ChangeAndRecoveryDenialRetainOriginalStampHashAndLiveProof(
        IdentityDatabaseTopology topology, Denial denial)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        string current = NewPassword();
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(await fixture.ReadAsync()), current))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        var before = await fixture.ReadAsync();
        string denied = Candidate(fixture, denial);
        await using (AsyncServiceScope change = fixture.Provider.CreateAsyncScope())
        {
            var authority = new LocalSessionAuthority(fixture.Receipt.LocalSubjectId, before.SecurityStamp!, true);
            await Assert.That(await Lifecycle(fixture, change).ChangePasswordAsync(new LocalIdentityPasswordChangeRequest(
                authority, fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId, current, denied), fixture.CancellationToken))
                .IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
        }
        await fixture.AssertUnchangedAsync(before);
        LocalIdentityLifecycleTransport link = await Recovery(fixture);
        var admitted = await fixture.ReadAsync();
        await using (AsyncServiceScope recover = fixture.Provider.CreateAsyncScope())
        {
            var store = Lifecycle(fixture, recover);
            await Assert.That((await store.ConsumeAsync(new LocalIdentityLifecycleConsumption(
                link.Operation, link.Token, denied), fixture.CancellationToken)).Outcome)
                .IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
            LocalIdentityLifecycleOperation operation = await fixture.Identity(recover).Set<LocalIdentityLifecycleOperation>()
                .AsNoTracking().SingleAsync(row => row.Id == link.Operation.OperationId, fixture.CancellationToken);
            await Assert.That(operation.ConsumedAt).IsNull();
            await Assert.That(operation.ResultSecurityStamp).IsNull();
            await Assert.That(await store.IssueTransportTokenAsync(link.Operation, fixture.CancellationToken)).IsNotNull();
        }
        await fixture.AssertUnchangedAsync(admitted);
        await Assert.That(await fixture.PasswordIsValidAsync(current)).IsTrue();
        await using AsyncServiceScope retry = fixture.Provider.CreateAsyncScope();
        await Assert.That((await Lifecycle(fixture, retry).ConsumeAsync(new LocalIdentityLifecycleConsumption(
            link.Operation, link.Token, NewPassword()), fixture.CancellationToken)).Outcome)
            .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task UnusableSnapshotRejectsGeneratedCreateAndResetBeforeAnyMutation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        var before = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        manager.PasswordValidators.Clear();
        manager.PasswordValidators.Add(new LocalIdentityPasswordValidator(LocalPasswordBlocklistSnapshot.Load(new LocalIdentityOptions())));
        LocalCredentialCreateRequest request = Creation(fixture);
        await Assert.That((await fixture.StateStore(scope).CreatePendingAsync(request, fixture.CancellationToken)).Outcome)
            .IsEqualTo(LocalCredentialCreateOutcome.Invalid);
        LocalCredentialResetResult reset = await fixture.StateStore(scope).ResetAsync(new LocalCredentialResetRequest(
            Guid.CreateVersion7(), fixture.Receipt.InitiatingApplicationUserId, fixture.Receipt.LocalSubjectId,
            fixture.Receipt.OperationId, before.OperationStamp, "Unavailable policy denial"), fixture.CancellationToken);
        await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Invalid);
        await Assert.That(await fixture.Identity(scope).Set<LocalIdentityCredentialOperation>()
            .CountAsync(fixture.CancellationToken)).IsEqualTo(1);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NfcIsSharedByReplacementChangeRecoveryAndLogin(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        var replacement = EquivalentPasswords();
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(await fixture.ReadAsync()), replacement.Decomposed))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await Assert.That((await fixture.AuthenticateAsync(replacement.Composed)).Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
        var before = await fixture.ReadAsync();
        await using (AsyncServiceScope same = fixture.Provider.CreateAsyncScope())
        {
            await Assert.That(await Lifecycle(fixture, same).ChangePasswordAsync(new LocalIdentityPasswordChangeRequest(
                new LocalSessionAuthority(fixture.Receipt.LocalSubjectId, before.SecurityStamp!, true),
                fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId,
                replacement.Composed, replacement.Composed), fixture.CancellationToken))
                .IsEqualTo(LocalIdentityLifecycleOutcome.SamePassword);
        }
        await fixture.AssertUnchangedAsync(before);
        var changed = EquivalentPasswords();
        await using (AsyncServiceScope change = fixture.Provider.CreateAsyncScope())
        {
            await Assert.That(await Lifecycle(fixture, change).ChangePasswordAsync(new LocalIdentityPasswordChangeRequest(
                new LocalSessionAuthority(fixture.Receipt.LocalSubjectId, before.SecurityStamp!, true),
                fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId,
                replacement.Composed, changed.Decomposed), fixture.CancellationToken))
                .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        }
        await Assert.That((await fixture.AuthenticateAsync(changed.Composed)).Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
        LocalIdentityLifecycleTransport link = await Recovery(fixture);
        var recovered = EquivalentPasswords();
        await using AsyncServiceScope recover = fixture.Provider.CreateAsyncScope();
        await Assert.That((await Lifecycle(fixture, recover).ConsumeAsync(new LocalIdentityLifecycleConsumption(
            link.Operation, link.Token, recovered.Decomposed), fixture.CancellationToken)).Outcome)
            .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        await Assert.That((await fixture.AuthenticateAsync(recovered.Composed)).Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NativePersistedHashesHaveIndependentSaltsAndSuccessfulWorkFactorRehash(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        var password = EquivalentPasswords();
        Guid firstId;
        Guid secondId;
        await using (AsyncServiceScope create = fixture.Provider.CreateAsyncScope())
        {
            var manager = create.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            manager.PasswordHasher = new NormalizedLocalIdentityPasswordHasher(Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = LocalIdentityOptions.MinimumHashIterations
            }));
            LocalCredentialCreateResult first = await fixture.StateStore(create).CreateBootstrapPendingAsync(
                Creation(fixture), null, password.Decomposed, fixture.CancellationToken);
            LocalCredentialCreateResult second = await fixture.StateStore(create).CreateBootstrapPendingAsync(
                Creation(fixture), null, password.Composed, fixture.CancellationToken);
            await Assert.That(first.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            await Assert.That(second.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            firstId = first.Receipt!.LocalSubjectId;
            secondId = second.Receipt!.LocalSubjectId;
        }
        string original;
        await using (AsyncServiceScope read = fixture.Provider.CreateAsyncScope())
        {
            LocalIdentityUser first = await fixture.Identity(read).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == firstId, fixture.CancellationToken);
            LocalIdentityUser second = await fixture.Identity(read).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == secondId, fixture.CancellationToken);
            original = first.PasswordHash!;
            byte[] firstBytes = Convert.FromBase64String(original);
            byte[] secondBytes = Convert.FromBase64String(second.PasswordHash!);
            await Assert.That(firstBytes[0]).IsEqualTo((byte)1);
            await Assert.That(firstBytes.Length).IsEqualTo(61);
            await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(firstBytes.AsSpan(1, 4))).IsEqualTo(2u);
            await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(firstBytes.AsSpan(5, 4))).IsEqualTo(100_000u);
            await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(firstBytes.AsSpan(9, 4))).IsEqualTo(16u);
            await Assert.That(firstBytes.AsSpan(13, 16).SequenceEqual(secondBytes.AsSpan(13, 16))).IsFalse();
            var manager = read.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            await Assert.That(manager.PasswordHasher.VerifyHashedPassword(first, original, password.Composed))
                .IsEqualTo(PasswordVerificationResult.SuccessRehashNeeded);
            await Assert.That(await manager.CheckPasswordAsync(first, password.Composed)).IsTrue();
        }
        await using AsyncServiceScope after = fixture.Provider.CreateAsyncScope();
        LocalIdentityUser persisted = await fixture.Identity(after).Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == firstId, fixture.CancellationToken);
        byte[] upgraded = Convert.FromBase64String(persisted.PasswordHash!);
        await Assert.That(persisted.PasswordHash == original).IsFalse();
        await Assert.That(BinaryPrimitives.ReadUInt32BigEndian(upgraded.AsSpan(5, 4))).IsEqualTo(210_000u);
        await Assert.That(after.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>()
            .PasswordHasher.VerifyHashedPassword(persisted, persisted.PasswordHash!, password.Decomposed))
            .IsEqualTo(PasswordVerificationResult.Success);
    }

    private static LocalCredentialCreateRequest Creation(Fixture fixture) => new(
        Guid.CreateVersion7(), fixture.Receipt.InitiatingApplicationUserId, null, "Policy", "Owner", Guid.CreateVersion7().ToString("N"));

    private static string Candidate(Fixture fixture, Denial denial) => denial switch
    {
        Denial.Short => Convert.ToHexString(RandomNumberGenerator.GetBytes(7)),
        Denial.Overlong => string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), 129)),
        Denial.BlockedLong => fixture.BlockedPassword,
        _ => throw new ArgumentOutOfRangeException(nameof(denial))
    };

    private static string NewPassword() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static (string Composed, string Decomposed) EquivalentPasswords()
    {
        string prefix = NewPassword();
        return (prefix + char.ConvertFromUtf32(0xE9), prefix + char.ConvertFromUtf32(0x65) + char.ConvertFromUtf32(0x301));
    }

    private static LocalIdentityLifecycleStore Lifecycle(Fixture fixture, AsyncServiceScope scope) => new(
        fixture.Identity(scope), scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Clock, fixture.StateStore(scope),
        NativeIdentityRecognitionBoundary.For(scope.ServiceProvider.GetRequiredService<ExploreDbContext>(), fixture.Clock));

    private static async Task<LocalIdentityLifecycleTransport> Recovery(Fixture fixture)
    {
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityUser user = await fixture.Identity(scope).Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        var store = Lifecycle(fixture, scope);
        LocalIdentityLifecyclePointer? pointer = await store.BeginAsync(new LocalIdentityLifecycleRequest(
            user.Id, fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId,
            LocalIdentityLifecyclePurpose.PasswordRecovery, user.Email!), fixture.CancellationToken);
        await Assert.That(pointer).IsNotNull();
        LocalIdentityLifecycleTransport? transport = await store.IssueTransportTokenAsync(pointer!, fixture.CancellationToken);
        await Assert.That(transport).IsNotNull();
        return transport!;
    }
}
