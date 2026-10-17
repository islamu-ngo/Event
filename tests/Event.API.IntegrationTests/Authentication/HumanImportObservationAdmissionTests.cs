using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Authentication;
using Explore.Application.Exceptions;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.ConfigurationManifest;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed class HumanImportObservationAdmissionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ObservationReleasesOnlyItsOriginalPostDisposalCommitReceipt(bool counterfeit)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var context = fixture.Context;
        var member = await context.TenantUsers.SingleAsync(
            row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        var factory = fixture.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>();
        var boundary = new ObservationWitnessBoundary(
            ((IHumanConfigurationImportOwnerFactory)factory).GetImportSettlementBoundary(fixture.Lifetime),
            context, counterfeit);
        var admission = ActivatorUtilities.CreateInstance<HumanConfigurationImportAdmission>(
            fixture.Services, new ObservationWitnessServices(fixture.Services, factory, boundary));
        var observation = new HumanConfigurationImportObservation<string>(human.Subject!,
            ConfigurationImportTarget.ForTenant(fixture.TenantId), HumanConfigurationImportObservationAccess.Read,
            async (_, token) =>
            {
                await context.Tenants.Where(row => row.Id == fixture.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "Acknowledged observation"), token);
                return "Private observation";
            });
        var attempt = admission.ExecuteObservationAsync(observation, CancellationToken.None);
        if (counterfeit)
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        else
            await Assert.That(await attempt).IsEqualTo("Private observation");
        await Assert.That(boundary.ConsumedAfterDisposal).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync()).IsEqualTo("Acknowledged observation");
        await Assert.That(await observer.ConfigurationImportOperations.AnyAsync()).IsFalse();
    }

    private sealed class ObservationWitnessServices(IServiceProvider inner,
        IPolicyMutationExecutionScopeFactory factory, IOriginalOwnerSettlementBoundary boundary) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IPolicyMutationExecutionScopeFactory)
                ? new ObservationWitnessFactory(factory, boundary) : inner.GetService(serviceType);
    }

    private sealed class ObservationWitnessFactory(
        IPolicyMutationExecutionScopeFactory inner, IOriginalOwnerSettlementBoundary boundary)
        : IPolicyMutationExecutionScopeFactory, IHumanConfigurationImportOwnerFactory
    {
        public ValueTask<IPolicyMutationExecutionScope> CreateAsync(
            PolicyMutationScope selection, CancellationToken cancellationToken) =>
            inner.CreateAsync(selection, cancellationToken);
        public ValueTask<IPolicyMutationExecutionScope> CreateFreshImportOwnerAsync(
            HumanConfigurationImportAuthority original, CancellationToken cancellationToken) =>
            ((IHumanConfigurationImportOwnerFactory)inner).CreateFreshImportOwnerAsync(original, cancellationToken);
        public IOriginalOwnerSettlementBoundary GetImportSettlementBoundary(HumanAuthenticationLifetime lifetime) =>
            boundary;
        public IHumanConfigurationImportSourceReader GetImportSourceReader() =>
            ((IHumanConfigurationImportOwnerFactory)inner).GetImportSourceReader();
    }

    private sealed class ObservationWitnessBoundary(
        IOriginalOwnerSettlementBoundary inner, Explore.Persistence.ExploreDbContext context, bool counterfeit)
        : IOriginalOwnerSettlementBoundary
    {
        internal bool ConsumedAfterDisposal { get; private set; }
        public async Task<IOriginalOwnerSettlement<T>> ExecuteAsync<T>(object invocation,
            Func<CancellationToken, Task<OriginalOwnerDecision<T>>> operation,
            CancellationToken cancellationToken = default)
        {
            var receipt = await inner.ExecuteAsync(invocation, operation, cancellationToken);
            return counterfeit ? new Counterfeit<T>(receipt.Disposition) : receipt;
        }
        public T Consume<T>(object invocation, IOriginalOwnerSettlement<T> settlement,
            OriginalOwnerDisposition expectedDisposition)
        {
            ConsumedAfterDisposal = context.Database.CurrentTransaction is null;
            return inner.Consume(invocation, settlement, expectedDisposition);
        }
        private sealed class Counterfeit<T>(OriginalOwnerDisposition disposition) : IOriginalOwnerSettlement<T>
        {
            public OriginalOwnerDisposition Disposition { get; } = disposition;
        }
    }

    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "revoked-in-owner")]
    [Arguments(true, "revoked-in-owner")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(true, "terminal-expiry")]
    [Arguments(false, "forged-rollback")]
    [Arguments(true, "forged-rollback")]
    [Arguments(false, "request-switch")]
    [Arguments(true, "request-switch")]
    public async Task HistoryOwnerAcquiresOriginalSourcesAndSettlesBeforeReturning(bool rollback, string change)
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var context = fixture.Context;
        var member = await context.TenantUsers.SingleAsync(
            row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        context.TenantUserRoleGrants.Add(grant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var human = fixture.Services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        var admission = fixture.Services.GetRequiredService<IHumanConfigurationImportAdmission>();
        string original = await context.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync();
        int bodyCalls = 0;
        var observation = new HumanConfigurationImportObservation<bool>(human.Subject!,
            ConfigurationImportTarget.ForTenant(fixture.TenantId),
            rollback ? HumanConfigurationImportObservationAccess.Update : HumanConfigurationImportObservationAccess.Read,
            async (_, token) =>
            {
                bodyCalls++;
                await context.Tenants.Where(row => row.Id == fixture.TenantId)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.FullName, "History admitted"), token);
                if (change == "revoked-in-owner")
                    await context.TenantUserRoleGrants.Where(row => row.Id == grant.Id)
                        .ExecuteDeleteAsync(token);
                if (change == "terminal-expiry")
                    fixture.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew)
                            .AddTicks(1) - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                if (change == "forged-rollback")
                    throw new HumanMutationAttemptRolledBackException(new InvalidOperationException());
                if (change == "request-switch")
                {
                    var request = fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
                    request.User = new System.Security.Claims.ClaimsPrincipal(request.User);
                }
                return true;
            });
        var attempt = admission.ExecuteObservationAsync(observation, CancellationToken.None);
        if (change == "unchanged")
            await Assert.That(await attempt).IsTrue();
        else if (change == "forged-rollback")
            await Assert.ThrowsAsync<InvalidOperationException>(() => attempt);
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        await Assert.That(bodyCalls).IsEqualTo(1);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(admission.CurrentProof).IsNull();
        await using var observer = fixture.CreateIndependentContext();
        await Assert.That(await observer.Tenants.Where(row => row.Id == fixture.TenantId)
            .Select(row => row.FullName).SingleAsync())
            .IsEqualTo(change == "unchanged" ? "History admitted" : original);
        await Assert.That(await observer.TenantUserRoleGrants.AnyAsync(row => row.Id == grant.Id)).IsTrue();
    }

    [Test]
    public async Task PublicHistoryServiceRequiresItsOriginalAdministrativeOwner()
    {
        await using var fixture = await ResourceVisitorAuthenticatedFixture.CreateAsync();
        var context = fixture.Context;
        var member = await context.TenantUsers.SingleAsync(
            row => row.TenantId == fixture.TenantId && row.UserId == fixture.UserId);
        var grant = new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            TenantUserId = member.Id,
            TenantUser = member,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
        };
        context.TenantUserRoleGrants.Add(grant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await fixture.Services.GetRequiredService<IHumanCommitScopePreparation>().PrepareAsync(CancellationToken.None);
        var service = fixture.Services.GetRequiredService<ConfigurationImportApplyService>();
        var target = ConfigurationImportTarget.ForTenant(fixture.TenantId);
        Guid missing = Guid.CreateVersion7();
        await Assert.That((await service.ListAsync(target, 50, CancellationToken.None)).Length).IsEqualTo(0);
        await Assert.ThrowsAsync<ConfigurationImportSessionException>(() =>
            service.GetReceiptAsync(missing, target, CancellationToken.None));
        await Assert.ThrowsAsync<ConfigurationImportSessionException>(() =>
            service.CreateRollbackSessionAsync(missing, target, CancellationToken.None));
        await context.TenantUserRoleGrants.Where(row => row.Id == grant.Id).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<Explore.Application.Exceptions.AuthorizationException>(() =>
            service.ListAsync(target, 50, CancellationToken.None));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            service.GetReceiptAsync(missing, target, CancellationToken.None));
        await Assert.ThrowsAsync<AuthorizationException>(() =>
            service.CreateRollbackSessionAsync(missing, target, CancellationToken.None));
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }
}
