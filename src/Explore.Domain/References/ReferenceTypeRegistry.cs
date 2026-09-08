using System.Collections.Frozen;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Domain.References;

public enum ReferenceIdKind
{
    Guid = 1
}

public enum ReferenceOwnership
{
    Platform = 1,
    Tenant = 2,
    Identity = 3,
    Actor = 4
}

public enum ReferenceTenantScopeRule
{
    Global = 1,
    TenantOwned = 2,
    TenantContextRequired = 3,
    TenantContextOptional = 4
}

public enum ReferenceCleanupBehavior
{
    Restrict = 1,
    RetainHistoricalReference = 2,
    CascadeWithOwner = 3,
    PurgeWithTenant = 4
}

public sealed record ReferenceTargetDefinition(
    string Kind,
    string DomainEntityName,
    ReferenceIdKind IdKind,
    ReferenceOwnership Ownership,
    ReferenceTenantScopeRule TenantScopeRule,
    ReferenceCleanupBehavior CleanupBehavior,
    string ValidationRule);

public sealed record ExternalBindingReferenceDefinition(
    string ExternalType,
    string InternalType,
    ReferenceTargetDefinition Target,
    ReferenceTenantScopeRule BindingTenantScopeRule,
    ReferenceCleanupBehavior CleanupBehavior,
    string ValidationRule);

public sealed record NotificationReferenceDefinition(
    NotificationEntityTypeEnum EntityType,
    string MasterCode,
    ReferenceTargetDefinition Target,
    bool EntityIdRequiredWhenTypePresent,
    ReferenceCleanupBehavior CleanupBehavior,
    string ValidationRule);

public sealed record CustomPropertyReferenceDefinition(
    EntityTypeName EntityTypeName,
    ReferenceTargetDefinition Target,
    bool SupportsSharedDefinitions,
    string StorageModel,
    ReferenceCleanupBehavior CleanupBehavior,
    string ValidationRule);

/// <summary>
/// Authoritative registry for every polymorphic reference discriminator that cannot be represented by a direct FK.
/// </summary>
public static class ReferenceTypeRegistry
{
    private static readonly FrozenDictionary<string, ReferenceTargetDefinition> TargetsByKind = BuildTargets();
    private static readonly FrozenDictionary<(string ExternalType, string InternalType), ExternalBindingReferenceDefinition> ExternalBindingPairs = BuildExternalBindingPairs();
    private static readonly FrozenDictionary<string, ExternalBindingReferenceDefinition> ExternalBindingsByExternalType =
        ExternalBindingPairs.Values.ToFrozenDictionary(pair => pair.ExternalType, StringComparer.Ordinal);
    private static readonly FrozenDictionary<NotificationEntityTypeEnum, NotificationReferenceDefinition> NotificationTargets = BuildNotificationTargets();
    private static readonly FrozenDictionary<EntityTypeName, CustomPropertyReferenceDefinition> CustomPropertyTargets = BuildCustomPropertyTargets();

    public static IReadOnlyCollection<ReferenceTargetDefinition> AllTargets => TargetsByKind.Values;
    public static IReadOnlyCollection<ExternalBindingReferenceDefinition> AllExternalBindingPairs => ExternalBindingPairs.Values;
    public static IReadOnlyCollection<NotificationReferenceDefinition> AllNotificationTargets => NotificationTargets.Values;
    public static IReadOnlyCollection<CustomPropertyReferenceDefinition> AllCustomPropertyTargets => CustomPropertyTargets.Values;

