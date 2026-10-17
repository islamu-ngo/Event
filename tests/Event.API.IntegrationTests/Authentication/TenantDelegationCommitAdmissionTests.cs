using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class TenantDelegationCommitAdmissionTests
{
    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "grant")]
    [Arguments(false, "credential")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "grant")]
    [Arguments(true, "credential")]
    public async Task DelegationRequiresOriginalGrantAndCredentialAfterItsRealPatch(bool smtp, string change)
    {
        bool observed = false;
        void Configure(IServiceCollection services) => services.Replace(
            ServiceDescriptor.Scoped<IUnitOfWork>(provider =>
            {
                var context = provider.GetRequiredService<ExploreDbContext>();
                return new AfterOriginalBody(new EfCoreUnitOfWork(context), async token =>
                {
                    observed = true;
                    var subject = provider.GetRequiredService<IHumanCommitScopePreparation>().Subject!;
                    if (change == "grant")
                        await context.PlatformUserRoles.Where(row => row.UserId == subject.PrincipalId)
                            .ExecuteDeleteAsync(token);
                    else if (change == "credential")
                    {
                        var lifetime = provider.GetRequiredService<ExploreDbContextExecutionLease>().HumanLifetime!;
                        var clock = (Fixtures.LocalIdentityLifecycleHttpFixture.LifecycleClock)
                            provider.GetRequiredService<TimeProvider>();
                        clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1) - clock.GetUtcNow());
                    }
                });
            }));

        async Task PatchAsync(IServiceProvider services)
        {
            var operation = services.GetRequiredService<ICommandHandler<UpdateTenantDelegationSettingsCommand,
                BaseCommandResponse<Guid>>>().ExecuteAsync(new()
                {
                    // A caller-supplied audit identity is never the original authenticated actor.
                    UserId = Guid.CreateVersion7(),
                    Patch = new PatchTenantDelegationSettingsDto
                    {
                        LockTenantSmtp = smtp ? OptionalUpdate<bool>.Set(false) : default,
                        AllowTenantSelfServiceRegistration = OptionalUpdate<bool>.Set(true)
                    }
                }, CancellationToken.None);
            if (change == "unchanged")
                await Assert.That((await operation).IsSuccess).IsTrue();
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation);
            await Assert.That(observed).IsTrue();
            await Assert.That(services.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction).IsNull();
        }

        async Task CheckAsync(ExploreDbContext context, Guid actor)
        {
            var setting = await context.SystemSettings.AsNoTracking().SingleAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Tenants.SelfServiceRegistration);
            await Assert.That(setting.Value).IsEqualTo(change == "unchanged" ? "true" : "false");
            if (change == "unchanged")
                await Assert.That(setting.UpdatedBy).IsEqualTo(actor);
            await Assert.That(await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == actor)).IsTrue();
        }

        if (smtp)
        {
            await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(configureServices: Configure);
            await using (var seed = fixture.CreateIndependentContext())
                await SeedAsync(seed);
            await fixture.ExecuteAsync(tenantOwner: false, PatchAsync);
            await using var observer = fixture.CreateIndependentContext();
            await CheckAsync(observer, fixture.Subject.PrincipalId);
        }
        else
        {
            await using var fixture = await TenantDelegationAuthenticationFixture.CreateAsync(configureServices: Configure);
            await using (var seed = fixture.CreateIndependentContext())
                await SeedAsync(seed);
            await using var selected = await fixture.SelectAsync();
            await PatchAsync(selected.Services);
            await using var observer = fixture.CreateIndependentContext();
            await CheckAsync(observer, fixture.UserId);
            await Assert.That(fixture.Published.Items.Count).IsEqualTo(change == "unchanged" ? 1 : 0);
        }
    }

    private static async Task SeedAsync(ExploreDbContext context)
    {
        await TenantDelegationAuthenticationFixture.SeedSettingAsync(context,
            GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"");
        await TenantDelegationAuthenticationFixture.SeedSettingAsync(context,
            GovernanceSettingKeys.Tenants.SelfServiceRegistration, "false");
    }

    /// <summary>Observes the real callback before the real owner's save, admission and settlement.</summary>
    private sealed class AfterOriginalBody(EfCoreUnitOfWork inner, Func<CancellationToken, Task> afterBody)
        : IUnitOfWork, ISerializableAttemptOwner
    {
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) =>
            ((ISerializableAttemptOwner)inner).ExecuteAsync(async token =>
            {
                var result = await operation(token);
                await afterBody(token);
                return result;
            }, cancellationToken);

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) =>
            inner.ExecuteInTransactionAsync(operation, ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            inner.ExecuteInTransactionAsync(operation, ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            inner.ExecuteSerializableAsync(operation, ct);
        public Task<T> ExecuteReadCommittedAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            inner.ExecuteReadCommittedAsync(operation, ct);
        public Task<T> ExecuteBootstrapConvergenceAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ((IUnitOfWork)inner).ExecuteBootstrapConvergenceAsync(operation, ct);
    }
}
