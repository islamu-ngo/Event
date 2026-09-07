namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;
using Explore.Domain.Settings;

public class GovernanceSettingKeysTests
{
    [Test]
    public async Task AuthenticationGovernanceKeys_ShouldUseAuthPrefix()
    {
        var keyFields = typeof(GovernanceSettingKeys.Authentication)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string));

        foreach (var field in keyFields)
        {
            var value = field.GetRawConstantValue() as string;
            await Assert.That(value).IsNotNull();
            await Assert.That(value!).StartsWith("auth.");
        }
    }

    [Test]
    public async Task FederationGovernanceKeys_ShouldUseFederationPrefix()
    {
        var keyFields = typeof(GovernanceSettingKeys.Federation)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string));

        foreach (var field in keyFields)
        {
            var value = field.GetRawConstantValue() as string;
            await Assert.That(value).IsNotNull();
            await Assert.That(value!).StartsWith("federation.");
        }
    }

    [Test]
    public async Task AuthenticationSecretKeys_ShouldRemainInInfrastructureSecretSettingKeys()
    {
        await Assert.That(InfrastructureSecretSettingKeys.Authentication.KeycloakClientSecret).IsEqualTo("auth.keycloak_client_secret");
        await Assert.That(InfrastructureSecretSettingKeys.Authentication.GoogleClientSecret).IsEqualTo("auth.google_client_secret");
    }

    [Test]
    public async Task GovernanceSettingKeys_ShouldNotContainFlatAliases()
    {
        var flatFields = typeof(GovernanceSettingKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string));

        await Assert.That(flatFields).IsEmpty();
    }

    [Test]
    public async Task NestedClasses_ShouldExistForAllCategories()
    {
        var nestedTypes = typeof(GovernanceSettingKeys).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
        var nestedNames = nestedTypes.Select(t => t.Name).ToHashSet();

        string[] expectedCategories =
        [
            "Deployment", "Tenants", "Routing", "Events", "Organizations", "Groups",
            "Modules", "Branding", "Domains", "Email", "Storage", "Security",
            "Cerbos", "Authentication", "Federation", "Analytics", "TenantDelegation", "Localization",
            "EventList"
        ];

        foreach (var category in expectedCategories)
        {
            await Assert.That(nestedNames).Contains(category);
        }
    }

    [Test]
    public async Task SettingGroups_AllKeysMustExistInRegistry()
    {
        var groupTypes = typeof(ISettingGroup).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                && typeof(ISettingGroup).IsAssignableFrom(t));

        var missingKeys = new List<string>();

        foreach (var groupType in groupTypes)
        {
            var keysProperty = groupType.GetProperty("SettingKeys", BindingFlags.Public | BindingFlags.Static);
            if (keysProperty?.GetValue(null) is not IEnumerable<string> keys) continue;

            foreach (var key in keys)
            {
                if (!SettingRegistry.Contains(key))
                    missingKeys.Add($"{groupType.Name}: {key}");
            }
        }

        await Assert.That(missingKeys).IsEmpty();
    }

    [Test]
    public async Task SettingRegistry_AllKeysFollowDotNotation()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That(definition.Key).Contains(".");
        }
    }

    [Test]
    public async Task SettingRegistry_AllDefinitionsHaveCategory()
    {
        foreach (var definition in SettingRegistry.All)
        {
            await Assert.That(string.IsNullOrWhiteSpace(definition.Category)).IsFalse();
        }
    }

    [Test]
    public async Task UiShellSettingsShouldRegisterExplicitGovernanceAndPreferenceScopes()
    {
        string[] governanceKeys =
        [
            GovernanceSettingKeys.UiShell.RailPublicVisibility,
            GovernanceSettingKeys.UiShell.DefaultNavModeEvents,
            GovernanceSettingKeys.UiShell.DefaultNavModeStudio,
            GovernanceSettingKeys.UiShell.DefaultNavModeAi,
            GovernanceSettingKeys.UiShell.AllowUserNavOverride,
            GovernanceSettingKeys.UiShell.OrganizerDefaultWorkspace
        ];
        string[] preferenceKeys =
        [
            GovernanceSettingKeys.UiShellPreferences.Layout,
            GovernanceSettingKeys.UiShellPreferences.LastWorkspace,
            GovernanceSettingKeys.UiShellPreferences.LastActor,
            GovernanceSettingKeys.UiShellPreferences.LastSettingsScope
        ];

        foreach (var key in governanceKeys)
        {
            var definition = SettingRegistry.Get(key);
            await Assert.That(definition).IsNotNull();
            await Assert.That(definition!.MinScope).IsEqualTo(SettingScope.Instance);
            await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.Tenant);
            await Assert.That(definition.IsLockable).IsTrue();
        }

        foreach (var key in preferenceKeys)
        {
            var definition = SettingRegistry.Get(key);
            await Assert.That(definition).IsNotNull();
            await Assert.That(definition!.MinScope).IsEqualTo(SettingScope.User);
            await Assert.That(definition.MaxScope).IsEqualTo(SettingScope.User);
            await Assert.That(definition.IsLockable).IsFalse();
        }

        await Assert.That(SettingRegistry.Contains("ui_shell.default_nav_mode.settings")).IsFalse();
    }
}
