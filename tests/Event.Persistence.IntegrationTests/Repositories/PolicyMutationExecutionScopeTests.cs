using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PolicyMutationExecutionScopeSqliteFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PolicyMutationExecutionScopeSqlite")]
public sealed class PolicyMutationExecutionScopeTests(PolicyMutationExecutionScopeSqliteFixture fixture)
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SelectedOwnerBindsTrustedSubjectBeforeConstructionAndRetainsItWhenBorrowed(bool tenantTarget)
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var selection = tenantTarget ? new PolicyMutationScope(fixture.TargetTenantId) : PolicyMutationScope.Instance;
        await using var execution = await Factory(caller.ServiceProvider).CreateAsync(selection, default);
        var lease = execution.Services.GetRequiredService<ExploreDbContextExecutionLease>();
        await Assert.That(lease.HumanScope).IsNotNull();
        await Assert.That(lease.HumanScope!.Subject.PrincipalId).IsEqualTo(fixture.AdministratorId);
        await Assert.That(lease.HumanScope.Purpose).IsEqualTo(tenantTarget
            ? HumanAdministratorCommitPurpose.RuntimeTenant : HumanAdministratorCommitPurpose.GlobalInstance);
        var authority = execution.Services.GetRequiredService<IInstanceAdministratorCommitAuthority>();
        var context = execution.Services.GetRequiredService<ExploreDbContext>();
        execution.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!.User =
            new System.Security.Claims.ClaimsPrincipal();
        await using var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var borrowed = await Factory(execution.Services).CreateAsync(selection, default);
        await Assert.That(borrowed.Services.GetRequiredService<ExploreDbContextExecutionLease>().HumanScope)
            .IsSameReferenceAs(lease.HumanScope);
        await Assert.That(await authority.IsInstanceAdministratorForCommitAsync(fixture.AdministratorId, default)).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await transaction.RollbackAsync();
    }

    [Test]
    public async Task InstanceAcquisitionConstructsConcreteDependenciesOnFreshGlobalLease()
    {
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var originalIdentity = original.TenantContext;
        var currentUser = caller.ServiceProvider.GetRequiredService<ICurrentUserService>();
        await using var execution = await Factory(caller.ServiceProvider)
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var selected = execution.Services.GetRequiredService<ExploreDbContext>();
        var repository = execution.Services.GetRequiredService<ISystemSettingRepository>();

        await Assert.That(ReferenceEquals(selected, original)).IsFalse();
        await Assert.That(selected.TenantFilterTenantId).IsNull();
        await Assert.That(selected.IsTenantFilterBypassed).IsFalse();
        await Assert.That(selected.CurrentUserService).IsSameReferenceAs(currentUser);
        await Assert.That(execution.Services.GetRequiredService<ITenantContextAccessor>().TenantId)
            .IsEqualTo((Guid?)fixture.CallerTenantId);
        await Assert.That((await repository.GetByKey(PolicyMutationExecutionScopeSqliteFixture.AuthorityKey))!.Value)
            .IsEqualTo("\"global\"");
        await Assert.That(original.TenantContext).IsSameReferenceAs(originalIdentity);
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    public async Task TenantAcquisitionConstructsConcreteDependenciesOnExactRuntimeLease()
    {
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var originalIdentity = original.TenantContext;
        await using var execution = await Factory(caller.ServiceProvider)
            .CreateAsync(new(fixture.TargetTenantId), CancellationToken.None);
        var selected = execution.Services.GetRequiredService<ExploreDbContext>();
        var repository = execution.Services.GetRequiredService<ISystemSettingRepository>();

        await Assert.That(ReferenceEquals(selected, original)).IsFalse();
        await Assert.That(selected.TenantFilterTenantId).IsEqualTo((Guid?)fixture.TargetTenantId);
        await Assert.That(selected.TenantContext!.TenantId).IsEqualTo(fixture.TargetTenantId);
        await Assert.That(selected.IsTenantFilterBypassed).IsFalse();
        await Assert.That(execution.Services.GetRequiredService<ITenantContextAccessor>().TenantId)
            .IsEqualTo((Guid?)fixture.TargetTenantId);
        await Assert.That((await repository.GetByKey(PolicyMutationExecutionScopeSqliteFixture.AuthorityKey))!.Value)
            .IsEqualTo("\"runtime\"");
        await Assert.That(await selected.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.SettingKey == PolicyMutationExecutionScopeSqliteFixture.AuthorityKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"target\"");
        await Assert.That(original.TenantContext).IsSameReferenceAs(originalIdentity);
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CompatibleNestedAcquisitionBorrowsOwnerAndPreservesItsTransaction(bool tenantTarget)
    {
        await using var caller = fixture.OpenCaller();
        var selection = tenantTarget ? new PolicyMutationScope(fixture.TargetTenantId) : PolicyMutationScope.Instance;
        await using var owner = await Factory(caller.ServiceProvider).CreateAsync(selection, CancellationToken.None);
        var context = owner.Services.GetRequiredService<ExploreDbContext>();
        var witness = owner.Services.GetRequiredService<PolicyScopeDisposalWitness>();
        var repository = owner.Services.GetRequiredService<ISystemSettingRepository>();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var nested = await Factory(owner.Services)
            .CreateAsync(new(selection.TenantId), CancellationToken.None);
        await Assert.That(nested.Services).IsSameReferenceAs(owner.Services);
        await Assert.That(nested.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(context);
        await Assert.That(nested.Services.GetRequiredService<ISystemSettingRepository>()).IsSameReferenceAs(repository);
        await nested.DisposeAsync();

        await Assert.That(witness.IsDisposed).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(selection.TenantId);
        await Assert.That((await repository.GetByKey(PolicyMutationExecutionScopeSqliteFixture.AuthorityKey))!.Value)
            .IsEqualTo(tenantTarget ? "\"runtime\"" : "\"global\"");
        await transaction.RollbackAsync();
    }

    [Test]
    [Arguments("instance-to-tenant")]
    [Arguments("tenant-to-instance")]
    [Arguments("tenant-to-other-tenant")]
    public async Task IncompatibleNestedSelectionRejectsWithoutReplacingOrDisposingOwner(string transition)
    {
        await using var caller = fixture.OpenCaller();
        var selection = transition == "instance-to-tenant"
            ? PolicyMutationScope.Instance : new(fixture.TargetTenantId);
        var incompatible = transition == "tenant-to-instance"
            ? PolicyMutationScope.Instance : new(fixture.CallerTenantId);
        await using var owner = await Factory(caller.ServiceProvider).CreateAsync(selection, CancellationToken.None);
        var context = owner.Services.GetRequiredService<ExploreDbContext>();
        var witness = owner.Services.GetRequiredService<PolicyScopeDisposalWitness>();

        await Assert.That(async () =>
        {
            await using var unexpected = await Factory(owner.Services).CreateAsync(incompatible, CancellationToken.None);
        }).Throws<InvalidOperationException>();

        await Assert.That(owner.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(context);
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(selection.TenantId);
        await Assert.That(witness.IsDisposed).IsFalse();
        await Assert.That((await owner.Services.GetRequiredService<ISystemSettingRepository>()
            .GetByKey(PolicyMutationExecutionScopeSqliteFixture.AuthorityKey))!.Value)
            .IsEqualTo(selection.TenantId is null ? "\"global\"" : "\"runtime\"");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OwnedScopeDisposalReleasesSelectedContextButLeavesCallerUsable(bool tenantTarget)
    {
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var selection = tenantTarget ? new PolicyMutationScope(fixture.TargetTenantId) : PolicyMutationScope.Instance;
        var execution = await Factory(caller.ServiceProvider).CreateAsync(selection, CancellationToken.None);
        var selected = execution.Services.GetRequiredService<ExploreDbContext>();
        var witness = execution.Services.GetRequiredService<PolicyScopeDisposalWitness>();
        try
        {
            await Assert.That(selected.CurrentUserService).IsNotNull();
        }
        finally
        {
            await execution.DisposeAsync();
        }

        await Assert.That(witness.IsDisposed).IsTrue();
        await Assert.That(selected.CurrentUserService).IsNull();
        await Assert.That(selected.TenantFilterTenantId).IsNull();
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    public async Task NativeInstanceCommandDefersMutationGraphUntilGlobalLeaseIsBound()
    {
        fixture.Constructions.Clear();
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var handler = caller.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(fixture.Constructions).IsEmpty();

        var result = await handler.ExecuteAsync(new()
        {
            Key = PolicyMutationExecutionScopeSqliteFixture.AuthorityKey,
            Value = "unused",
            Scope = SettingScope.Instance
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        var construction = fixture.Constructions.Single();
        await Assert.That(ReferenceEquals(construction.Context, original)).IsFalse();
        await Assert.That(construction.TenantId).IsNull();
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    public async Task NativeTenantCommandBuildsAuthorizedGraphOnExactTargetWithoutRetargetingCaller()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        fixture.Constructions.Clear();
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var handler = caller.ServiceProvider.GetRequiredService<ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>();
        await Assert.That(fixture.Constructions).IsEmpty();

        var result = await handler.ExecuteAsync(new(fixture.TargetTenantId,
            PolicyMutationExecutionScopeSqliteFixture.AuthorityKey, "unused"), CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("setting_not_found");
        var construction = fixture.Constructions.Single();
        await Assert.That(ReferenceEquals(construction.Context, original)).IsFalse();
        await Assert.That(construction.TenantId).IsEqualTo((Guid?)fixture.TargetTenantId);
        await Assert.That(construction.ActorId).IsEqualTo((Guid?)fixture.AdministratorId);
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    public async Task OverlappingExecutionScopesKeepTenantFactsSeparateFromSharedRequest()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await using var first = await Factory(caller.ServiceProvider)
            .CreateAsync(new(fixture.TargetTenantId), default);
        await using var second = await Factory(caller.ServiceProvider)
            .CreateAsync(new(fixture.CallerTenantId), default);
        var firstFacts = first.Services.GetRequiredService<ITenantContextAccessor>();
        var secondFacts = second.Services.GetRequiredService<ITenantContextAccessor>();
        caller.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(fixture.CallerTenantId);

        await Assert.That(firstFacts.TenantId).IsEqualTo((Guid?)fixture.TargetTenantId);
        await Assert.That(secondFacts.TenantId).IsEqualTo((Guid?)fixture.CallerTenantId);
        await Assert.That(() => firstFacts.SetTenant(fixture.CallerTenantId)).Throws<InvalidOperationException>();
        await Assert.That(() => firstFacts.Clear()).Throws<InvalidOperationException>();
        await Assert.That(() => firstFacts.BindExecutionTenant(fixture.TargetTenantId)).Throws<InvalidOperationException>();
        await Assert.That(first.Services.GetRequiredService<ExploreDbContext>().TenantFilterTenantId)
            .IsEqualTo((Guid?)fixture.TargetTenantId);
        await AssertCallerLeaseAsync(original);
    }

    [Test]
    public async Task NativeAuthorizationDenialPrecedesMutationGraphAndPolicyAcquisition()
    {
        fixture.Constructions.Clear();
        await using var caller = fixture.OpenCaller();
        var original = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var handler = caller.ServiceProvider.GetRequiredService<ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>();

        await Assert.That(async () =>
        {
            await handler.ExecuteAsync(new(fixture.TargetTenantId,
                PolicyMutationExecutionScopeSqliteFixture.AuthorityKey, "unused"), CancellationToken.None);
        })
            .Throws<AuthorizationException>();

        await Assert.That(fixture.Constructions).IsEmpty();
        await Assert.That(caller.ServiceProvider.GetRequiredService<ITenantContextAccessor>().TenantId)
            .IsEqualTo((Guid?)fixture.CallerTenantId);
        await AssertCallerLeaseAsync(original);
    }

    private static IPolicyMutationExecutionScopeFactory Factory(IServiceProvider services) =>
        services.GetRequiredService<IPolicyMutationExecutionScopeFactory>();

    private async Task AssertCallerLeaseAsync(ExploreDbContext context)
    {
        await Assert.That(context.TenantFilterTenantId).IsEqualTo((Guid?)fixture.CallerTenantId);
        await Assert.That(context.IsTenantFilterBypassed).IsFalse();
        await Assert.That(context.CurrentUserService).IsNotNull();
        await Assert.That(await context.SystemSettings.AsNoTracking()
            .Where(row => row.SettingKey == PolicyMutationExecutionScopeSqliteFixture.AuthorityKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"runtime\"");
        await Assert.That(await context.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.SettingKey == PolicyMutationExecutionScopeSqliteFixture.AuthorityKey)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"caller\"");
    }
}
