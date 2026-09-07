using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Architecture.Tests;

public class AiContextDisclosureSchemaTests
{
    private static readonly Type[] PiiEntityTypes =
    {
        typeof(UserPii),
        typeof(OrganizationPii),
        typeof(ActorPii),
        typeof(LocationPii)
    };

    private static readonly HashSet<Type> ExcludedNavigationTypes = new()
    {
        typeof(User),
        typeof(Organization),
        typeof(Actor),
        typeof(Location)
    };

    private static readonly HashSet<string> ExplicitNonPiiKeys = new(StringComparer.Ordinal)
    {
        AiContextDisclosureEntry.BuildKey(nameof(AtprotoIdentity), nameof(AtprotoIdentity.Did)),
        AiContextDisclosureEntry.BuildKey(nameof(AtprotoIdentity), nameof(AtprotoIdentity.Handle))
    };

    private static readonly AiProviderTrustTierEnum[] AllProviderTrustTiers =
    {
        AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel,
        AiProviderTrustTierEnum.TenantControlledPrivateEndpoint,
        AiProviderTrustTierEnum.TenantConfiguredExternalProcessor,
        AiProviderTrustTierEnum.PlatformConfiguredExternalProcessor,
        AiProviderTrustTierEnum.Unknown
    };

    private static IEnumerable<PropertyInfo> EnumerateClassifiedProperties(Type piiType) =>
        piiType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !ExcludedNavigationTypes.Contains(p.PropertyType));

    private static HashSet<string> GetExpectedKeys()
    {
        var keys = PiiEntityTypes
            .SelectMany(type => EnumerateClassifiedProperties(type)
                .Select(property => AiContextDisclosureEntry.BuildKey(type.Name, property.Name)))
            .ToHashSet(StringComparer.Ordinal);
        keys.UnionWith(ExplicitNonPiiKeys);
        return keys;
    }

    [Test]
    public async Task CreateDefault_DoesNotThrowAndProducesRegistry()
    {
        AiContextDisclosureRegistry? registry = null;
        Exception? caught = null;

        try
        {
            registry = AiContextDisclosureRegistry.CreateDefault();
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        await Assert.That(caught).IsNull().Because("registry seed must not throw duplicate-key or validation errors");
        await Assert.That(registry).IsNotNull();
        await Assert.That(registry!.Count).IsGreaterThan(0).Because("registry must contain PII classifications");
    }

    [Test]
    public async Task EveryPiiPropertyIsClassified()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var errors = new List<string>();

        foreach (var piiType in PiiEntityTypes)
        {
            foreach (var property in EnumerateClassifiedProperties(piiType))
            {
                if (!registry.TryGetEntry(piiType.Name, property.Name, out _))
                {
                    errors.Add($"{piiType.Name}.{property.Name} is missing a classification entry in {nameof(AiContextDisclosureRegistry)}.");
                }
            }
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    public async Task RegistryEntryCountMatchesPiiPropertySurface()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var expected = GetExpectedKeys().Count;

        await Assert.That(registry.Count).IsEqualTo(expected)
            .Because("registry must classify the complete persisted disclosure surface and exclude navigation properties");
    }

    [Test]
    public async Task RegistryHasNoExtraEntriesBeyondPiiProperties()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var validKeys = GetExpectedKeys();

        var extras = registry.Entries
            .Where(e => !validKeys.Contains(e.Key))
            .Select(e => e.Key)
            .ToList();

        await Assert.That(extras).IsEmpty()
            .Because("registry must not carry entries outside the persisted disclosure surface");
    }

    [Test]
    public async Task NavigationPropertiesAreNotClassified()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var errors = new List<string>();

        foreach (var piiType in PiiEntityTypes)
        {
            var navPropertyNames = piiType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => ExcludedNavigationTypes.Contains(p.PropertyType))
                .Select(p => p.Name);

            foreach (var navName in navPropertyNames)
            {
                if (registry.TryGetEntry(piiType.Name, navName, out _))
                {
                    errors.Add($"{piiType.Name}.{navName} is a navigation property and must NOT be classified.");
                }
            }
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    public async Task Phase4GatedEntriesRequireConfidentialOrHigher()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var violators = registry.Entries
            .Where(e => e.Phase4Gated && e.Sensitivity < AiContextSensitivityEnum.Confidential)
            .Select(e => $"{e.Key} (Sensitivity={e.Sensitivity})")
            .ToList();

        await Assert.That(violators).IsEmpty()
            .Because("Phase-4 gating only makes sense for Confidential or Restricted PII; Public/Internal fields must not be gated");
    }

    [Test]
    public async Task PostcodeClassification_IsContextualToOwningEntity()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();

        bool hasOrganizationPostcode = registry.TryGetEntry(
            nameof(OrganizationPii),
            nameof(OrganizationPii.Postcode),
            out AiContextDisclosureEntry? organizationPostcode);
        bool hasLocationPostcode = registry.TryGetEntry(
            nameof(LocationPii),
            nameof(LocationPii.Postcode),
            out AiContextDisclosureEntry? locationPostcode);

        await Assert.That(hasOrganizationPostcode).IsTrue();
        await Assert.That(organizationPostcode!.Sensitivity).IsEqualTo(AiContextSensitivityEnum.Internal);
        await Assert.That(organizationPostcode.Phase4Gated).IsFalse();
        await Assert.That(hasLocationPostcode).IsTrue();
        await Assert.That(locationPostcode!.Sensitivity).IsEqualTo(AiContextSensitivityEnum.Restricted);
        await Assert.That(locationPostcode.Phase4Gated).IsTrue();
    }

    [Test]
    public async Task EveryEntryHasDefinedSensitivityAndRule()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var validSensitivities = Enum.GetValues<AiContextSensitivityEnum>().Cast<AiContextSensitivityEnum>().ToHashSet();
        var validRules = Enum.GetValues<AiContextDisclosureRuleEnum>().Cast<AiContextDisclosureRuleEnum>().ToHashSet();

        var errors = new List<string>();
        foreach (var entry in registry.Entries)
        {
            if (!validSensitivities.Contains(entry.Sensitivity))
            {
                errors.Add($"{entry.Key} has undefined Sensitivity value {(int)entry.Sensitivity}.");
            }
            if (!validRules.Contains(entry.LocalModelRule))
            {
                errors.Add($"{entry.Key} has undefined LocalModelRule value {(int)entry.LocalModelRule}.");
            }
        }

        await Assert.That(errors).IsEmpty().Because(string.Join("\n", errors));
    }

    [Test]
    [MethodDataSource(nameof(PublicOrInternalEntries))]
    public async Task PublicOrInternalField_IsLocalRuleAtEveryProviderTrustTier(AiContextDisclosureEntry entry)
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();

        foreach (var tier in AllProviderTrustTiers)
        {
            var effective = registry.ResolveEffectiveRule(entry.EntityName, entry.FieldName, tier, piiDisclosureEnabled: true);
            await Assert.That(effective).IsEqualTo(entry.LocalModelRule)
                .Because($"Public/Internal field {entry.Key} should honor LocalModelRule at every tier (tier={tier}).");
        }
    }

    [Test]
    [MethodDataSource(nameof(ConfidentialOrRestrictedEntries))]
    public async Task ConfidentialOrRestrictedField_IsLocalRuleOnlyAtLocalModelAndDenyElsewhere(AiContextDisclosureEntry entry)
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();

        var localEffective = registry.ResolveEffectiveRule(
            entry.EntityName,
            entry.FieldName,
            AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel,
            piiDisclosureEnabled: true);
        await Assert.That(localEffective).IsEqualTo(entry.LocalModelRule)
            .Because($"Confidential/Restricted field {entry.Key} should honor LocalModelRule at the local-model tier.");

        foreach (var externalTier in AllProviderTrustTiers.Where(t => t != AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel))
        {
            var effective = registry.ResolveEffectiveRule(entry.EntityName, entry.FieldName, externalTier, piiDisclosureEnabled: true);
            await Assert.That(effective).IsEqualTo(AiContextDisclosureRuleEnum.Deny)
                .Because($"Confidential/Restricted field {entry.Key} must be denied at external provider trust tier {externalTier}.");
        }
    }

    [Test]
    [MethodDataSource(nameof(Phase4GatedEntries))]
    public async Task Phase4GatedField_DeniedWhenPiiDisclosureDisabled(AiContextDisclosureEntry entry)
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();

        foreach (var tier in AllProviderTrustTiers)
        {
            var effective = registry.ResolveEffectiveRule(
                entry.EntityName,
                entry.FieldName,
                tier,
                piiDisclosureEnabled: false);
            await Assert.That(effective).IsEqualTo(AiContextDisclosureRuleEnum.Deny)
                .Because($"Phase-4-gated field {entry.Key} must be denied at every tier until PII disclosure is enabled (tier={tier}).");
        }
    }

    [Test]
    public async Task UnregisteredField_IsAlwaysDenied()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();

        foreach (var tier in AllProviderTrustTiers)
        {
            var effective = registry.ResolveEffectiveRule(
                "UserPii",
                "ThisFieldDoesNotExist",
                tier,
                piiDisclosureEnabled: true);
            await Assert.That(effective).IsEqualTo(AiContextDisclosureRuleEnum.Deny)
                .Because($"Unregistered fields must always be denied (tier={tier}).");
        }
    }

    [Test]
    public async Task ResolveEffectiveRule_ForUnregisteredEntity_IsAlwaysDenied()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        var effective = registry.ResolveEffectiveRule(
            "EntityThatDoesNotExist",
            "Email",
            AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel,
            piiDisclosureEnabled: true);

        await Assert.That(effective).IsEqualTo(AiContextDisclosureRuleEnum.Deny)
            .Because("Unknown entities must always be denied as a fail-closed default.");
    }

    public static IEnumerable<AiContextDisclosureEntry> PublicOrInternalEntries()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        return registry.Entries.Where(e => e.Sensitivity is AiContextSensitivityEnum.Public or AiContextSensitivityEnum.Internal).ToList();
    }

    public static IEnumerable<AiContextDisclosureEntry> ConfidentialOrRestrictedEntries()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        return registry.Entries.Where(e => e.Sensitivity is AiContextSensitivityEnum.Confidential or AiContextSensitivityEnum.Restricted).ToList();
    }

    public static IEnumerable<AiContextDisclosureEntry> Phase4GatedEntries()
    {
        var registry = AiContextDisclosureRegistry.CreateDefault();
        return registry.Entries.Where(e => e.Phase4Gated).ToList();
    }
}
