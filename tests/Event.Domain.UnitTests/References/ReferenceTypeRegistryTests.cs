namespace Event.Domain.UnitTests.References;

using System.Reflection;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.References;

public class ReferenceTypeRegistryTests
{
    [Test]
    public async Task AllTargets_HaveCompleteGovernanceMetadata()
    {
        await Assert.That(ReferenceTypeRegistry.AllTargets.Count).IsGreaterThan(0);

        foreach (var target in ReferenceTypeRegistry.AllTargets)
        {
            await Assert.That(string.IsNullOrWhiteSpace(target.Kind)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(target.DomainEntityName)).IsFalse();
            await Assert.That(target.IdKind).IsEqualTo(ReferenceIdKind.Guid);
            await Assert.That(Enum.IsDefined(target.Ownership)).IsTrue();
            await Assert.That(Enum.IsDefined(target.TenantScopeRule)).IsTrue();
            await Assert.That(Enum.IsDefined(target.CleanupBehavior)).IsTrue();
            await Assert.That(string.IsNullOrWhiteSpace(target.ValidationRule)).IsFalse();
        }
    }

    [Test]
    public async Task ExternalBindingPairs_CoverEveryExternalBindingTypeConstant()
    {
        var registeredExternalTypes = ReferenceTypeRegistry.AllExternalBindingPairs
            .Select(pair => pair.ExternalType)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var declaredExternalTypes = PublicStringConstants(typeof(ExternalBindingTypes.External))
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(registeredExternalTypes).IsEquivalentTo(declaredExternalTypes);
    }

