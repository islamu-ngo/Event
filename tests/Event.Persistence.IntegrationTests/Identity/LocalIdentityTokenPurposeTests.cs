// ABOUTME: Exercises native Local lifecycle tokens and transactional authority over both SQLite Identity topologies.
// ABOUTME: Reuses real credential provisioning/binding; only clocks, ephemeral secrets and explicit race signals are controlled.

using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Identity;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Fixture = Event.Persistence.IntegrationTests.Identity.LocalCredentialFirstUseTests.Fixture;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityTokenPurposeTests
{
    public enum PointerMutation { Operation, Subject, Actor, Login, Purpose, Generation }
    public enum InvalidAuthority { Expired, StampRotated, SupervisedReset, SuspendedActor, MissingBinding, MissingState }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AnonymousRepeatRetainsPendingOperationAndOriginalDeadline(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleRequest request = await RequestAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        await using AsyncServiceScope first = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecyclePointer? original = await Store(fixture, first).BeginAsync(request, fixture.CancellationToken);
        LocalIdentityLifecycleTransport? firstLink = await Store(fixture, first).IssueTransportTokenAsync(original!, fixture.CancellationToken);
        fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        await using AsyncServiceScope repeat = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecyclePointer? repeated = await Store(fixture, repeat).BeginAsync(request, fixture.CancellationToken);
        await Assert.That(original).IsNotNull();
        await Assert.That(repeated).IsEqualTo(original);
        await Assert.That(await fixture.Identity(repeat).Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(1);
        LocalIdentityLifecycleTransport? repeatedLink = await Store(fixture, repeat).IssueTransportTokenAsync(repeated!, fixture.CancellationToken);
        await Assert.That(repeatedLink!.ExpiresAtUtc).IsEqualTo(firstLink!.ExpiresAtUtc);
        await Assert.That((await ConsumeAsync(fixture, original!, firstLink.Token, NewPassword())).Outcome)
            .IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalIdentityLifecyclePurpose.EmailVerification)]
    [Arguments(IdentityDatabaseTopology.External, LocalIdentityLifecyclePurpose.EmailVerification)]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalIdentityLifecyclePurpose.EmailChange)]
    [Arguments(IdentityDatabaseTopology.External, LocalIdentityLifecyclePurpose.EmailChange)]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalIdentityLifecyclePurpose.PasswordRecovery)]
    [Arguments(IdentityDatabaseTopology.External, LocalIdentityLifecyclePurpose.PasswordRecovery)]
    public async Task NativeConsumeMutatesExactlyOnceRevokesSessionAndKeepsCredentialPointer(
        IdentityDatabaseTopology topology, LocalIdentityLifecyclePurpose purpose)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        string originalPassword = await ReadyAsync(fixture);
        if (purpose == LocalIdentityLifecyclePurpose.EmailVerification) await SetVerifiedAsync(fixture, false);
        var before = await fixture.ReadAsync();
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, purpose);
        await Assert.That(link.ExpiresAtUtc - fixture.Clock.GetUtcNow()).IsEqualTo(
            TimeSpan.FromMinutes(purpose == LocalIdentityLifecyclePurpose.PasswordRecovery ? 15 : 30));
        string? password = purpose == LocalIdentityLifecyclePurpose.PasswordRecovery ? NewPassword() : null;
        LocalIdentityLifecycleResult result = await ConsumeAsync(fixture, link.Operation, link.Token, password);
        await Assert.That(result.Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        await Assert.That(result.Synchronization).IsNotNull();
        await Assert.That(result.Synchronization!.Operation).IsEqualTo(link.Operation);
        await Assert.That(result.Synchronization.EmailVerified).IsTrue();
        var after = await fixture.ReadAsync();
        await Assert.That(after.TokenValue == before.TokenValue).IsTrue();
        await Assert.That(after.OperationStamp).IsEqualTo(before.OperationStamp);
        await Assert.That(after.SecurityStamp == before.SecurityStamp).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(password ?? originalPassword)).IsTrue();
        if (password is not null) await Assert.That(await fixture.PasswordIsValidAsync(originalPassword)).IsFalse();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        await Assert.That(await fixture.StateStore(scope).ReadReadySessionAsync(new LocalSessionAuthority(
            fixture.Receipt.LocalSubjectId, before.SecurityStamp!, purpose != LocalIdentityLifecyclePurpose.EmailVerification), fixture.CancellationToken)).IsNull();
        await Assert.That(await Store(fixture, scope).IssueTransportTokenAsync(link.Operation, fixture.CancellationToken)).IsNull();
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, password)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        LocalIdentityLifecycleSynchronization? retry = await Store(fixture, scope).ReadSynchronizationAsync(link.Operation, fixture.CancellationToken);
        await Assert.That(retry).IsEqualTo(result.Synchronization);
        await Assert.That(await Store(fixture, scope).MarkSynchronizedAsync(link.Operation, fixture.CancellationToken)).IsTrue();
        await Assert.That(await Store(fixture, scope).MarkSynchronizedAsync(link.Operation, fixture.CancellationToken)).IsTrue();
        await Assert.That((await Store(fixture, scope).ReadSynchronizationAsync(link.Operation, fixture.CancellationToken))!.Synchronized).IsTrue();
        LocalIdentityLifecycleOperation durable = await fixture.Identity(scope).Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(fixture.CancellationToken);
        string serialized = JsonSerializer.Serialize(durable);
        await Assert.That(serialized.Contains(link.Token, StringComparison.Ordinal)).IsFalse();
        await Assert.That(serialized.Contains(originalPassword, StringComparison.Ordinal)).IsFalse();
        if (password is not null) await Assert.That(serialized.Contains(password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.Synchronization.Email).IsEqualTo(link.Address);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Operation)]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Subject)]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Actor)]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Login)]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Purpose)]
    [Arguments(IdentityDatabaseTopology.Colocated, PointerMutation.Generation)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Operation)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Subject)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Actor)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Login)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Purpose)]
    [Arguments(IdentityDatabaseTopology.External, PointerMutation.Generation)]
    public async Task WrongBoundPointerCannotIssueConsumeOrReadMirror(IdentityDatabaseTopology topology, PointerMutation mutation)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        LocalIdentityLifecyclePointer p = link.Operation;
        var wrong = new LocalIdentityLifecyclePointer(mutation == PointerMutation.Operation ? Guid.CreateVersion7() : p.OperationId,
            mutation == PointerMutation.Subject ? Guid.CreateVersion7() : p.LocalSubjectId,
            mutation == PointerMutation.Actor ? Guid.CreateVersion7() : p.PersonalActorId,
            mutation == PointerMutation.Login ? Guid.CreateVersion7() : p.ExternalLoginId,
            mutation == PointerMutation.Purpose ? LocalIdentityLifecyclePurpose.EmailVerification : p.Purpose,
            mutation == PointerMutation.Generation ? Guid.CreateVersion7() : p.Generation);
        var before = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        await Assert.That(await Store(fixture, scope).IssueTransportTokenAsync(wrong, fixture.CancellationToken)).IsNull();
        await Assert.That((await ConsumeAsync(fixture, wrong, link.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That(await Store(fixture, scope).ReadSynchronizationAsync(wrong, fixture.CancellationToken)).IsNull();
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NativeProvidersRejectWrongPurposeForeignAccountAndCrossOperationTokens(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport recovery = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        LocalIdentityLifecycleTransport change = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.EmailChange);
        var before = await fixture.ReadAsync();
        await Assert.That((await ConsumeAsync(fixture, recovery.Operation, change.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That((await ConsumeAsync(fixture, change.Operation, recovery.Token)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        LocalIdentityUser user = await fixture.Identity(scope).Set<LocalIdentityUser>().AsNoTracking().SingleAsync(fixture.CancellationToken);
        string wrongPurpose = await manager.GenerateUserTokenAsync(user, LocalIdentityLifecycleTokenProviders.Recovery,
            UserManager<LocalIdentityUser>.ResetPasswordTokenPurpose);
        await Assert.That((await ConsumeAsync(fixture, recovery.Operation, wrongPurpose, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        var foreign = new LocalIdentityUser { UserName = Guid.CreateVersion7().ToString("N"), Email = $"other-{Guid.CreateVersion7():N}@example.test" };
        await Assert.That((await manager.CreateAsync(foreign, NewPassword())).Succeeded).IsTrue();
        // Native provider verification binds the account even when both users share the same real cryptographic provider.
        await Assert.That(await manager.VerifyUserTokenAsync(foreign, LocalIdentityLifecycleTokenProviders.Recovery,
            UserManager<LocalIdentityUser>.ResetPasswordTokenPurpose, wrongPurpose)).IsFalse();
        await Assert.That(await manager.VerifyUserTokenAsync(user, LocalIdentityLifecycleTokenProviders.Recovery,
            UserManager<LocalIdentityUser>.ResetPasswordTokenPurpose, wrongPurpose)).IsTrue();
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.Expired)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.StampRotated)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.SupervisedReset)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.SuspendedActor)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.MissingBinding)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidAuthority.MissingState)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.Expired)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.StampRotated)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.SupervisedReset)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.SuspendedActor)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.MissingBinding)]
    [Arguments(IdentityDatabaseTopology.External, InvalidAuthority.MissingState)]
    public async Task ChangedAuthorityFailsClosedWithPretrackedRows(IdentityDatabaseTopology topology, InvalidAuthority mutation)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        await using AsyncServiceScope stale = fixture.Provider.CreateAsyncScope();
        await fixture.Identity(stale).Set<LocalIdentityUser>().SingleAsync(fixture.CancellationToken);
        await fixture.Identity(stale).Set<LocalIdentityLifecycleOperation>().SingleAsync(fixture.CancellationToken);
        await using (AsyncServiceScope scope = fixture.Provider.CreateAsyncScope())
        {
            DbContext identity = fixture.Identity(scope);
            var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            switch (mutation)
            {
                case InvalidAuthority.Expired: fixture.Clock.Advance(TimeSpan.FromMinutes(15)); break;
                case InvalidAuthority.StampRotated:
                    var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
                    LocalIdentityUser user = (await manager.FindByIdAsync(link.Operation.LocalSubjectId.ToString("D")))!;
                    await Assert.That((await manager.UpdateSecurityStampAsync(user)).Succeeded).IsTrue(); break;
                case InvalidAuthority.SupervisedReset: await ResetAsync(fixture); break;
                case InvalidAuthority.SuspendedActor:
                    await application.Actors.Where(row => row.Id == link.Operation.PersonalActorId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsSuspended, true), fixture.CancellationToken); break;
                case InvalidAuthority.MissingBinding:
                    await application.UserExternalLogins.Where(row => row.Id == link.Operation.ExternalLoginId).ExecuteDeleteAsync(fixture.CancellationToken); break;
                case InvalidAuthority.MissingState:
                    await identity.Set<IdentityUserToken<Guid>>().Where(row => row.UserId == link.Operation.LocalSubjectId)
                        .ExecuteDeleteAsync(fixture.CancellationToken); break;
            }
        }
        await Assert.That(await Store(fixture, stale).IssueTransportTokenAsync(link.Operation, fixture.CancellationToken)).IsNull();
        await Assert.That((await Store(fixture, stale).ConsumeAsync(new LocalIdentityLifecycleConsumption(link.Operation, link.Token, NewPassword()),
            fixture.CancellationToken)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That(await Store(fixture, stale).ReadSynchronizationAsync(link.Operation, fixture.CancellationToken)).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PasswordPolicyAndUnchangedPasswordDoNotConsumeOperation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        string password = await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        var before = await fixture.ReadAsync();
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, password)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.SamePassword);
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, NewPassword()[..11])).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
        string nativePolicyFailure = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, nativePolicyFailure)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
        await fixture.AssertUnchangedAsync(before);
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LateHandoffAndExpiryDuringNativeValidationCannotExtendDeadline(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        fixture.Clock.Advance(TimeSpan.FromMinutes(14));
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecycleTransport? late = await Store(fixture, scope).IssueTransportTokenAsync(link.Operation, fixture.CancellationToken);
        await Assert.That(late!.ExpiresAtUtc).IsEqualTo(link.ExpiresAtUtc);
        var before = await fixture.ReadAsync();
        fixture.ValidationExpiry.Enabled = true;
        await Assert.That((await ConsumeAsync(fixture, link.Operation, late.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That(fixture.ValidationExpiry.Observed).IsTrue();
        await fixture.AssertUnchangedAsync(before);
        await Assert.That(await Store(fixture, scope).IssueTransportTokenAsync(link.Operation, fixture.CancellationToken)).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task EmailChangeRequiresCurrentStampAndDoesNotAdoptOccupiedMailbox(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleRequest request = await RequestAsync(fixture, LocalIdentityLifecyclePurpose.EmailChange, $"new-{Guid.CreateVersion7():N}@example.test");
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var unauthenticated = new LocalIdentityLifecycleRequest(request.LocalSubjectId, request.PersonalActorId, request.ExternalLoginId,
            request.Purpose, request.Address);
        await Assert.That(await Store(fixture, scope).BeginAsync(unauthenticated, fixture.CancellationToken)).IsNull();
        LocalIdentityLifecyclePointer pointer = (await Store(fixture, scope).BeginAsync(request, fixture.CancellationToken))!;
        LocalIdentityLifecycleTransport link = (await Store(fixture, scope).IssueTransportTokenAsync(pointer, fixture.CancellationToken))!;
        var before = await fixture.ReadAsync();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var owner = new LocalIdentityUser { UserName = Guid.CreateVersion7().ToString("N"), Email = request.Address };
        await Assert.That((await manager.CreateAsync(owner, NewPassword())).Succeeded).IsTrue();
        await Assert.That((await ConsumeAsync(fixture, pointer, link.Token)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Conflict);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentConsumptionHasOneWinnerAndResetInvalidatesMirrorRetry(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string passwordA = NewPassword(); string passwordB = NewPassword();
        Task<LocalIdentityLifecycleResult> Run(string password, TaskCompletionSource ready) => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            LocalIdentityLifecycleStore store = Store(fixture, scope);
            ready.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await store.ConsumeAsync(new LocalIdentityLifecycleConsumption(link.Operation, link.Token, password), fixture.CancellationToken);
        }, fixture.CancellationToken);
        Task<LocalIdentityLifecycleResult> first = Run(passwordA, readyA);
        Task<LocalIdentityLifecycleResult> second = Run(passwordB, readyB);
        await Task.WhenAll(readyA.Task, readyB.Task).WaitAsync(fixture.CancellationToken);
        start.SetResult();
        LocalIdentityLifecycleResult[] results = await Task.WhenAll(first, second).WaitAsync(fixture.CancellationToken);
        await Assert.That(results.Count(result => result.Outcome == LocalIdentityLifecycleOutcome.Consumed)).IsEqualTo(1);
        await Assert.That(results.All(result => result.Outcome is LocalIdentityLifecycleOutcome.Consumed or LocalIdentityLifecycleOutcome.Invalid
            or LocalIdentityLifecycleOutcome.Conflict)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(results[0].Outcome == LocalIdentityLifecycleOutcome.Consumed ? passwordA : passwordB)).IsTrue();
        await ResetAsync(fixture);
        await using AsyncServiceScope retry = fixture.Provider.CreateAsyncScope();
        await Assert.That(await Store(fixture, retry).ReadSynchronizationAsync(link.Operation, fixture.CancellationToken)).IsNull();
        await Assert.That(await Store(fixture, retry).MarkSynchronizedAsync(link.Operation, fixture.CancellationToken)).IsFalse();
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FailureAfterReceiptWriteRollsBackPasswordStampAndConsumption(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        var before = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        fixture.WriteFault.Arm(fixture.Identity(scope), LocalCredentialFirstUseTests.ReplacementWriteBoundary.AfterSecondWrite);
        await Assert.ThrowsAsync<Exception>(() => Store(fixture, scope).ConsumeAsync(
            new LocalIdentityLifecycleConsumption(link.Operation, link.Token, NewPassword()), fixture.CancellationToken));
        await Assert.That(fixture.WriteFault.Observed).IsTrue();
        await Assert.That(fixture.WriteFault.AffectedRows).IsEqualTo(1);
        await fixture.AssertUnchangedAsync(before);
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NativeIdentifierResolutionNeverAdoptsDomainMailbox(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        LocalIdentityUser native = await fixture.Identity(scope).Set<LocalIdentityUser>().AsNoTracking().SingleAsync(fixture.CancellationToken);
        string decoy = $"decoy-{Guid.CreateVersion7():N}@example.test";
        await application.UserPii.Where(row => row.UserId == native.Id).ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Email, decoy), fixture.CancellationToken);
        LocalIdentityLifecycleStore store = Store(fixture, scope);
        await Assert.That(await store.FindRequestByIdentifierAsync(decoy, LocalIdentityLifecyclePurpose.PasswordRecovery, fixture.CancellationToken)).IsNull();
        LocalIdentityLifecycleRequest? byEmail = await store.FindRequestByIdentifierAsync(native.Email!.ToUpperInvariant(), LocalIdentityLifecyclePurpose.PasswordRecovery, fixture.CancellationToken);
        LocalIdentityLifecycleRequest? byName = await store.FindRequestByIdentifierAsync(native.UserName!, LocalIdentityLifecyclePurpose.PasswordRecovery, fixture.CancellationToken);
        await Assert.That(byEmail).IsEqualTo(byName);
        await Assert.That(byEmail!.LocalSubjectId).IsEqualTo(fixture.Receipt.LocalSubjectId);
        await Assert.That(byEmail.PersonalActorId).IsEqualTo(fixture.Receipt.PersonalActorId);
        await Assert.That(byEmail.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
        await SetVerifiedAsync(fixture, false);
        await Assert.That(await store.FindRequestByIdentifierAsync(native.Email, LocalIdentityLifecyclePurpose.PasswordRecovery, fixture.CancellationToken)).IsNull();
        await Assert.That(await store.FindRequestByIdentifierAsync(native.Email, LocalIdentityLifecyclePurpose.EmailVerification, fixture.CancellationToken)).IsNotNull();
        await Assert.That(await store.FindRequestByIdentifierAsync(native.Email, LocalIdentityLifecyclePurpose.EmailChange, fixture.CancellationToken)).IsNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task OrdinaryCurrentPasswordChangeIsSmtpIndependentAndRejectsEveryStaleAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        string password = await ReadyAsync(fixture);
        var before = await fixture.ReadAsync();
        var authority = new LocalSessionAuthority(fixture.Receipt.LocalSubjectId, before.SecurityStamp!, true);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecycleStore store = Store(fixture, scope);
        LocalIdentityPasswordChangeRequest Request(string current, string replacement, LocalSessionAuthority? session = null, Guid? actor = null) =>
            new(session ?? authority, actor ?? fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId, current, replacement);
        await Assert.That(await store.ChangePasswordAsync(Request(NewPassword(), NewPassword()), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That(await store.ChangePasswordAsync(Request(password, password), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.SamePassword);
        await Assert.That(await store.ChangePasswordAsync(Request(password, NewPassword()[..11]), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
        await Assert.That(await store.ChangePasswordAsync(Request(password, Convert.ToHexString(RandomNumberGenerator.GetBytes(24))), fixture.CancellationToken))
            .IsEqualTo(LocalIdentityLifecycleOutcome.InvalidPassword);
        await Assert.That(await store.ChangePasswordAsync(Request(password, NewPassword(), actor: Guid.CreateVersion7()), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await Assert.That(await store.ChangePasswordAsync(Request(password, NewPassword(), new LocalSessionAuthority(authority.LocalSubjectId,
            Guid.CreateVersion7().ToString("N"), true)), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await fixture.AssertUnchangedAsync(before);
        string replacement = NewPassword();
        await Assert.That(await store.ChangePasswordAsync(Request(password, replacement), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        var after = await fixture.ReadAsync();
        await Assert.That(after.TokenValue == before.TokenValue).IsTrue();
        await Assert.That(after.OperationStamp).IsEqualTo(before.OperationStamp);
        await Assert.That(after.SecurityStamp == before.SecurityStamp).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(replacement)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(password)).IsFalse();
        await Assert.That(await fixture.StateStore(scope).ReadReadySessionAsync(authority, fixture.CancellationToken)).IsNull();
        await Assert.That(await fixture.Identity(scope).Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(0);
        await Assert.That(await store.ChangePasswordAsync(Request(replacement, NewPassword()), fixture.CancellationToken)).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task MirrorRetryRequiresOriginalNativeTokenAndRollsBackRejectedCallback(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.EmailChange);
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        var consumed = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        string originalEmail = await application.Users.Where(row => row.Id == link.Operation.LocalSubjectId).Select(row => row.Pii!.Email).SingleAsync(fixture.CancellationToken);
        int callbacks = 0;
        async Task<bool> Mirror(LocalIdentityLifecycleSynchronization receipt, CancellationToken ct)
        {
            callbacks++;
            await WriteMirrorAsync(application, receipt, ct);
            return true;
        }
        var store = Store(fixture, scope);
        await Assert.That(await store.ExecuteSynchronizationAsync(new LocalIdentityLifecycleConsumption(link.Operation,
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), Mirror, fixture.CancellationToken)).IsFalse();
        await Assert.That(callbacks).IsEqualTo(0);
        var authority = new LocalIdentityLifecycleConsumption(link.Operation, link.Token);
        await Assert.That(await store.ExecuteSynchronizationAsync(authority, async (receipt, ct) =>
        {
            await WriteMirrorAsync(application, receipt, ct);
            return false;
        }, fixture.CancellationToken)).IsFalse();
        await Assert.That(await application.Users.AsNoTracking().Where(row => row.Id == link.Operation.LocalSubjectId).Select(row => row.Pii!.Email)
            .SingleAsync(fixture.CancellationToken)).IsEqualTo(originalEmail);
        await Assert.That(await store.ExecuteSynchronizationAsync(authority, Mirror, fixture.CancellationToken)).IsTrue();
        await Assert.That(await store.ExecuteSynchronizationAsync(authority, Mirror, fixture.CancellationToken)).IsTrue();
        await Assert.That(callbacks).IsEqualTo(2);
        await Assert.That(await application.Users.AsNoTracking().Where(row => row.Id == link.Operation.LocalSubjectId).Select(row => row.Pii!.Email)
            .SingleAsync(fixture.CancellationToken)).IsEqualTo(link.Address);
        var after = await fixture.ReadAsync();
        await Assert.That(after.PasswordHash == consumed.PasswordHash).IsTrue();
        await Assert.That(after.SecurityStamp == consumed.SecurityStamp).IsTrue();
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));
        await Assert.That(await store.ExecuteSynchronizationAsync(authority, Mirror, fixture.CancellationToken)).IsFalse();
        await Assert.That(callbacks).IsEqualTo(2);
        // Trusted durable reconciliation remains runnable after link expiry; it cannot replay Identity mutation.
        await application.UserPii.Where(row => row.UserId == link.Operation.LocalSubjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Email, originalEmail), fixture.CancellationToken);
        await Assert.That(await store.ExecuteSynchronizationAsync(link.Operation, Mirror, fixture.CancellationToken)).IsTrue();
        await Assert.That(await application.UserPii.AsNoTracking().Where(row => row.UserId == link.Operation.LocalSubjectId)
            .Select(row => row.Email).SingleAsync(fixture.CancellationToken)).IsEqualTo(link.Address);
        await Assert.That(callbacks).IsEqualTo(3);
        var reconciled = await fixture.ReadAsync();
        await Assert.That(reconciled.PasswordHash == consumed.PasswordHash).IsTrue();
        await Assert.That(reconciled.SecurityStamp == consumed.SecurityStamp).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task MirrorTransactionSerializesWithRealSupervisedReset(IdentityDatabaseTopology topology)
    {
        var observer = new TransactionAttemptObserver();
        await using Fixture fixture = await Fixture.CreateAsync(topology, observer);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.EmailChange);
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token)).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
        var consumed = await fixture.ReadAsync();
        await using AsyncServiceScope synchronization = fixture.Provider.CreateAsyncScope();
        await using AsyncServiceScope resetScope = fixture.Provider.CreateAsyncScope();
        observer.Arm(fixture.Identity(resetScope));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var application = synchronization.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Task<bool> sync = Task.Run(() => Store(fixture, synchronization).ExecuteSynchronizationAsync(
            new LocalIdentityLifecycleConsumption(link.Operation, link.Token), async (receipt, ct) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(ct);
                await WriteMirrorAsync(application, receipt, ct);
                return true;
            }, fixture.CancellationToken), fixture.CancellationToken);
        Task<LocalCredentialResetResult>? reset = null;
        try
        {
            await entered.Task.WaitAsync(fixture.CancellationToken);
            reset = Task.Run(() => fixture.StateStore(resetScope).ResetAsync(new LocalCredentialResetRequest(
                Guid.CreateVersion7(), fixture.Receipt.InitiatingApplicationUserId, fixture.Receipt.LocalSubjectId,
                fixture.Receipt.OperationId, consumed.OperationStamp, "Serialized mirror reset"), fixture.CancellationToken), fixture.CancellationToken);
            await observer.Started.WaitAsync(fixture.CancellationToken);
        }
        finally { release.TrySetResult(); }
        await Assert.That(await sync.WaitAsync(fixture.CancellationToken)).IsTrue();
        LocalCredentialResetResult result = await reset!.WaitAsync(fixture.CancellationToken);
        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        await Assert.That(await fixture.PasswordIsValidAsync(result.TemporaryPassword!)).IsTrue();
        int retries = 0;
        await Assert.That(await Store(fixture, synchronization).ExecuteSynchronizationAsync(new LocalIdentityLifecycleConsumption(link.Operation, link.Token),
            (_, _) => { retries++; return Task.FromResult(true); }, fixture.CancellationToken)).IsFalse();
        await Assert.That(retries).IsEqualTo(0);
        await Assert.That(await application.Users.AsNoTracking().Where(row => row.Id == link.Operation.LocalSubjectId)
            .Select(row => row.Pii!.Email).SingleAsync(fixture.CancellationToken)).IsEqualTo(link.Address);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task FreshOperationBudgetDoesNotChargeLiveAnonymousRepeats(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleRequest request = await RequestAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecycleStore store = Store(fixture, scope);
        for (int index = 0; index < 3; index++)
        {
            LocalIdentityLifecyclePointer? first = await store.BeginAsync(request, fixture.CancellationToken);
            await Assert.That(first).IsNotNull();
            await Assert.That(await store.BeginAsync(request, fixture.CancellationToken)).IsEqualTo(first);
            fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        }
        await Assert.That(await store.BeginAsync(request, fixture.CancellationToken)).IsNull();
        await Assert.That(await fixture.Identity(scope).Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(3);
        fixture.Clock.Advance(TimeSpan.FromMinutes(15));
        await Assert.That(await store.BeginAsync(request, fixture.CancellationToken)).IsNotNull();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentAnonymousIntakeConvergesWithoutRotatingSecurityAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleRequest request = await RequestAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        var before = await fixture.ReadAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LocalIdentityLifecyclePointer?> Run(TaskCompletionSource ready) => Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            LocalIdentityLifecycleStore store = Store(fixture, scope);
            ready.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await store.BeginAsync(request, fixture.CancellationToken);
        }, fixture.CancellationToken);
        Task<LocalIdentityLifecyclePointer?> first = Run(readyA);
        Task<LocalIdentityLifecyclePointer?> second = Run(readyB);
        await Task.WhenAll(readyA.Task, readyB.Task).WaitAsync(fixture.CancellationToken);
        start.SetResult();
        LocalIdentityLifecyclePointer?[] results = await Task.WhenAll(first, second).WaitAsync(fixture.CancellationToken);
        await Assert.That(results[0]).IsNotNull();
        await Assert.That(results[1]).IsEqualTo(results[0]);
        var after = await fixture.ReadAsync();
        await Assert.That(after.SecurityStamp == before.SecurityStamp).IsTrue();
        await Assert.That(after.TokenValue == before.TokenValue).IsTrue();
        await using AsyncServiceScope read = fixture.Provider.CreateAsyncScope();
        await Assert.That(await fixture.Identity(read).Set<LocalIdentityLifecycleOperation>().CountAsync(fixture.CancellationToken)).IsEqualTo(1);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentRecoveryAndSupervisedResetCannotResurrectConsumedAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await ReadyAsync(fixture);
        LocalIdentityLifecycleTransport link = await BeginAsync(fixture, LocalIdentityLifecyclePurpose.PasswordRecovery);
        var before = await fixture.ReadAsync();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyConsume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyReset = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<LocalIdentityLifecycleResult> consume = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            LocalIdentityLifecycleStore store = Store(fixture, scope);
            readyConsume.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await store.ConsumeAsync(new LocalIdentityLifecycleConsumption(link.Operation, link.Token, NewPassword()), fixture.CancellationToken);
        }, fixture.CancellationToken);
        Task<LocalCredentialResetResult> reset = Task.Run(async () =>
        {
            await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
            LocalIdentityCredentialStateStore store = fixture.StateStore(scope);
            readyReset.SetResult();
            await start.Task.WaitAsync(fixture.CancellationToken);
            return await store.ResetAsync(new LocalCredentialResetRequest(Guid.CreateVersion7(), fixture.Receipt.InitiatingApplicationUserId,
                fixture.Receipt.LocalSubjectId, fixture.Receipt.OperationId, before.OperationStamp, "Recovery versus supervised reset"), fixture.CancellationToken);
        }, fixture.CancellationToken);
        await Task.WhenAll(readyConsume.Task, readyReset.Task).WaitAsync(fixture.CancellationToken);
        start.SetResult();
        await Task.WhenAll(consume, reset).WaitAsync(fixture.CancellationToken);
        await Assert.That((await consume).Outcome is LocalIdentityLifecycleOutcome.Consumed or LocalIdentityLifecycleOutcome.Invalid or LocalIdentityLifecycleOutcome.Conflict).IsTrue();
        LocalCredentialResetResult result = await reset;
        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        await Assert.That(await fixture.PasswordIsValidAsync(result.TemporaryPassword!)).IsTrue();
        await Assert.That((await ConsumeAsync(fixture, link.Operation, link.Token, NewPassword())).Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Invalid);
        await using AsyncServiceScope retry = fixture.Provider.CreateAsyncScope();
        int callbacks = 0;
        await Assert.That(await Store(fixture, retry).ExecuteSynchronizationAsync(link.Operation,
            (_, _) => { callbacks++; return Task.FromResult(true); }, fixture.CancellationToken)).IsFalse();
        await Assert.That(callbacks).IsEqualTo(0);
    }

    private static async Task WriteMirrorAsync(ExploreDbContext application, LocalIdentityLifecycleSynchronization receipt, CancellationToken ct)
    {
        await Assert.That(application.Database.CurrentTransaction).IsNotNull();
        var user = await application.Users.Include(row => row.Pii).SingleAsync(row => row.Id == receipt.ApplicationUserId, ct);
        user.Email = receipt.Email!; user.EmailVerified = receipt.EmailVerified;
        await application.SaveChangesAsync(ct);
    }

    private sealed class TransactionAttemptObserver : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _target;
        internal Task Started => _started.Task;
        internal void Arm(DbContext target) => _target = target;
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _target)) _started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private static async Task ResetAsync(Fixture fixture)
    {
        var before = await fixture.ReadAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalCredentialResetResult reset = await fixture.StateStore(scope).ResetAsync(new LocalCredentialResetRequest(
            Guid.CreateVersion7(), fixture.Receipt.InitiatingApplicationUserId, fixture.Receipt.LocalSubjectId,
            fixture.Receipt.OperationId, before.OperationStamp, "Native lifecycle race"), fixture.CancellationToken);
        await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
    }

    private static async Task SetVerifiedAsync(Fixture fixture, bool verified)
    {
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        await fixture.Identity(scope).Set<LocalIdentityUser>().Where(user => user.Id == fixture.Receipt.LocalSubjectId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailConfirmed, verified), fixture.CancellationToken);
    }

    private static async Task<LocalIdentityLifecycleTransport> BeginAsync(Fixture fixture, LocalIdentityLifecyclePurpose purpose)
    {
        LocalIdentityLifecycleRequest request = await RequestAsync(fixture, purpose,
            purpose == LocalIdentityLifecyclePurpose.EmailChange ? $"changed-{Guid.CreateVersion7():N}@example.test" : null);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityLifecyclePointer? pointer = await Store(fixture, scope).BeginAsync(request, fixture.CancellationToken);
        await Assert.That(pointer).IsNotNull();
        LocalIdentityLifecycleTransport? link = await Store(fixture, scope).IssueTransportTokenAsync(pointer!, fixture.CancellationToken);
        await Assert.That(link).IsNotNull();
        return link!;
    }

    private static async Task<LocalIdentityLifecycleResult> ConsumeAsync(Fixture fixture, LocalIdentityLifecyclePointer operation, string token, string? password = null)
    {
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        return await Store(fixture, scope).ConsumeAsync(new LocalIdentityLifecycleConsumption(operation, token, password), fixture.CancellationToken);
    }

    private static LocalIdentityLifecycleStore Store(Fixture fixture, AsyncServiceScope scope) => new(
        fixture.Identity(scope), scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
        scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Clock, fixture.StateStore(scope));

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    private static async Task<string> ReadyAsync(Fixture fixture)
    {
        string password = NewPassword();
        await Assert.That(await fixture.ReplaceAsync(fixture.Authority(await fixture.ReadAsync()), password))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        return password;
    }

    private static async Task<LocalIdentityLifecycleRequest> RequestAsync(Fixture fixture, LocalIdentityLifecyclePurpose purpose,
        string? address = null)
    {
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityUser user = await fixture.Identity(scope).Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        return new LocalIdentityLifecycleRequest(user.Id, fixture.Receipt.PersonalActorId, fixture.Receipt.ExternalLoginId,
            purpose, address ?? user.Email!, purpose == LocalIdentityLifecyclePurpose.EmailChange ? user.SecurityStamp : null);
    }
}
