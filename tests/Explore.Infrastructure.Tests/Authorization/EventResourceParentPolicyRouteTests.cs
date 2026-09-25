using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Authorization;

public sealed class EventResourceParentPolicyRouteTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeParentScopeChangesIndependentlyOfResourceBindingAndUsesDefaultVersion(bool scoped)
    {
        Guid tenant = Guid.CreateVersion7(), deployment = Guid.CreateVersion7(), operation = Guid.CreateVersion7();
        var now = new DateTime(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var activation = EventResourceProviderActivation.Create(deployment, now);
        activation.BeginOperation(operation, now);
        await Assert.That(activation.TryActivate(operation, activation.Epoch, true, 1, 1, now)).IsTrue();
        var binding = new EventResourceProviderBindingDocument(Guid.CreateVersion7(),
            [new(deployment, ["https://pdp.example.test"], "resource-only-scope", "resource-v2")]);
        var values = new Dictionary<string, string>
        {
            [GovernanceSettingKeys.Cerbos.TenantCustomizationEnabled] = "false",
            [GovernanceSettingKeys.Cerbos.Mode] = JsonSerializer.Serialize("shared"),
            [GovernanceSettingKeys.Security.AuthorizationProvider] = JsonSerializer.Serialize("cerbos"),
            [GovernanceSettingKeys.Cerbos.GrpcEndpoint] = JsonSerializer.Serialize("https://pdp.example.test"),
            [EventResourceProviderBindingDocument.SettingKey] = JsonSerializer.Serialize(binding, EventResourceProviderBindingDocument.JsonOptions)
        };
        var system = Substitute.For<ISystemSettingRepository>();
        system.GetByKey(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
            values.TryGetValue(call.ArgAt<string>(0), out var value)
                ? new SystemSetting { SettingKey = call.ArgAt<string>(0), Value = value } : null);
        var tenants = Substitute.For<ITenantSettingRepository>();
        tenants.GetByTenantAndKeys(tenant, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);
        var activations = Substitute.For<IEventResourceProviderActivationRepository>();
        activations.GetAsync(deployment, Arg.Any<CancellationToken>()).Returns(activation);
        var settings = new CerbosSettings { UsePolicyScope = scoped };
        var reader = new EventResourceProviderSnapshotReader(system, tenants, activations,
            Options.Create(new AuthorizationProviderDeploymentOptions()), Options.Create(settings),
            new ConfigurationBuilder().Build(), NullLogger<EventResourceProviderSnapshotReader>.Instance);
        var first = (await reader.ReadAsync(tenant, default))!;
        await Assert.That(first.IsUsable).IsTrue();
        await Assert.That(first.Scope).IsEqualTo("resource-only-scope");
        await Assert.That(first.PolicyVersion).IsEqualTo("resource-v2");
        await Assert.That(first.ParentEventPolicy).IsEqualTo(new EventResourceParentPolicyRoute(scoped ? tenant.ToString("D") : ""));
        settings.UsePolicyScope = !scoped;
        var changed = (await reader.ReadAsync(tenant, default))!;
        await Assert.That(changed.Scope).IsEqualTo(first.Scope);
        await Assert.That(changed.ActivationEpoch).IsEqualTo(first.ActivationEpoch);
        await Assert.That(changed == first).IsFalse();
    }
}
