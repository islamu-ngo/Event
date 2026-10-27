using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models.InternalEvents;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Infrastructure.Identity;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualDispatcherAppliesOriginalQuarantineButCannotAcknowledgeAnUnfinishedRecipe(bool storedFile)
    {
        await using var fixture = await Fixture.CreateAsync(storedFile);
        await ReleaseInitialLeaseAsync(fixture);
        var tenants = new List<QuarantineDrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = DrainServices(fixture, tenants, machines);
        var dispatcher = services.GetRequiredService<ModerationEffectOutboxDispatcher>();
        var message = new OutboxMessage
        {
            Id = fixture.EffectId,
            AggregateId = fixture.EffectId,
            AggregateType = ModerationEffectOutboxMessageFactory.AggregateType,
            EventType = ModerationEffectOutboxMessageFactory.EventType
        };
        await Assert.That(() => dispatcher.DispatchAsync(message)).Throws<InvalidOperationException>();
        Guid causeId;
        await using (var reader = fixture.Context())
        {
            reader.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var cause = await reader.EventResourceModerationQuarantines.SingleAsync(row => row.EffectId == fixture.EffectId);
            causeId = cause.Id;
            await Assert.That(cause.AppliedAt).IsEqualTo(Now);
            var resource = await reader.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
            await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(1);
            await Assert.That(resource.HasPublishablePayload()).IsFalse();
            var effect = await reader.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
            await Assert.That(effect.CompletedAt).IsNull();
            await Assert.That(effect.LeaseId).IsNull();
        }
        fixture.Clock.UtcNow = Now.AddSeconds(10);
        await Assert.That(() => dispatcher.DispatchAsync(message)).Throws<InvalidOperationException>();
        await using var verify = fixture.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        var original = await verify.EventResourceModerationQuarantines.SingleAsync(row => row.EffectId == fixture.EffectId);
        await Assert.That(original.Id).IsEqualTo(causeId);
        await Assert.That(original.AppliedAt).IsEqualTo(Now);
        var stillPending = await verify.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
        await Assert.That(stillPending.CompletedAt).IsNull();
        await Assert.That(stillPending.LeaseId).IsNull();
        await Assert.That(tenants.Count).IsEqualTo(2);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualDispatcherCannotApplyOrCompleteWithExpiredPurposeOrCredential(bool expireCredential)
    {
        await using var fixture = await Fixture.CreateAsync();
        await ReleaseInitialLeaseAsync(fixture);
        if (expireCredential)
        {
            await using var writer = fixture.Context();
            await writer.ExternalApiKeys.Where(row => row.KeyId == fixture.KeyId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExpiresAt, Now.UtcDateTime));
        }
        else
        {
            fixture.Clock.UtcNow = fixture.PurposeDeadline;
        }
        var tenants = new List<QuarantineDrainTenant>();
        var machines = new List<MachinePrincipalAccessor>();
        await using var services = DrainServices(fixture, tenants, machines);
        var message = new OutboxMessage
        {
            Id = fixture.EffectId,
            AggregateId = fixture.EffectId,
            AggregateType = ModerationEffectOutboxMessageFactory.AggregateType,
            EventType = ModerationEffectOutboxMessageFactory.EventType
        };
        await Assert.That(() => services.GetRequiredService<ModerationEffectOutboxDispatcher>().DispatchAsync(message))
            .Throws<InvalidOperationException>();
        await using var verify = fixture.Context();
        verify.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await verify.EventResourceModerationQuarantines
            .AnyAsync(row => row.EffectId == fixture.EffectId)).IsFalse();
        var resource = await verify.EventResources.SingleAsync(row => row.Id == fixture.ResourceId);
        await Assert.That(resource.ModerationQuarantineCount).IsEqualTo(0);
        var effect = await verify.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
        await Assert.That(effect.CompletedAt).IsNull();
        await Assert.That(effect.LeaseId).IsNull();
        await Assert.That(tenants.Count).IsEqualTo(1);
        foreach (var tenant in tenants) await Assert.That(tenant.TenantId).IsNull();
        foreach (var machine in machines) await Assert.That(machine.Current).IsNull();
    }

    private static async Task ReleaseInitialLeaseAsync(Fixture fixture)
    {
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        var released = await new ModerationEffectLeaseRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock)
            .ReleaseAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(released.Outcome).IsEqualTo(ModerationEffectLeaseOutcome.Known);
    }

    private static ServiceProvider DrainServices(
        Fixture fixture, List<QuarantineDrainTenant> tenants, List<MachinePrincipalAccessor> machines)
    {
        var settings = new ModerationReconciliationOptions { Enabled = true, TenantId = fixture.Database.TenantId };
        var services = new ServiceCollection();
        services.AddSingleton<IOptionsMonitor<ModerationReconciliationOptions>>(new Settings(settings));
        services.AddScoped<ITenantContextAccessor>(_ =>
        {
            var tenant = new QuarantineDrainTenant();
            tenants.Add(tenant);
            return tenant;
        });
        services.AddScoped<MachinePrincipalAccessor>(_ =>
        {
            var machine = new MachinePrincipalAccessor(new HttpContextAccessor());
            machines.Add(machine);
            return machine;
        });
        services.AddScoped<IMachinePrincipalExecutionAccessor>(provider => provider.GetRequiredService<MachinePrincipalAccessor>());
        services.AddScoped<ExploreDbContext>(provider =>
        {
            var context = fixture.Context();
            context.TenantContext = provider.GetRequiredService<ITenantContextAccessor>().TenantId is { } id
                ? new SelectedTenant(id) : null;
            return context;
        });
        services.AddScoped<IExternalApiKeyAuthenticationRepository>(provider => new ExternalApiKeyAuthenticationRepository(
            new SecurityTestSupport.AdministrativeAuthorityTestFactory(provider.GetRequiredService<ExploreDbContext>())));
        services.AddScoped<IModerationMachineSubjectBindingRepository>(provider =>
        {
            var context = provider.GetRequiredService<ExploreDbContext>();
            var admin = new AdminContext(new HttpContextAccessor(), new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
            return new ModerationMachineSubjectBindingRepository(context, admin,
                provider.GetRequiredService<MachinePrincipalAccessor>(), provider.GetRequiredService<IExternalApiKeyAuthenticationRepository>(),
                new ModerationNativeAuthorityParticipantTests.UnconfiguredKeys(), fixture.Clock);
        });
        services.AddScoped(provider => new ModerationReconciliationCredentialProvider(
            provider.GetRequiredService<IOptionsMonitor<ModerationReconciliationOptions>>(),
            provider.GetRequiredService<IExternalApiKeyAuthenticationRepository>(), new Secret(fixture), fixture.Clock));
        services.AddScoped<IModerationEffectLeaseRepository>(provider => new ModerationEffectLeaseRepository(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<MachinePrincipalAccessor>(),
            provider.GetRequiredService<IModerationMachineSubjectBindingRepository>(),
            provider.GetRequiredService<IExternalApiKeyAuthenticationRepository>(), fixture.Clock));
        services.AddScoped<IModerationResourceQuarantineRepository>(provider => new ModerationResourceQuarantineRepository(
            provider.GetRequiredService<ExploreDbContext>(), provider.GetRequiredService<MachinePrincipalAccessor>(),
            provider.GetRequiredService<IModerationMachineSubjectBindingRepository>(),
            provider.GetRequiredService<IExternalApiKeyAuthenticationRepository>(), fixture.Clock,
            fixture.Database.Privacy.Authority(provider.GetRequiredService<ExploreDbContext>(), fixture.Clock)));
        services.AddScoped<IModerationOriginalEffectCompletionRepository>(provider => OriginalCompletionRepository(
            fixture, provider.GetRequiredService<ExploreDbContext>(),
            provider.GetRequiredService<MachinePrincipalAccessor>(),
            provider.GetRequiredService<IModerationMachineSubjectBindingRepository>()));
        services.AddScoped<ModerationEffectOutboxDispatcher>();
        return services.BuildServiceProvider();
    }

    private sealed class QuarantineDrainTenant() : TenantContextAccessor(new HttpContextAccessor());
}
