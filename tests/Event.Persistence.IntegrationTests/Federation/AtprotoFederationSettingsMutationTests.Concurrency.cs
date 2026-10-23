using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class AtprotoFederationSettingsMutationTests
{
    [Test]
    public async Task CommittedParentLockRejectsTheWaitingTenantWriterBeforeItsCallback()
    {
        await using var scenario = await CreateScenarioAsync();
        await SeedUnmovedProfileAsync(scenario);
        // Membership creation has its own disclosure effects; finish it before capturing command outcomes.
        await scenario.Authentication.RunAsync(scenario.Tenant, _ => Task.FromResult(true));
        await scenario.Authentication.RunAsync(null, _ => Task.FromResult(true));
        var before = await ReadAsync(scenario);
        var privateBefore = await ReadProfileAsync(scenario);
        await Assert.That(before.Instance.IsLocked).IsFalse();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var parentWritten = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var childPrepared = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseParent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int childCallbackAdmitted = 0;
        bool operationsCompleted = false;
        Task child = Task.CompletedTask;

        Task parent = Task.Run(() => scenario.Authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(
                new LockSettingCommand { Key = Key, Scope = SettingScope.Instance }, deadline.Token);
            return await ExecuteAsync(session, prepared, [Key], async ownerToken =>
            {
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerToken, deadline.Token);
                var transaction = session.Context.Database.CurrentTransaction;
                var connection = (NpgsqlConnection)session.Context.Database.GetDbConnection();
                await Assert.That(transaction).IsNotNull();
                int backend = connection.ProcessID;
                var effect = new HumanSettingsMutation(HumanSettingsOperation.InstanceSettings, SettingScope.Instance,
                    null, null, Key, HumanSettingsMutationKind.SetLock, IsLocked: true);
                var changed = await Writer(session).ApplyCurrentAsync(effect, cancellation.Token);
                await Assert.That(changed.Applied).IsTrue();
                await Assert.That(changed.PreviousLock).IsFalse();
                await Assert.That(changed.CurrentLock).IsTrue();
                await Assert.That(await session.Context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
                    .Where(row => row.SettingKey == Key).Select(row => row.IsLocked).SingleAsync(cancellation.Token)).IsTrue();
                parentWritten.TrySetResult(backend);

                await releaseParent.Task.WaitAsync(cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();

                await Assert.That(session.Context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
                await Assert.That(session.Context.Database.GetDbConnection()).IsSameReferenceAs(connection);
                await Assert.That(transaction!.GetDbTransaction().Connection).IsSameReferenceAs(connection);
                await Assert.That(connection.ProcessID).IsEqualTo(backend);
                await Assert.That(session.Context.TenantFilterTenantId).IsNull();
                return true;
            });
        }, cancellationToken: deadline.Token), deadline.Token);

        try
        {
            int parentBackend = await AwaitConcurrentSignalAsync(parentWritten.Task, parent, deadline.Token);
            child = Task.Run(() => scenario.Authentication.RunAsync(scenario.Tenant, async session =>
            {
                await session.Context.Database.OpenConnectionAsync(deadline.Token);
                var connection = (NpgsqlConnection)session.Context.Database.GetDbConnection();
                var prepared = await session.Preparation.PrepareAsync(
                    new UpdateSettingCommand { Key = Key, Value = "true", Scope = SettingScope.Tenant },
                    "true", deadline.Token);
                childPrepared.TrySetResult(connection.ProcessID);
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ExecuteAsync(session, prepared, [Key], async ownerToken =>
                {
                    Interlocked.Exchange(ref childCallbackAdmitted, 1);
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(ownerToken, deadline.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    return await Writer(session).ApplyCurrentAsync(
                        ValueEffect(SettingScope.Tenant, scenario.Tenant, Key, "true"), cancellation.Token);
                }));
                await Assert.That(session.Context.TenantFilterTenantId).IsEqualTo(scenario.Tenant);
                return true;
            }, cancellationToken: deadline.Token), deadline.Token);

            int childBackend = await AwaitConcurrentSignalAsync(childPrepared.Task, child, deadline.Token);
            await Assert.That(childBackend).IsNotEqualTo(parentBackend);
            await WaitForAdvisoryBlockerAsync(parentBackend, childBackend, parent, child, deadline.Token);
            await Assert.That(Volatile.Read(ref childCallbackAdmitted)).IsEqualTo(0);
            // The parent is persisted in its owner transaction but remains invisible to the child's preparation.
            await Assert.That((await ReadAsync(scenario)).Instance).IsEqualTo(before.Instance);
            releaseParent.TrySetResult(true);
            await Task.WhenAll(parent, child).WaitAsync(deadline.Token);
            operationsCompleted = true;
        }
        finally
        {
            await deadline.CancelAsync();
            releaseParent.TrySetResult(true);
            // Release the real owner even on assertion/cancellation failures, then drain both authenticated scopes.
            // Native commands retain their bounded provider timeout; duplicate cleanup faults must not hide the first failure.
            try { await Task.WhenAll(parent, child); }
            catch when (!operationsCompleted) { }
        }

        await Assert.That(Volatile.Read(ref childCallbackAdmitted)).IsEqualTo(0);
        var after = await ReadAsync(scenario);
        await Assert.That(after.Instance.UpdatedAt).IsNotNull();
        await Assert.That(after.Instance).IsEqualTo(before.Instance with
        {
            IsLocked = true,
            UpdatedAt = after.Instance.UpdatedAt,
            UpdatedBy = scenario.Authentication.ActorId
        });
        await Assert.That(after.Target).IsEqualTo(before.Target);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await AssertEpochsAsync(before, after, root: true);
        await AssertLegacyUnchangedAsync(before, after);
        await Assert.That(await ReadProfileAsync(scenario)).IsEqualTo(privateBefore);
    }

    private static async Task<int> AwaitConcurrentSignalAsync(Task<int> signal, Task operation, CancellationToken token)
    {
        if (await Task.WhenAny(signal, operation).WaitAsync(token) == operation)
        {
            await operation;
            throw new InvalidOperationException("The authenticated operation ended before reaching its concurrency checkpoint.");
        }
        return await signal.WaitAsync(token);
    }

    private async Task WaitForAdvisoryBlockerAsync(int parentBackend, int childBackend,
        Task parent, Task child, CancellationToken token)
    {
        await using var observer = OpenMigrator();
        while (true)
        {
            bool blocked = await observer.Database.SqlQueryRaw<bool>("""
                SELECT @parent=ANY(pg_catalog.pg_blocking_pids(@child)) AND EXISTS (
                    SELECT 1 FROM pg_catalog.pg_locks waiting JOIN pg_catalog.pg_locks holding
                        ON holding.locktype=waiting.locktype
                        AND holding.database IS NOT DISTINCT FROM waiting.database
                        AND holding.classid IS NOT DISTINCT FROM waiting.classid
                        AND holding.objid IS NOT DISTINCT FROM waiting.objid
                        AND holding.objsubid IS NOT DISTINCT FROM waiting.objsubid
                    WHERE waiting.pid=@child AND waiting.locktype='advisory' AND NOT waiting.granted
                        AND holding.pid=@parent AND holding.granted) AS "Value"
                """, new NpgsqlParameter("parent", parentBackend), new NpgsqlParameter("child", childBackend))
                .SingleAsync(token);
            if (blocked) return;
            if (parent.IsCompleted || child.IsCompleted)
            {
                if (parent.IsCompleted) await parent;
                if (child.IsCompleted) await child;
                throw new InvalidOperationException("The commands did not contend on the cooperative setting-key advisory lock.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(25), token);
        }
    }
}
