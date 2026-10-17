using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Handlers.Queries;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageProbeOriginalActionTests
{
    [Test]
    [Arguments("before-begin-revocation")]
    [Arguments("terminal-provider-change")]
    [Arguments("terminal-expiry")]
    public async Task OriginalSourceTransactionsRejectAndRollBackBeforeRemoteWork(string change)
    {
        var gate = new ProbeTransactionGate();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: gate);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        remote.Owner = gate.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        gate.BeforeBegin = async token =>
        {
            if (change != "before-begin-revocation") return;
            await using var independent = fixture.Native.CreateDatabase();
            await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId).ExecuteDeleteAsync(token);
        };
        gate.OnSourceRead = () => selected.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(async token =>
        {
            if (change == "terminal-provider-change")
                await gate.Owner.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "\"foreign-bucket\""), token);
            if (change == "terminal-expiry")
            {
                var lifetime = selected.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - fixture.Clock.GetUtcNow());
            }
        }, _ => { });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(selected.Services)
                .QueryAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None));
        await Assert.That(remote.Puts).IsEqualTo(0);
        await Assert.That(remote.Deletes).IsEqualTo(0);
        await Assert.That(gate.Starts).IsEqualTo(1);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That((await observer.SystemSettings.SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Storage.BucketName)).Value).IsEqualTo("\"probe-bucket\"");
    }

    [Test]
    public async Task InstanceProbeCannotBorrowASelectedTenantOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.Services.GetRequiredService<IStorageProbeOriginalAction>()
            .ExecuteAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None));
        await Assert.That(remote.Puts).IsEqualTo(0);
    }

    [Test]
    public async Task IndependentLocalIdentityCannotInventOriginalNativeCustody()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await SeedProbeAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedProbeProviderAsync(seed);
        }
        var remote = new ProbeRemote();
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(remote.Register));
        await using var request = await ProbeRequest.CreateAsync(host.Services, await fixture.SignInAsync());
        await Assert.ThrowsAsync<NotSupportedException>(() =>
            ActivatorUtilities.CreateInstance<TestInstanceStorageProviderQueryHandler>(request.Services)
                .QueryAsync(new TestInstanceStorageProviderQuery { UserId = fixture.Binding.LocalSubjectId }, CancellationToken.None));
        await Assert.That(remote.Puts).IsEqualTo(0);
    }

    private sealed class ProbeTransactionGate : DbCommandInterceptor, IDbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Func<CancellationToken, Task>? BeforeBegin { get; set; }
        internal Action? OnSourceRead { get; set; }
        internal int Starts { get; private set; }

        public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner))
            {
                Starts++;
                if (BeforeBegin is { } callback)
                {
                    BeforeBegin = null;
                    await callback(cancellationToken);
                }
            }
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner) && Owner?.Database.CurrentTransaction is not null
                && OnSourceRead is { } callback)
            {
                OnSourceRead = null;
                callback();
            }
            return ValueTask.FromResult(result);
        }
    }
}
