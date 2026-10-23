using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PolicyMutationExecutionScopeTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DirectCommandHumanGatePrecedesPreparationOnTheSelectedOwner(bool tenantTarget)
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var ambient = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        ExploreDbContext? observed = null;
        using var observation = fixture.ObserveNextPreparation(context =>
        {
            HumanSecuritySourceGate.RequireCurrent(context);
            if (ReferenceEquals(context, ambient) || context.Database.CurrentTransaction is not null
                || context.TenantFilterTenantId != (tenantTarget ? fixture.TargetTenantId : null))
                throw new InvalidOperationException("The command prepared on the wrong selected owner.");
            observed = context;
        });

        BaseCommandResponse<Guid> result = tenantTarget
            ? await caller.ServiceProvider.GetRequiredService<ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new(fixture.TargetTenantId, PolicyMutationExecutionScopeSqliteFixture.AuthorityKey, "unused"), default)
            : await caller.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>()
                .ExecuteAsync(new()
                {
                    Key = PolicyMutationExecutionScopeSqliteFixture.AuthorityKey,
                    Value = "unused",
                    Scope = SettingScope.Instance
                }, default);

        // This fixture proves dispatch custody, not original-credential commit admission.
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(observed).IsNotNull();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(ambient)).IsFalse();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(observed!)).IsFalse();
        await AssertCallerLeaseAsync(ambient);
    }

    [Test]
    public async Task DirectCommandHumanGatePreservesTheCompatibleCallersCustody()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        await using var selected = await Factory(caller.ServiceProvider).CreateAsync(new(fixture.TargetTenantId), default);
        var context = selected.Services.GetRequiredService<ExploreDbContext>();
        await using var human = await new HumanSecuritySourceGate(context).AcquireAsync();
        bool observed = false;
        using var observation = fixture.ObserveNextPreparation(current =>
        {
            HumanSecuritySourceGate.RequireCurrent(current);
            if (!ReferenceEquals(current, context) || current.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("The compatible command replaced its original idle owner.");
            observed = true;
        });

        var result = await selected.Services
            .GetRequiredService<ICommandHandler<SetInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new(fixture.TargetTenantId, PolicyMutationExecutionScopeSqliteFixture.AuthorityKey, "unused"), default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(observed).IsTrue();
        HumanSecuritySourceGate.RequireCurrent(context);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await Assert.That(selected.Services.GetRequiredService<ExploreDbContext>()).IsSameReferenceAs(context);
    }

    [Test]
    public async Task DirectCommandHumanGateDoesNotLockTheDelegatingAmbientOwner()
    {
        using var authentication = fixture.AuthenticateAdministrator();
        await using var caller = fixture.OpenCaller();
        var ambient = caller.ServiceProvider.GetRequiredService<ExploreDbContext>();
        bool observed = false;
        using var observation = fixture.ObserveNextPreparation(selected =>
        {
            HumanSecuritySourceGate.RequireCurrent(selected);
            if (ReferenceEquals(selected, ambient) || selected.TenantFilterTenantId.HasValue
                || HumanSecuritySourceGate.TryRequireCurrent(ambient))
                throw new InvalidOperationException("A delegated instance command acquired ambient tenant custody.");
            observed = true;
        });

        var result = await caller.ServiceProvider
            .GetRequiredService<ICommandHandler<UpdateSettingCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new()
            {
                Key = PolicyMutationExecutionScopeSqliteFixture.AuthorityKey,
                Value = "unused",
                Scope = SettingScope.Instance
            }, default);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(observed).IsTrue();
        await Assert.That(HumanSecuritySourceGate.TryRequireCurrent(ambient)).IsFalse();
        await AssertCallerLeaseAsync(ambient);
    }
}