    public static bool TryGetTarget(string? kind, out ReferenceTargetDefinition target)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            target = null!;
            return false;
        }

        return TargetsByKind.TryGetValue(kind, out target!);
    }

    public static ReferenceTargetDefinition GetRequiredTarget(string kind) =>
        TryGetTarget(kind, out var target)
            ? target
            : throw new KeyNotFoundException($"Unknown reference target kind '{kind}'. Add it to ReferenceTypeRegistry first.");

    public static bool TryGetExternalBindingPair(
        string? externalType,
        string? internalType,
        out ExternalBindingReferenceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(externalType) || string.IsNullOrWhiteSpace(internalType))
        {
            definition = null!;
            return false;
        }

        return ExternalBindingPairs.TryGetValue((externalType, internalType), out definition!);
    }

    public static bool TryGetExternalBindingByExternalType(
        string? externalType,
        out ExternalBindingReferenceDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(externalType))
        {
            definition = null!;
            return false;
        }

        return ExternalBindingsByExternalType.TryGetValue(externalType, out definition!);
    }

    public static bool IsAllowedExternalBindingPair(string? externalType, string? internalType) =>
        TryGetExternalBindingPair(externalType, internalType, out _);

    public static bool TryGetNotificationTarget(
        NotificationEntityTypeEnum entityType,
        out NotificationReferenceDefinition definition) =>
        NotificationTargets.TryGetValue(entityType, out definition!);

    public static bool TryGetCustomPropertyTarget(
        EntityTypeName entityTypeName,
        out CustomPropertyReferenceDefinition definition) =>
        CustomPropertyTargets.TryGetValue(entityTypeName, out definition!);

    public static bool SupportsSharedCustomPropertyDefinitions(EntityTypeName entityTypeName) =>
        TryGetCustomPropertyTarget(entityTypeName, out var definition) && definition.SupportsSharedDefinitions;

    public static IReadOnlyList<string> ValidateExternalBinding(ExternalBinding binding) =>
        ValidateExternalBinding(binding.ExternalType, binding.InternalType, binding.ScopeTenantId);

    public static IReadOnlyList<string> ValidateExternalBinding(string? externalType, string? internalType, Guid? scopeTenantId)
    {
        var errors = new List<string>();

        if (!TryGetExternalBindingPair(externalType, internalType, out var definition))
        {
            errors.Add($"External binding type pair '{externalType ?? "<null>"}' -> '{internalType ?? "<null>"}' is not registered.");
            return errors;
        }

        AddTenantScopeErrors(errors, definition.BindingTenantScopeRule, scopeTenantId, "External binding");
        return errors;
    }

    public static IReadOnlyList<string> ValidateNotificationReference(Notification notification)
    {
        var errors = new List<string>();

        if (!notification.NotificationEntityTypeId.HasValue)
        {
            if (!string.IsNullOrWhiteSpace(notification.EntityId))
            {
                errors.Add("Notification EntityId must be null when NotificationEntityTypeId is null.");
            }

            return errors;
        }

        var entityTypeId = notification.NotificationEntityTypeId.Value;
        if (!Enum.IsDefined(typeof(NotificationEntityTypeEnum), entityTypeId)
            || !TryGetNotificationTarget((NotificationEntityTypeEnum)entityTypeId, out var definition))
        {
            errors.Add($"Notification entity type '{entityTypeId}' is not registered.");
            return errors;
        }

        if (definition.EntityIdRequiredWhenTypePresent && string.IsNullOrWhiteSpace(notification.EntityId))
        {
            errors.Add("Notification EntityId is required when NotificationEntityTypeId is present.");
            return errors;
        }

        if (definition.Target.IdKind == ReferenceIdKind.Guid
            && !Guid.TryParse(notification.EntityId, out _))
        {
            errors.Add($"Notification EntityId must be a Guid for {definition.EntityType} references.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSharedCustomPropertyDefinition(EntityTypeName entityTypeName)
    {
        if (!TryGetCustomPropertyTarget(entityTypeName, out var definition))
        {
            return [$"Custom-property entity type '{entityTypeName}' is not registered."];
        }

        return definition.SupportsSharedDefinitions
            ? []
            : [$"{entityTypeName} does not support shared custom-property definitions. Use {definition.StorageModel}."];
    }

    public static IReadOnlyList<string> ValidateSharedCustomPropertyDefinition(CustomPropertyDefinition definition) =>
        ValidateSharedCustomPropertyDefinition(definition.EntityTypeName);

    private static void AddTenantScopeErrors(
        List<string> errors,
        ReferenceTenantScopeRule rule,
        Guid? scopeTenantId,
        string context)
    {
        switch (rule)
        {
            case ReferenceTenantScopeRule.Global when scopeTenantId.HasValue:
                errors.Add($"{context} must not include ScopeTenantId for a global reference.");
                break;
            case ReferenceTenantScopeRule.TenantOwned or ReferenceTenantScopeRule.TenantContextRequired when !scopeTenantId.HasValue:
                errors.Add($"{context} requires ScopeTenantId for a tenant-scoped reference.");
                break;
        }
    }

    private static FrozenDictionary<string, ReferenceTargetDefinition> BuildTargets()
    {
        var definitions = new[]
        {
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.Tenant,
                nameof(Tenant),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Platform,
                ReferenceTenantScopeRule.Global,
                ReferenceCleanupBehavior.Restrict,
                "Tenant references must use the tenant aggregate Guid and must not infer tenant context from another row."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.User,
                nameof(User),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Identity,
                ReferenceTenantScopeRule.Global,
                ReferenceCleanupBehavior.Restrict,
                "User references must use the global user Guid; tenant-local meaning requires an explicit tenant context."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.TenantUser,
                nameof(TenantUser),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.CascadeWithOwner,
                "TenantUser references must use a Guid whose TenantId matches the surrounding tenant context."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.TenantUserProfile,
                nameof(TenantUserProfile),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.CascadeWithOwner,
                "TenantUserProfile references must use a Guid whose parent TenantUser belongs to the same tenant."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.Actor,
                nameof(Actor),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Actor,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "Actor references must use a Guid whose TenantId matches the surrounding tenant context."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.UserExternalLogin,
                nameof(UserExternalLogin),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Identity,
                ReferenceTenantScopeRule.TenantContextRequired,
                ReferenceCleanupBehavior.CascadeWithOwner,
                "UserExternalLogin references must use the login Guid and carry the tenant context that authorized the login."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.Organization,
                nameof(Organization),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "Organization references must use a Guid whose TenantId matches the surrounding tenant context."),
            new ReferenceTargetDefinition(
                ExternalBindingTypes.Internal.Group,
                nameof(Group),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "Group references must use a Guid whose TenantId matches the surrounding tenant context."),
            new ReferenceTargetDefinition(
                nameof(Event),
                nameof(Event),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "Event references must use a Guid whose TenantId matches the surrounding tenant context."),
            new ReferenceTargetDefinition(
                nameof(EventRegistration),
                nameof(EventRegistration),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "EventRegistration references must use a Guid whose TenantId and EventId match the surrounding event context."),
            new ReferenceTargetDefinition(
                nameof(EventSession),
                nameof(EventSession),
                ReferenceIdKind.Guid,
                ReferenceOwnership.Tenant,
                ReferenceTenantScopeRule.TenantOwned,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                "EventSession references must use a Guid whose TenantId and EventId match the surrounding event context."),
        };

        return definitions.ToFrozenDictionary(definition => definition.Kind, StringComparer.Ordinal);
    }

    private static FrozenDictionary<(string ExternalType, string InternalType), ExternalBindingReferenceDefinition> BuildExternalBindingPairs()
    {
        var definitions = new[]
        {
            Binding(
                ExternalBindingTypes.External.ProviderCustomer,
                ExternalBindingTypes.Internal.Tenant,
                ReferenceTenantScopeRule.Global,
                "Provider customer bindings point at the provisioned tenant and must have ScopeTenantId null."),
            Binding(
                ExternalBindingTypes.External.ManagedTenantProvisioningOperation,
                ExternalBindingTypes.Internal.Tenant,
                ReferenceTenantScopeRule.TenantContextRequired,
                "Managed provisioning operation bindings prove which durable operation created the scoped tenant."),
            Binding(
                ExternalBindingTypes.External.ExternalAdminUser,
                ExternalBindingTypes.Internal.User,
                ReferenceTenantScopeRule.TenantContextRequired,
                "External admin user bindings point at the global user and must carry the provisioned tenant as ScopeTenantId."),
            Binding(
                ExternalBindingTypes.External.ExternalAdminTenantUser,
                ExternalBindingTypes.Internal.TenantUser,
                ReferenceTenantScopeRule.TenantOwned,
                "External admin tenant-user bindings must point at tenant-local user state within ScopeTenantId."),
            Binding(
                ExternalBindingTypes.External.ExternalAdminTenantUserProfile,
                ExternalBindingTypes.Internal.TenantUserProfile,
                ReferenceTenantScopeRule.TenantOwned,
                "External admin tenant-user-profile bindings must point at the profile owned by the scoped tenant user."),
            Binding(
                ExternalBindingTypes.External.ExternalAdminUserActor,
                ExternalBindingTypes.Internal.Actor,
                ReferenceTenantScopeRule.TenantOwned,
                "External admin actor bindings must point at the tenant-local user actor."),
            Binding(
                ExternalBindingTypes.External.ExternalAdminUserLogin,
                ExternalBindingTypes.Internal.UserExternalLogin,
                ReferenceTenantScopeRule.TenantContextRequired,
                "External admin login bindings must point at the login row authorized for the scoped tenant."),
            Binding(
                ExternalBindingTypes.External.CustomerOrganization,
                ExternalBindingTypes.Internal.Organization,
                ReferenceTenantScopeRule.TenantOwned,
                "Customer organization bindings must point at the tenant-local organizer organization."),
            Binding(
                ExternalBindingTypes.External.CustomerOrganizationActor,
                ExternalBindingTypes.Internal.Actor,
                ReferenceTenantScopeRule.TenantOwned,
                "Customer organization actor bindings must point at the actor for the tenant-local organizer organization."),
            Binding(
                ExternalBindingTypes.External.CustomerGroup,
                ExternalBindingTypes.Internal.Group,
                ReferenceTenantScopeRule.TenantOwned,
                "Customer group bindings must point at the tenant-local organizer group."),
            Binding(
                ExternalBindingTypes.External.CustomerGroupActor,
                ExternalBindingTypes.Internal.Actor,
                ReferenceTenantScopeRule.TenantOwned,
                "Customer group actor bindings must point at the actor for the tenant-local organizer group."),
        };

        return definitions.ToFrozenDictionary(definition => (definition.ExternalType, definition.InternalType));

        static ExternalBindingReferenceDefinition Binding(
            string externalType,
            string internalType,
            ReferenceTenantScopeRule tenantScopeRule,
            string validationRule)
        {
            var target = GetRequiredTarget(internalType);
            return new ExternalBindingReferenceDefinition(
                externalType,
                internalType,
                target,
                tenantScopeRule,
                ReferenceCleanupBehavior.PurgeWithTenant,
                validationRule);
        }
    }

    private static FrozenDictionary<NotificationEntityTypeEnum, NotificationReferenceDefinition> BuildNotificationTargets()
    {
        var definitions = new[]
        {
            Notification(NotificationEntityTypeEnum.Event, "EVENT", nameof(Event), "Notification Event links must store an Event Guid from the notification tenant."),
            Notification(NotificationEntityTypeEnum.Organization, "ORGANIZATION", ExternalBindingTypes.Internal.Organization, "Notification Organization links must store an Organization Guid from the notification tenant."),
            Notification(NotificationEntityTypeEnum.Group, "GROUP", ExternalBindingTypes.Internal.Group, "Notification Group links must store a Group Guid from the notification tenant."),
            Notification(NotificationEntityTypeEnum.EventRegistration, "EVENT_REGISTRATION", nameof(EventRegistration), "Notification EventRegistration links must store an EventRegistration Guid from the notification tenant."),
            Notification(NotificationEntityTypeEnum.EventSession, "EVENT_SESSION", nameof(EventSession), "Notification EventSession links must store an EventSession Guid from the notification tenant."),
            Notification(NotificationEntityTypeEnum.User, "USER", ExternalBindingTypes.Internal.User, "Notification User links must store a User Guid and remain scoped by the notification TenantId."),
        };

        return definitions.ToFrozenDictionary(definition => definition.EntityType);

        static NotificationReferenceDefinition Notification(
            NotificationEntityTypeEnum entityType,
            string masterCode,
            string targetKind,
            string validationRule) =>
            new(
                entityType,
                masterCode,
                GetRequiredTarget(targetKind),
                EntityIdRequiredWhenTypePresent: true,
                ReferenceCleanupBehavior.RetainHistoricalReference,
                validationRule);
    }

    private static FrozenDictionary<EntityTypeName, CustomPropertyReferenceDefinition> BuildCustomPropertyTargets()
    {
        var definitions = new[]
        {
            CustomProperty(
                EntityTypeName.Event,
                nameof(Event),
                supportsSharedDefinitions: false,
                "Event custom properties use EventCustomPropertyDefinition/EventCustomPropertyValue and event-template materialization, not shared CustomPropertyDefinition rows.",
                ReferenceCleanupBehavior.CascadeWithOwner,
                "Event-scoped custom-property values must use an Event Guid and validate through the event-specific runtime model."),
            CustomProperty(
                EntityTypeName.Organization,
                ExternalBindingTypes.Internal.Organization,
                supportsSharedDefinitions: true,
                "Shared CustomPropertyDefinition and CustomPropertyValue rows.",
                ReferenceCleanupBehavior.CascadeWithOwner,
                "Shared organization custom-property values must use an Organization Guid from the same tenant as the definition."),
            CustomProperty(
                EntityTypeName.Group,
                ExternalBindingTypes.Internal.Group,
                supportsSharedDefinitions: true,
                "Shared CustomPropertyDefinition and CustomPropertyValue rows.",
                ReferenceCleanupBehavior.CascadeWithOwner,
                "Shared group custom-property values must use a Group Guid from the same tenant as the definition."),
        };

        return definitions.ToFrozenDictionary(definition => definition.EntityTypeName);

        static CustomPropertyReferenceDefinition CustomProperty(
            EntityTypeName entityTypeName,
            string targetKind,
            bool supportsSharedDefinitions,
            string storageModel,
            ReferenceCleanupBehavior cleanupBehavior,
            string validationRule) =>
            new(
                entityTypeName,
                GetRequiredTarget(targetKind),
                supportsSharedDefinitions,
                storageModel,
                cleanupBehavior,
                validationRule);
    }
}
