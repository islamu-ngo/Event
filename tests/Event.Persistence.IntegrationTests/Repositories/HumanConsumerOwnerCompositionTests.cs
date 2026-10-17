using System.Data;
using System.Security.Claims;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PolicyMutationExecutionScopeSqliteFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PolicyMutationExecutionScopeSqlite")]
public sealed class HumanConsumerOwnerCompositionTests(PolicyMutationExecutionScopeSqliteFixture fixture)
{
    [Test]
    public async Task OrdinaryPreparationResolvesRealProviderBindingWithoutGuidFallback()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", fixture.AdministratorId.ToString("D")), new Claim("auth_provider", "local"),
                new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
        var account = principal.GetProviderIdentity()!.AccountKey;
        await using var global = await caller.ServiceProvider.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, default);
        var context = global.Services.GetRequiredService<ExploreDbContext>();
        var binding = new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.AdministratorId,
            User = null!,
            AuthenticationProviderId = (int)account.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = account.Value,
            CreatedAt = DateTime.UtcNow
        };
        context.UserExternalLogins.Add(binding);
        await context.SaveChangesAsync();
        try
        {
            caller.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext!.User = principal;
            var preparation = caller.ServiceProvider.GetRequiredService<IHumanCommitScopePreparation>();
            await preparation.PrepareAsync(default);
            await Assert.That(preparation.Subject!.PrincipalId).IsEqualTo(fixture.AdministratorId);
            await Assert.That(preparation.Subject.AccountKey).IsEqualTo(account);
            await context.UserExternalLogins.Where(row => row.Id == binding.Id).ExecuteDeleteAsync();
            await using var fresh = fixture.OpenCaller();
            var freshPreparation = fresh.ServiceProvider.GetRequiredService<IHumanCommitScopePreparation>();

            await freshPreparation.PrepareAsync(default);

            await Assert.That(freshPreparation.Subject).IsNull();
            await Assert.That(fresh.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope).IsNull();
        }
        finally
        {
            await context.UserExternalLogins.Where(row => row.Id == binding.Id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task OrdinaryPreparationUsesOriginalOwnerAndRetainsSubjectAcrossTransactionAndBorrow()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var services = caller.ServiceProvider;
        var context = services.GetRequiredService<ExploreDbContext>();
        var connection = context.Database.GetDbConnection();
        var preparation = services.GetRequiredService<IHumanCommitScopePreparation>();
        await preparation.PrepareAsync(default);
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        var subject = lease.HumanScope!.Subject;
        await Assert.That(subject.PrincipalId).IsEqualTo(fixture.AdministratorId);
        await Assert.That(lease.Context).IsSameReferenceAs(context);
        var authority = services.GetRequiredService<ITenantAdministratorCommitAuthority>();

        services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User = new ClaimsPrincipal();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await preparation.PrepareAsync(default);
        await using var borrowed = await services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(fixture.CallerTenantId), default);

        await Assert.That(borrowed.Services).IsSameReferenceAs(services);
        await Assert.That(lease.HumanScope.Subject).IsSameReferenceAs(subject);
        await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connection);
        await Assert.That(await authority.IsTenantAdministratorForCommitAsync(
            fixture.AdministratorId, fixture.CallerTenantId, default)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task PolicySourceSetUsesSelectedOwnerAndRejectsReplacementTransaction()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var services = caller.ServiceProvider;
        await services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default);
        var context = services.GetRequiredService<ExploreDbContext>();
        var fence = services.GetRequiredService<IPolicySourceSetFence>();
        IPolicySourceSetLease lease;
        await using (var original = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            lease = await fence.AcquireAsync();
            await lease.ValidateAsync();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(original);
            await original.RollbackAsync();
        }
        await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lease.ValidateAsync());
        await replacement.RollbackAsync();
    }

    [Test]
    public async Task OrdinaryOwnerCannotAcquireHumanBindingAfterTransactionStarts()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var context = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await caller.ServiceProvider.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(default));

        await Assert.That(caller.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }
}