    [Test]
    public async Task ExternalBindingPairs_HaveUniqueExternalTypesAndTargets()
    {
        var pairs = ReferenceTypeRegistry.AllExternalBindingPairs.ToArray();

        await Assert.That(pairs.Select(pair => pair.ExternalType).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(pairs.Length);

        foreach (var pair in pairs)
        {
            await Assert.That(pair.Target.Kind).IsEqualTo(pair.InternalType);
            await Assert.That(pair.CleanupBehavior).IsEqualTo(ReferenceCleanupBehavior.PurgeWithTenant);
            await Assert.That(Enum.IsDefined(pair.BindingTenantScopeRule)).IsTrue();
            await Assert.That(string.IsNullOrWhiteSpace(pair.ValidationRule)).IsFalse();
        }
    }

    [Test]
    public async Task ExternalBindingPairs_RejectUnregisteredInternalPairings()
    {
        var valid = ReferenceTypeRegistry.IsAllowedExternalBindingPair(
            ExternalBindingTypes.External.ProviderCustomer,
            ExternalBindingTypes.Internal.Tenant);
        var invalid = ReferenceTypeRegistry.IsAllowedExternalBindingPair(
            ExternalBindingTypes.External.ProviderCustomer,
            ExternalBindingTypes.Internal.User);

        await Assert.That(valid).IsTrue();
        await Assert.That(invalid).IsFalse();
    }

    [Test]
    public async Task ExternalBindingPairs_DefineExpectedTenantScopeRules()
    {
        ReferenceTypeRegistry.TryGetExternalBindingPair(
            ExternalBindingTypes.External.ProviderCustomer,
            ExternalBindingTypes.Internal.Tenant,
            out var customerBinding);
        ReferenceTypeRegistry.TryGetExternalBindingPair(
            ExternalBindingTypes.External.ExternalAdminUser,
            ExternalBindingTypes.Internal.User,
            out var userBinding);
        ReferenceTypeRegistry.TryGetExternalBindingPair(
            ExternalBindingTypes.External.CustomerOrganization,
            ExternalBindingTypes.Internal.Organization,
            out var organizationBinding);

        await Assert.That(customerBinding.BindingTenantScopeRule).IsEqualTo(ReferenceTenantScopeRule.Global);
        await Assert.That(userBinding.BindingTenantScopeRule).IsEqualTo(ReferenceTenantScopeRule.TenantContextRequired);
        await Assert.That(organizationBinding.BindingTenantScopeRule).IsEqualTo(ReferenceTenantScopeRule.TenantOwned);
    }

    [Test]
    public async Task ValidateExternalBinding_RejectsUnregisteredPairsAndScopeMismatches()
    {
        var invalidPairErrors = ReferenceTypeRegistry.ValidateExternalBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            ExternalBindingTypes.Internal.User,
            scopeTenantId: null);
        var globalScopeErrors = ReferenceTypeRegistry.ValidateExternalBinding(
            ExternalBindingTypes.External.ProviderCustomer,
            ExternalBindingTypes.Internal.Tenant,
            scopeTenantId: Guid.NewGuid());
        var tenantScopeErrors = ReferenceTypeRegistry.ValidateExternalBinding(
            ExternalBindingTypes.External.ExternalAdminUser,
            ExternalBindingTypes.Internal.User,
            scopeTenantId: null);

        await Assert.That(invalidPairErrors.Any(error => error.Contains("not registered", StringComparison.Ordinal))).IsTrue();
        await Assert.That(globalScopeErrors.Any(error => error.Contains("must not include ScopeTenantId", StringComparison.Ordinal))).IsTrue();
        await Assert.That(tenantScopeErrors.Any(error => error.Contains("requires ScopeTenantId", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task NotificationTargets_CoverEveryNotificationEntityType()
    {
        var registeredTypes = ReferenceTypeRegistry.AllNotificationTargets
            .Select(target => target.EntityType)
            .Order()
            .ToArray();
        var enumTypes = Enum.GetValues<NotificationEntityTypeEnum>()
            .Order()
            .ToArray();

        await Assert.That(registeredTypes).IsEquivalentTo(enumTypes);
    }

    [Test]
    public async Task NotificationTargets_UseGuidEntityIdsAndRetainHistoricalReferences()
    {
        foreach (var target in ReferenceTypeRegistry.AllNotificationTargets)
        {
            await Assert.That(target.Target.IdKind).IsEqualTo(ReferenceIdKind.Guid);
            await Assert.That(target.EntityIdRequiredWhenTypePresent).IsTrue();
            await Assert.That(target.CleanupBehavior).IsEqualTo(ReferenceCleanupBehavior.RetainHistoricalReference);
            await Assert.That(string.IsNullOrWhiteSpace(target.MasterCode)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(target.ValidationRule)).IsFalse();
        }
    }

    [Test]
    public async Task ValidateNotificationReference_RequiresRegisteredTypeAndGuidEntityIdShape()
    {
        var missingTypeErrors = ReferenceTypeRegistry.ValidateNotificationReference(new Explore.Domain.Notification
        {
            Id = Guid.NewGuid(),
            User = null!,
            NotificationType = null!,
            Title = "Detached link",
            DeduplicationKey = "reference-test-detached",
            EntityId = Guid.NewGuid().ToString(),
            NotificationScope = null!,
            Tenant = null!,
        });
        var invalidGuidErrors = ReferenceTypeRegistry.ValidateNotificationReference(new Explore.Domain.Notification
        {
            Id = Guid.NewGuid(),
            User = null!,
            NotificationType = null!,
            Title = "Bad link",
            DeduplicationKey = "reference-test-bad-link",
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            EntityId = "event-123",
            NotificationScope = null!,
            Tenant = null!,
        });

        await Assert.That(missingTypeErrors.Any(error => error.Contains("NotificationEntityTypeId is null", StringComparison.Ordinal))).IsTrue();
        await Assert.That(invalidGuidErrors.Any(error => error.Contains("must be a Guid", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task CustomPropertyTargets_CoverEveryEntityTypeName()
    {
        var registeredTypes = ReferenceTypeRegistry.AllCustomPropertyTargets
            .Select(target => target.EntityTypeName)
            .Order()
            .ToArray();
        var enumTypes = Enum.GetValues<EntityTypeName>()
            .Order()
            .ToArray();

        await Assert.That(registeredTypes).IsEquivalentTo(enumTypes);
    }

    [Test]
    public async Task CustomPropertyTargets_SharedDefinitionsAreOnlyForOrganizationsAndGroups()
    {
        await Assert.That(ReferenceTypeRegistry.SupportsSharedCustomPropertyDefinitions(EntityTypeName.Event)).IsFalse();
        await Assert.That(ReferenceTypeRegistry.SupportsSharedCustomPropertyDefinitions(EntityTypeName.Organization)).IsTrue();
        await Assert.That(ReferenceTypeRegistry.SupportsSharedCustomPropertyDefinitions(EntityTypeName.Group)).IsTrue();

        ReferenceTypeRegistry.TryGetCustomPropertyTarget(EntityTypeName.Event, out var eventTarget);

        await Assert.That(eventTarget.StorageModel).Contains(nameof(Explore.Domain.EventCustomPropertyDefinition));
        await Assert.That(eventTarget.CleanupBehavior).IsEqualTo(ReferenceCleanupBehavior.CascadeWithOwner);
        await Assert.That(string.IsNullOrWhiteSpace(eventTarget.ValidationRule)).IsFalse();
    }

    [Test]
    public async Task ValidateSharedCustomPropertyDefinition_RejectsEventSharedDefinitions()
    {
        var eventErrors = ReferenceTypeRegistry.ValidateSharedCustomPropertyDefinition(EntityTypeName.Event);
        var organizationErrors = ReferenceTypeRegistry.ValidateSharedCustomPropertyDefinition(EntityTypeName.Organization);

        await Assert.That(eventErrors.Any(error => error.Contains("does not support shared custom-property definitions", StringComparison.Ordinal))).IsTrue();
        await Assert.That(organizationErrors.Count).IsEqualTo(0);
    }

    private static IReadOnlyCollection<string> PublicStringConstants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();
}
