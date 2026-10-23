using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalAdministrationCustodyTests
{
    [Test]
    public async Task BindingExpiryPreservesPendingIdentityForANewAuthorizedRecovery()
    {
        Guid operationId = Guid.CreateVersion7();
        var barrier = new TerminalReadBarrier
        {
            Select = (context, token) => context.Set<LocalIdentityCredentialOperation>()
                .AnyAsync(operation => operation.Id == operationId
                    && context.UserExternalLogins.Any(login => login.Id == operation.ExternalLoginId), token)
        };
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: barrier.Decorate);
        await GrantAdministrator(fixture);
        var command = new CreateLocalIdentityCommand(operationId, $"{Guid.CreateVersion7():N}@custody.test", "Pending", "Owner");
        await using (var request = new Request(fixture.Host.Services))
        {
            await request.Authenticate(await fixture.SignInAsync());
            var handler = ActivatorUtilities.CreateInstance<CreateLocalIdentityCommandHandler>(request.Services);
            var pending = handler.ExecuteAsync(command);
            try
            {
                await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
                if (!barrier.Read.Task.IsCompleted) await pending;
                await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
                fixture.Clock.Advance(TimeSpan.FromDays(2));
            }
            finally { barrier.Release.TrySetResult(); }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await pending);
        }
        LocalIdentityCredentialOperation stored;
        await using (var observer = fixture.Native.CreateDatabase())
        {
            stored = await observer.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == operationId);
            await Assert.That(stored.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
            await Assert.That(await observer.Users.AsNoTracking().AnyAsync(row => row.Id == stored.LocalSubjectId)).IsFalse();
            await Assert.That(await observer.Actors.AsNoTracking().AnyAsync(row => row.Id == stored.PersonalActorId)).IsFalse();
            await Assert.That(await observer.UserExternalLogins.AsNoTracking()
                .AnyAsync(row => row.Id == stored.ExternalLoginId)).IsFalse();
        }
        await using (var recovery = new Request(fixture.Host.Services))
        {
            await recovery.Authenticate(await fixture.SignInAsync());
            var handler = ActivatorUtilities.CreateInstance<ReconcileLocalCredentialOperationCommandHandler>(recovery.Services);
            var result = await handler.ExecuteAsync(new(operationId));
            await Assert.That(result.IsSuccess).IsTrue();
        }
        await using var after = fixture.Native.CreateDatabase();
        await Assert.That(await after.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .Where(row => row.Id == operationId).Select(row => row.Stage).SingleAsync())
            .IsEqualTo(LocalCredentialOperationStage.ChangeRequired);
        await Assert.That(await after.UserExternalLogins.AsNoTracking().AnyAsync(row =>
            row.Id == stored.ExternalLoginId && row.UserId == stored.LocalSubjectId)).IsTrue();
        await Assert.That(await after.Actors.AsNoTracking().AnyAsync(row =>
            row.Id == stored.PersonalActorId && row.UserId == stored.LocalSubjectId)).IsTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task LostPhysicalAcknowledgementNeverBecomesReplayAuthority(bool commit)
    {
        var loss = new LostAcknowledgement(commit);
        var barrier = new TerminalReadBarrier();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: loss, configureServices: barrier.Decorate);
        await GrantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
            $"{Guid.CreateVersion7():N}@custody.test", "Unknown", "Owner");
        var original = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId, default);
        var administration = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
        loss.Armed = true;
        Task<LocalCredentialCreateResult> pending = administration.CreatePendingAsync(original, command, default);
        try
        {
            await Task.WhenAny(barrier.Read.Task, pending).WaitAsync(TimeSpan.FromSeconds(30));
            if (!barrier.Read.Task.IsCompleted) await pending;
            await barrier.Read.Task.WaitAsync(TimeSpan.FromSeconds(30));
            if (!commit) fixture.Clock.Advance(TimeSpan.FromDays(2));
        }
        finally { barrier.Release.TrySetResult(); }
        await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(async () => await pending);
        await loss.Settled.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await using (var observer = fixture.Native.CreateDatabase())
        {
            await Assert.That(await observer.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .AnyAsync(row => row.Id == command.OperationId)).IsEqualTo(commit);
            var subjects = await observer.Set<LocalIdentityUser>().AsNoTracking()
                .Where(row => row.Email == command.Email).Select(row => row.Id).ToArrayAsync();
            await Assert.That(subjects.Length).IsEqualTo(commit ? 1 : 0);
            await Assert.That(await observer.Users.AsNoTracking().AnyAsync(row => subjects.Contains(row.Id))).IsFalse();
        }
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            administration.CreatePendingAsync(original, command, default));
    }

    [Test]
    public async Task ExternalIdentityAdministrationFailsBeforeAnyLifecycleEffect()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        await GrantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
            $"{Guid.CreateVersion7():N}@custody.test", "External", "Owner");
        var handler = ActivatorUtilities.CreateInstance<CreateLocalIdentityCommandHandler>(request.Services);
        await Assert.ThrowsAsync<NotSupportedException>(() => handler.ExecuteAsync(command));
        await using var identity = fixture.Native.CreateIdentityDatabase();
        await Assert.That(await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .AnyAsync(row => row.Id == command.OperationId)).IsFalse();
        await Assert.That(await identity.Set<LocalIdentityUser>().AsNoTracking()
            .AnyAsync(row => row.Email == command.Email)).IsFalse();
    }

    private sealed class LostAcknowledgement(bool commit) : DbTransactionInterceptor
    {
        internal bool Armed { get; set; }
        internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || !commit || eventData.Context is not ExploreDbContext) return Task.CompletedTask;
            Armed = false;
            Settled.TrySetResult();
            throw new InvalidOperationException("Lost original Local commit acknowledgement.");
        }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (!Armed || commit || eventData.Context is not ExploreDbContext) return Task.CompletedTask;
            Armed = false;
            Settled.TrySetResult();
            throw new InvalidOperationException("Lost original Local rollback acknowledgement.");
        }
    }
}
