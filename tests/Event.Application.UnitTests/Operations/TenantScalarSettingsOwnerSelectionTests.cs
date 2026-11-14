using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Operations;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Operations;

public sealed class TenantScalarSettingsOwnerSelectionTests
{
    [Test]
    [Arguments("value")]
    [Arguments("batch")]
    [Arguments("reset")]
    [Arguments("lock")]
    [Arguments("unlock")]
    public async Task TenantScalarMutationSelectsItsExactStandardOwnerBeforeHandlerConstruction(string operation)
    {
        Guid tenant = Guid.CreateVersion7();
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor>(new FixedTenantFacts(tenant));
        await using var provider = services.BuildServiceProvider();
        var selection = await PolicyMutationCommandScope.SelectAsync(
            Request(operation, SettingScope.Tenant), provider, CancellationToken.None);
        await Assert.That(selection).IsEqualTo(new PolicyMutationScope(tenant));
        await Assert.That(provider.GetRequiredService<ITenantContextAccessor>().TenantId).IsEqualTo(tenant);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrEmptyTenantCannotFallBackToAnUnselectedMutationOwner(bool empty)
    {
        var services = new ServiceCollection();
        if (empty) services.AddSingleton<ITenantContextAccessor>(new FixedTenantFacts(Guid.Empty));
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PolicyMutationCommandScope.SelectAsync(Request("value", SettingScope.Tenant), provider,
                CancellationToken.None).AsTask());
    }

    [Test]
    public async Task UserPreferencesRemainOutsideAdministratorOwnerSelection()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var selection = await PolicyMutationCommandScope.SelectAsync(
            Request("value", SettingScope.User), provider, CancellationToken.None);
        await Assert.That(selection).IsNull();
    }

    private static object Request(string operation, SettingScope scope) => operation switch
    {
        "value" => new UpdateSettingCommand
        { Key = GovernanceSettingKeys.Tenants.UserRegistrationEnabled, Value = "false", Scope = scope },
        "batch" => new UpdateSettingBatchCommand
        {
            Category = "Tenant",
            Scope = scope,
            Values = new Dictionary<string, string>
            { [GovernanceSettingKeys.Tenants.UserRegistrationEnabled] = "false" }
        },
        "reset" => new ResetSettingCommand { Key = GovernanceSettingKeys.Tenants.UserRegistrationEnabled, Scope = scope },
        "lock" => new LockSettingCommand { Key = GovernanceSettingKeys.Tenants.UserRegistrationEnabled, Scope = scope },
        "unlock" => new UnlockSettingCommand { Key = GovernanceSettingKeys.Tenants.UserRegistrationEnabled, Scope = scope },
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private sealed class FixedTenantFacts(Guid tenantId) : ITenantContextAccessor
    {
        public Guid? TenantId => tenantId;
        public bool IsResolved => true;
        public void SetTenant(Guid value) => throw new InvalidOperationException("Selection cannot rebind tenant facts.");
        public void BindExecutionTenant(Guid value) => throw new InvalidOperationException("Selection cannot rebind tenant facts.");
        public void Clear() => throw new InvalidOperationException("Selection cannot clear tenant facts.");
    }
}
