using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class StorageAccountingOriginalOwnerTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("binding-revoked")]
    [Arguments("administrator-revoked")]
    [Arguments("binding-revoked-in-owner")]
    [Arguments("administrator-revoked-in-owner")]
    [Arguments("principal-replaced")]
    [Arguments("expired-before-begin")]
    [Arguments("terminal-expiry")]
    [Arguments("later-repair-failure")]
    public async Task ActualHumanAccountingRetainsOriginalSourcesAndRollsBackWholeRepair(string change)
    {
        var gate = new AccountingGate();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: gate);
        Guid grant;
        CounterSnapshot[] before;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            grant = await SeedAccountingAdministratorAsync(seed, fixture.Binding.LocalSubjectId);
            await SeedAccountingAsync(seed);
            before = await CounterSnapshotsAsync(seed);
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(RegisterAccounting));
        await using var request = await AccountingRequest.CreateAsync(host.Services, await fixture.SignInAsync(), null);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        gate.Owner = context;
        gate.BeforeStart = async token =>
        {
            await using var independent = fixture.Native.CreateDatabase();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
            if (change == "expired-before-begin")
            {
                var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                    .HumanLifetime!;
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
            }
        };
        gate.AfterRepair = () =>
        {
            if (change == "terminal-expiry")
            {
                var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                    .HumanLifetime!;
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                        - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
            }
            if (change == "later-repair-failure" && gate.Repairs == 2)
                throw new InvalidOperationException("The second accounting scope failed after its write.");
        };
        gate.AfterRepairAsync = async token =>
        {
            if (gate.Repairs != 2) return;
            if (change == "binding-revoked-in-owner")
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked-in-owner")
                await context.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
        };
        var execution = ActivatorUtilities.CreateInstance<RecalculateInstanceStorageUsageCommandHandler>(services)
            .ExecuteAsync(new RecalculateInstanceStorageUsageCommand { UserId = fixture.Binding.LocalSubjectId },
                CancellationToken.None);
        if (change == "unchanged")
        {
            var result = await execution;
            await Assert.That(result.UsedBytes).IsEqualTo(41L);
            await Assert.That(result.ObjectCount).IsEqualTo(1L);
        }
        else if (change == "later-repair-failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(gate.Starts).IsEqualTo(1);
        await Assert.That(gate.Repairs).IsEqualTo(change is "binding-revoked" or "administrator-revoked"
            or "principal-replaced" or "expired-before-begin" ? 0 : 2);
        await using var observer = fixture.Native.CreateDatabase();
        await AssertAccountingAsync(observer, change == "unchanged");
        if (change != "unchanged")
            await Assert.That(await CounterSnapshotsAsync(observer)).IsEquivalentTo(before);
        if (change == "binding-revoked-in-owner")
            await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == fixture.Binding.ExternalLoginId)).IsTrue();
        if (change == "administrator-revoked-in-owner")
            await Assert.That(await observer.PlatformUserRoles.AnyAsync(row => row.Id == grant)).IsTrue();
    }

    private static async Task<Guid> SeedAccountingAdministratorAsync(ExploreDbContext seed, Guid actor)
    {
        int role = await seed.Roles.Where(row => row.MasterCode == "platform.admin"
            && row.RoleScopeId == (int)RoleScopeEnum.Platform).Select(row => row.Id).SingleAsync();
        var grant = new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = actor,
            User = null!,
            RoleId = role,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        };
        seed.PlatformUserRoles.Add(grant);
        await seed.SaveChangesAsync();
        return grant.Id;
    }

    private static async Task SeedAccountingAsync(ExploreDbContext context)
    {
        context.StorageUsageCounters.Add(new StorageUsageCounter
        {
            TenantId = PlatformDefaults.DefaultTenantId,
            Provider = StorageProviders.S3Compatible,
            UsedBytes = 7,
            ObjectCount = 3,
            ReservedBytes = 11,
            QuarantinedBytes = 13
        });
        var binding = StorageProviderBinding.Local(Path.Combine(
            Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), $"accounting-{Guid.CreateVersion7():N}"));
        context.StorageProviderBindings.Add(binding);
        context.StorageObjects.Add(new StorageObject
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            FileTypeId = await context.FileTypes.Select(row => row.Id).FirstAsync(),
            FileType = null!,
            Provider = StorageProviders.Local,
            StorageProviderBindingId = binding.Id,
            FullName = "held-accounting.pdf",
            SafeDisplayName = "held-accounting.pdf",
            Extension = ".pdf",
            ObjectKey = "accounting/held.pdf",
            Size = 41,
            Purpose = StorageObjectPurposes.Document,
            Visibility = StorageObjectVisibilities.PrivateOwner,
            LifecycleState = StorageObjectLifecycleStates.DeleteRequested,
            IsDeleted = true
        });
        await context.SaveChangesAsync();
    }

    private static void RegisterAccounting(IServiceCollection services)
    {
        services.AddStorageAccountingOriginalAuthority();
        services.AddStorageAccountingOriginalOwner();
    }

    private static async Task<AccountingRequest> CreateSetupAccountingRequestAsync(
        LocalAdmissionWebApplicationFactory native, WebApplicationFactory<Program> host)
    {
        Guid providerSubject = Guid.CreateVersion7();
        await using (var seed = native.CreateDatabase())
        {
            Guid actor = await seed.Users.Select(row => row.Id).FirstAsync();
            seed.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = actor,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = providerSubject.ToString("D"),
                CreatedAt = DateTime.UtcNow
            });
            await seed.SaveChangesAsync();
        }
        // Real signature validation against the fixture's native static Keycloak metadata.
        // The ticket authenticates a non-admin; the secret and pending state authorize setup.
        string token = native.CreateExternalProviderToken(providerSubject, "accounting-setup@example.test", true);
        return await AccountingRequest.CreateAsync(host.Services, token, native.SetupSecret);
    }

    private static Task<CounterSnapshot[]> CounterSnapshotsAsync(ExploreDbContext context) =>
        context.StorageUsageCounters.AsNoTracking().Select(row => new CounterSnapshot(
            row.Id, row.TenantId, row.Provider, row.UsedBytes, row.ReservedBytes, row.QuarantinedBytes,
            row.ObjectCount, row.ConcurrencyStamp, row.CreatedAt, row.UpdatedAt, row.LastRecalculatedAt,
            row.CreatedBy, row.UpdatedBy)).ToArrayAsync();

    private sealed record CounterSnapshot(Guid Id, Guid TenantId, string Provider, long Used, long Reserved,
        long Quarantined, long Count, Guid Stamp, DateTime Created, DateTime? Updated, DateTime? Recalculated,
        Guid? CreatedBy, Guid? UpdatedBy);

    private static async Task AssertAccountingAsync(ExploreDbContext context, bool committed)
    {
        var counters = await context.StorageUsageCounters.AsNoTracking().OrderBy(row => row.Provider).ToArrayAsync();
        await Assert.That(counters.Length).IsEqualTo(committed ? 2 : 1);
        var stale = counters.Single(row => row.Provider == StorageProviders.S3Compatible);
        await Assert.That(stale.UsedBytes).IsEqualTo(committed ? 0L : 7L);
        await Assert.That(stale.ReservedBytes).IsEqualTo(committed ? 0L : 11L);
        await Assert.That(stale.QuarantinedBytes).IsEqualTo(committed ? 0L : 13L);
        await Assert.That(stale.ObjectCount).IsEqualTo(committed ? 0L : 3L);
        await Assert.That(stale.LastRecalculatedAt.HasValue).IsEqualTo(committed);
        var held = await context.StorageObjects.IgnoreQueryFilters().SingleAsync(row => row.ObjectKey == "accounting/held.pdf");
        await Assert.That(held.IsDeleted).IsTrue();
        await Assert.That(held.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.DeleteRequested);
        await Assert.That(held.Size).IsEqualTo(41L);
        await Assert.That(await context.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == held.Id)).IsFalse();
        if (committed)
        {
            var local = counters.Single(row => row.Provider == StorageProviders.Local);
            await Assert.That(local.UsedBytes).IsEqualTo(41L);
            await Assert.That(local.ObjectCount).IsEqualTo(1L);
        }
    }

    private sealed class AccountingRequest(AsyncServiceScope scope, IHttpContextAccessor accessor, DefaultHttpContext context)
        : IAsyncDisposable
    {
        internal IServiceProvider Services { get { accessor.HttpContext = context; return scope.ServiceProvider; } }
        internal DefaultHttpContext Context => context;

        internal static async Task<AccountingRequest> CreateAsync(IServiceProvider services, string? bearer, string? secret)
        {
            var scope = services.CreateAsyncScope();
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/instance/settings/storage/usage/recalculate";
            accessor.HttpContext = context;
            if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
            if (secret is not null) context.Request.Headers["X-Setup-Secret"] = secret;
            var authentication = await context.AuthenticateAsync();
            if (!authentication.Succeeded)
            {
                accessor.HttpContext = null;
                await scope.DisposeAsync();
                throw new InvalidOperationException("The actual accounting request did not authenticate.");
            }
            context.User = authentication.Principal!;
            return new(scope, accessor, context);
        }

        public async ValueTask DisposeAsync()
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
        }
    }

    private sealed class AccountingGate : DbCommandInterceptor, IDbTransactionInterceptor
    {
        internal ExploreDbContext? Owner { get; set; }
        internal Func<CancellationToken, Task>? BeforeStart { get; set; }
        internal Action? AfterRepair { get; set; }
        internal Func<CancellationToken, Task>? AfterRepairAsync { get; set; }
        internal int Starts { get; private set; }
        internal int Repairs { get; private set; }

        public async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, Owner))
            {
                Starts++;
                if (BeforeStart is { } callback)
                {
                    BeforeStart = null;
                    await callback(cancellationToken);
                }
            }
            return result;
        }

        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            // Observe the physical counter update, not a source read or a timing delay.
            if (ReferenceEquals(eventData.Context, Owner) && Owner?.Database.CurrentTransaction is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains("last_recalculated_at", StringComparison.OrdinalIgnoreCase))
            {
                Repairs++;
                AfterRepair?.Invoke();
                if (AfterRepairAsync is { } callback)
                    await callback(cancellationToken);
            }
            return result;
        }
    }
}
