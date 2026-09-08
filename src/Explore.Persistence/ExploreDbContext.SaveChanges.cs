using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.Secrets;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    public override int SaveChanges() => SaveChanges(acceptAllChangesOnSuccess: true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareTrackedEntities();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareTrackedEntities();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    internal async Task<int> SavePrivacyErasureChangesAsync(CancellationToken cancellationToken)
    {
        PrepareTrackedEntities();
        foreach (var entry in ChangeTracker.Entries()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case Actor actor when actor.IsDeleted
                    && actor.UserId is null
                    && actor.OrganizationId is null
                    && actor.GroupId is null
                    && actor.ExternalActorSubjectId is null
                    && actor.ServicePrincipalId is null:
                    ClearAuditOwnership(actor);
                    break;

                case AtprotoIdentity identity when identity.IsDeleted
                    && identity.Did.StartsWith("did:deleted:", StringComparison.Ordinal):
                    ClearAuditOwnership(identity);
                    break;
            }
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
    }

    private void PrepareTrackedEntities()
    {
        ValidateEventLocationCarrierConsistency();
        ValidateRegistrationProviderConnectionSecrets();
        ValidateRegistrationProviderBindingSecrets();
        ValidateRegistrationAttemptProviderRevisions();
        ValidateAdmissionIntegrity();
        PopulateMySqlPortableComputedValues();
        var userId = GetCurrentUserId();
        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is Explore.Domain.WebhookAuditEvent
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Webhook audit events are append-only and cannot be modified or deleted.");
            }

            if (entry.Entity is AdmissionCheckInEvent
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Admission check-in events are append-only and cannot be modified or deleted.");
            }

            if (entry.Entity is Explore.Domain.ConfigurationManifestOperation
                    or Explore.Domain.ConfigurationManifestTenantResult
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException(
                    "Configuration manifest audit evidence is append-only and cannot be modified or deleted.");
            }

            if (entry.Entity is Explore.Domain.EventLocationDisclosureAudit
                    or Explore.Domain.EventLocationExactReadAudit
                    or Explore.Domain.PrivacyErasureReplayCheckpoint
                && entry.State is EntityState.Modified or EntityState.Deleted)
            {
                throw new InvalidOperationException("Event location privacy evidence is append-only and cannot be modified or deleted.");
            }

            if (entry.Entity is Explore.Domain.Event eventEntity &&
                entry.State == EntityState.Added &&
                string.IsNullOrWhiteSpace(eventEntity.PublicCode))
            {
                eventEntity.PublicCode = GeneratePublicCode();
            }

            if (entry.Entity is IConcurrencyAware concurrencyAware &&
                (entry.State == EntityState.Added || entry.State == EntityState.Modified) &&
                (entry.State == EntityState.Modified || concurrencyAware.ConcurrencyStamp == Guid.Empty))
            {
                concurrencyAware.ConcurrencyStamp = Guid.CreateVersion7();
            }

            if (entry.Entity is IAuditableEntity auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        if (auditable.CreatedAt == default)
                        {
                            auditable.CreatedAt = now;
                        }
                        auditable.CreatedBy = userId ?? auditable.CreatedBy;
                        break;

                    case EntityState.Modified:
                        if (auditable.UpdatedAt == null || auditable.UpdatedAt == default(DateTime))
                        {
                            auditable.UpdatedAt = now;
                        }

                        if (userId.HasValue)
                        {
                            auditable.UpdatedBy = userId;
                        }
                        break;
                }
            }

            if (entry.Entity is ISoftDeletable deletable && entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;

                deletable.IsDeleted = true;
                deletable.DeletedAt = now;
                deletable.DeletedBy = userId;

                if (entry.Entity is IAuditableEntity auditableDeleted)
                {
                    auditableDeleted.UpdatedAt = now;
                    auditableDeleted.UpdatedBy = userId;
                }
            }
        }

    }

    private void ValidateRegistrationProviderConnectionSecrets()
    {
        foreach (var entry in ChangeTracker.Entries<RegistrationProviderConnection>()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            RegistrationProviderConnection connection = entry.Entity;
            EnsureProviderSecretPurpose(connection.TenantId, connection.ApiTokenSecretBindingId,
                SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken, nameof(connection.ApiTokenSecretBindingId));
            EnsureProviderSecretPurpose(connection.TenantId, connection.WebhookSecretBindingId,
                SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret, nameof(connection.WebhookSecretBindingId));
        }
    }

    private void ValidateRegistrationProviderBindingSecrets()
    {
        foreach (var entry in ChangeTracker.Entries<RegistrationProviderBinding>()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            RegistrationProviderBinding binding = entry.Entity;
            EnsureProviderBindingWebhookSecretPurpose(binding.TenantId, binding.Id, binding.WebhookSecretBindingId);
        }
    }

    private void EnsureProviderSecretPurpose(Guid tenantId, Guid? bindingId, string expectedKey, string parameterName)
    {
        if (bindingId is null)
        {
            return;
        }

        SecretBinding? binding = ChangeTracker.Entries<SecretBinding>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(candidate => candidate.Id == bindingId)
            ?? SecretBindings
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .FirstOrDefault(candidate => candidate.Id == bindingId);
        if (binding is null || binding.ScopeId != tenantId || binding.SettingKey != expectedKey || string.IsNullOrWhiteSpace(binding.Qualifier))
        {
            throw new InvalidOperationException($"Registration provider connection {parameterName} must reference a tenant-qualified {expectedKey} SecretBinding.");
        }
    }

    private void EnsureProviderBindingWebhookSecretPurpose(Guid tenantId, Guid bindingId, Guid? secretBindingId)
    {
        if (secretBindingId is null)
        {
            return;
        }

        string expectedQualifier = bindingId.ToString("N", CultureInfo.InvariantCulture);
        SecretBinding? secret = ChangeTracker.Entries<SecretBinding>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(candidate => candidate.Id == secretBindingId)
            ?? SecretBindings
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                .FirstOrDefault(candidate => candidate.Id == secretBindingId);
        if (secret is null || secret.ScopeId != tenantId ||
            secret.SettingKey != SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret ||
            !string.Equals(secret.Qualifier, expectedQualifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Registration provider binding WebhookSecretBindingId must reference its tenant-qualified registration provider webhook SecretBinding.");
        }
    }

    private void ValidateRegistrationAttemptProviderRevisions()
    {
        foreach (var entry in ChangeTracker.Entries<RegistrationAttempt>()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            RegistrationAttempt attempt = entry.Entity;
            if (attempt.RegistrationProviderBindingId is null && attempt.ProviderMappingRevisionHash is null)
            {
                continue;
            }

            RegistrationProviderBinding? binding = ChangeTracker.Entries<RegistrationProviderBinding>()
                .Select(item => item.Entity)
                .FirstOrDefault(candidate => candidate.TenantId == attempt.TenantId && candidate.Id == attempt.RegistrationProviderBindingId)
                ?? RegistrationProviderBindings.IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
                    .FirstOrDefault(candidate => candidate.TenantId == attempt.TenantId && candidate.Id == attempt.RegistrationProviderBindingId);
            if (binding?.PublishedMappingRevisionHash?.Value != attempt.ProviderMappingRevisionHash?.Value)
            {
                throw new InvalidOperationException("Registration attempts must pin the published provider binding mapping revision.");
            }
        }
    }

    private void ValidateAdmissionIntegrity()
    {
        foreach (var entry in ChangeTracker.Entries<AdmissionTarget>()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            AdmissionTarget target = entry.Entity;
            bool validScope = (AdmissionTargetTypeEnum)target.AdmissionTargetTypeId switch
            {
                AdmissionTargetTypeEnum.Event =>
                    target.EventDayId is null && target.EventSessionId is null && target.ScopeId == target.EventId,
                AdmissionTargetTypeEnum.EventDay =>
                    target.EventDayId.HasValue && target.EventSessionId is null && target.ScopeId == target.EventDayId,
                AdmissionTargetTypeEnum.EventSession =>
                    target.EventDayId is null && target.EventSessionId.HasValue && target.ScopeId == target.EventSessionId,
                _ => false
            };
            if (!validScope)
            {
                throw new InvalidOperationException(
                    "Admission targets must persist one known canonical event, day, or session scope.");
            }
        }

        foreach (var entry in ChangeTracker.Entries<AdmissionCheckInEvent>()
                     .Where(item => item.State == EntityState.Added))
        {
            AdmissionCheckInEvent fact = entry.Entity;
            bool validAuthority = fact.ActorId.HasValue != fact.ScannerCapabilityId.HasValue;
            bool validShape = (AdmissionCheckInActionEnum)fact.AdmissionCheckInActionId switch
            {
                AdmissionCheckInActionEnum.CheckIn =>
                    fact.AdmissionCheckInUndoReasonCodeId is null &&
                    fact.CompensatedCheckInEventId is null,
                AdmissionCheckInActionEnum.Undo =>
                    fact.AdmissionCheckInUndoReasonCodeId.HasValue &&
                    Enum.IsDefined(
                        typeof(AdmissionCheckInUndoReasonCodeEnum),
                        fact.AdmissionCheckInUndoReasonCodeId.Value) &&
                    fact.CompensatedCheckInEventId.HasValue &&
                    fact.CompensatedCheckInEventId != fact.Id,
                _ => false
            };
            if (!validAuthority || !validShape)
            {
                throw new InvalidOperationException(
                    "Admission facts require a known action, exactly one authority, and a valid action/reason/compensation shape.");
            }
        }
    }

    private void PopulateMySqlPortableComputedValues()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case RegistrationOrder order:
                    entry.Property("RegistrationWorkflowVersionKey").CurrentValue =
                        order.RegistrationWorkflowVersionId ?? Guid.Empty;
                    break;
                case RegistrationAttempt attempt:
                    entry.Property("RegistrationProviderBindingKey").CurrentValue =
                        attempt.RegistrationProviderBindingId ?? Guid.Empty;
                    entry.Property("ProviderMappingRevisionHashKey").CurrentValue =
                        attempt.ProviderMappingRevisionHash?.Value ?? string.Empty;
                    break;
                case RegistrationChannel channel:
                    entry.Property("RegistrationProviderBindingKey").CurrentValue =
                        channel.RegistrationProviderBindingId ?? Guid.Empty;
                    break;
                case RegistrationRequirement requirement:
                    entry.Property(nameof(RegistrationRequirement.AppliesToSubjectKey)).CurrentValue =
                        requirement.AppliesToSubjectId ?? Guid.Empty;
                    break;
                case RegistrationAnswer answer:
                    entry.Property(nameof(RegistrationAnswer.RequirementSubjectKey)).CurrentValue =
                        answer.RequirementSubjectId ?? Guid.Empty;
                    entry.Property(nameof(RegistrationAnswer.EffectiveSubjectIdentity)).CurrentValue =
                        answer.OrderSubjectId ?? answer.PurchaserSubjectId ?? answer.ParticipantSubjectId ??
                        answer.TicketAssignmentSubjectId ?? answer.SessionSelectionSubjectId ?? Guid.Empty;
                    break;
            }
        }

        if (!StringComparer.Ordinal.Equals(Database.ProviderName, "Microting.EntityFrameworkCore.MySql"))
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case ExternalBinding externalBinding:
                    entry.Property("ExternalGlobalUniquenessHash").CurrentValue = externalBinding.ScopeTenantId is null
                        ? ComputeMySqlUniquenessHash(externalBinding.ProviderKey, externalBinding.ExternalSystem, externalBinding.ExternalType, externalBinding.ExternalId)
                        : null;
                    entry.Property("ExternalTenantUniquenessHash").CurrentValue = externalBinding.ScopeTenantId is { } externalScope
                        ? ComputeMySqlUniquenessHash(externalBinding.ProviderKey, externalBinding.ExternalSystem, externalBinding.ExternalType, externalBinding.ExternalId, externalScope.ToString("D"))
                        : null;
                    entry.Property("InternalGlobalUniquenessHash").CurrentValue = externalBinding.ScopeTenantId is null
                        ? ComputeMySqlUniquenessHash(externalBinding.ProviderKey, externalBinding.ExternalSystem, externalBinding.InternalType, externalBinding.InternalId.ToString("D"))
                        : null;
                    entry.Property("InternalTenantUniquenessHash").CurrentValue = externalBinding.ScopeTenantId is { } internalScope
                        ? ComputeMySqlUniquenessHash(externalBinding.ProviderKey, externalBinding.ExternalSystem, externalBinding.InternalType, externalBinding.InternalId.ToString("D"), internalScope.ToString("D"))
                        : null;
                    break;
                case StorageObject storageObject:
                    entry.Property("ProviderObjectKeyUniquenessHash").CurrentValue = storageObject.ObjectKey is { } objectKey
                        ? ComputeMySqlUniquenessHash(storageObject.Provider, objectKey)
                        : null;
                    break;
                case WebPushSubscription webPushSubscription:
                    var active = webPushSubscription.IsActive && !webPushSubscription.IsDeleted;
                    entry.Property("ActiveEndpointUniquenessHash").CurrentValue = active
                        ? ComputeMySqlUniquenessHash(webPushSubscription.Endpoint)
                        : null;
                    entry.Property("ActiveUserDeviceUniquenessHash").CurrentValue = active
                        ? ComputeMySqlUniquenessHash(
                            webPushSubscription.TenantId.ToString("D"),
                            webPushSubscription.UserId.ToString("D"),
                            webPushSubscription.DeviceIdentifier)
                        : null;
                    break;
                case WebhookConsumer consumer:
                    entry.Property(nameof(WebhookConsumer.ConfigurationScopeId)).CurrentValue =
                        consumer.TenantId ?? consumer.InstanceId ?? Guid.Empty;
                    break;
                case WebhookEndpoint endpoint:
                    entry.Property(nameof(WebhookEndpoint.ConfigurationScopeId)).CurrentValue =
                        endpoint.TenantId ?? endpoint.InstanceId ?? Guid.Empty;
                    break;
                case WebhookEndpointSubscription subscription:
                    entry.Property(nameof(WebhookEndpointSubscription.ConfigurationScopeId)).CurrentValue =
                        subscription.TenantId ?? subscription.InstanceId ?? Guid.Empty;
                    break;
                case WebhookConsumerProviderBinding binding:
                    entry.Property(nameof(WebhookConsumerProviderBinding.ConfigurationScopeId)).CurrentValue =
                        binding.TenantId ?? binding.InstanceId;
                    var providerKind = binding.ProviderKindId.ToString(CultureInfo.InvariantCulture);
                    entry.Property("ProviderEnvironmentApplicationUidHash").CurrentValue =
                        ComputeMySqlUniquenessHash(providerKind, binding.NormalizedEnvironment, binding.NormalizedApplicationUid);
                    entry.Property("ProviderEnvironmentExternalAppHash").CurrentValue =
                        binding.NormalizedExternalApplicationId is { } externalApplicationId
                            ? ComputeMySqlUniquenessHash(providerKind, binding.NormalizedEnvironment, externalApplicationId)
                            : null;
                    entry.Property("ProviderApplicationIdentityHash").CurrentValue =
                        binding.NormalizedExternalApplicationId is { } normalizedExternalApplicationId
                            ? ComputeMySqlUniquenessHash(
                                providerKind,
                                binding.NormalizedEnvironment,
                                normalizedExternalApplicationId,
                                binding.NormalizedApplicationUid)
                            : null;
                    break;
                case RegistrationOrder order:
                    entry.Property("RegistrationWorkflowVersionKey").CurrentValue =
                        order.RegistrationWorkflowVersionId ?? Guid.Empty;
                    break;
                case RegistrationAttempt attempt:
                    entry.Property("RegistrationProviderBindingKey").CurrentValue =
                        attempt.RegistrationProviderBindingId ?? Guid.Empty;
                    entry.Property("ProviderMappingRevisionHashKey").CurrentValue =
                        attempt.ProviderMappingRevisionHash?.Value ?? string.Empty;
                    break;
                case RegistrationChannel channel:
                    entry.Property("RegistrationProviderBindingKey").CurrentValue =
                        channel.RegistrationProviderBindingId ?? Guid.Empty;
                    break;
                case RegistrationRequirement requirement:
                    entry.Property(nameof(RegistrationRequirement.AppliesToSubjectKey)).CurrentValue =
                        requirement.AppliesToSubjectId ?? Guid.Empty;
                    break;
                case RegistrationAnswer answer:
                    entry.Property(nameof(RegistrationAnswer.RequirementSubjectKey)).CurrentValue =
                        answer.RequirementSubjectId ?? Guid.Empty;
                    entry.Property(nameof(RegistrationAnswer.EffectiveSubjectIdentity)).CurrentValue =
                        answer.OrderSubjectId ?? answer.PurchaserSubjectId ?? answer.ParticipantSubjectId ??
                        answer.TicketAssignmentSubjectId ?? answer.SessionSelectionSubjectId ?? Guid.Empty;
                    break;
            }
        }
    }

    internal static byte[] ComputeMySqlUniquenessHash(params string[] components)
    {
        var canonical = new StringBuilder();
        foreach (var component in components)
        {
            var byteLength = Encoding.UTF8.GetByteCount(component);
            canonical.Append(byteLength.ToString("D10", CultureInfo.InvariantCulture));
            canonical.Append(':');
            canonical.Append(component);
        }

        return SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
    }

    private void ValidateEventLocationCarrierConsistency()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(item => item.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case EventSession session when session.EventLocationId.HasValue:
                    ValidateEventCarrier(
                        nameof(EventSession),
                        session.TenantId,
                        session.EventId,
                        session.EventLocationId.Value,
                        session.LocationId,
                        session.RoomId);
                    break;
                case EventSessionGroup group when group.EventLocationId.HasValue:
                    ValidateEventCarrier(
                        nameof(EventSessionGroup),
                        group.TenantId,
                        group.EventId,
                        group.EventLocationId.Value,
                        group.LocationId,
                        group.RoomId);
                    break;
                case EventAgendaItem agendaItem when agendaItem.EventLocationId.HasValue:
                    ValidateEventCarrier(
                        nameof(EventAgendaItem),
                        agendaItem.TenantId,
                        agendaItem.EventId,
                        agendaItem.EventLocationId.Value,
                        agendaItem.LocationId,
                        agendaItem.RoomId);
                    break;
                case EventSessionAgendaItem sessionAgendaItem when sessionAgendaItem.EventLocationId.HasValue:
                    ValidateSessionAgendaCarrier(sessionAgendaItem);
                    break;
            }
        }
    }

    private void ValidateEventCarrier(
        string carrierName,
        Guid tenantId,
        Guid eventId,
        Guid eventLocationId,
        Guid? locationId,
        Guid? roomId)
    {
        RequireCarrierTenant(tenantId);
        EventLocation eventLocation = FindActiveEventLocation(tenantId, eventLocationId);
        if (eventLocation.EventId != eventId)
        {
            throw new InvalidOperationException($"{carrierName} and EventLocation must belong to the same event.");
        }

        ValidatePhysicalKeys(carrierName, tenantId, eventLocation, locationId, roomId);
    }

    private void ValidateSessionAgendaCarrier(EventSessionAgendaItem carrier)
    {
        RequireCarrierTenant(carrier.TenantId);
        EventLocation eventLocation = FindActiveEventLocation(carrier.TenantId, carrier.EventLocationId!.Value);
        EventSession session = ChangeTracker.Entries<EventSession>()
            .Where(item => item.State != EntityState.Deleted)
            .Select(item => item.Entity)
            .SingleOrDefault(item => item.TenantId == carrier.TenantId && item.Id == carrier.EventSessionId)
            ?? EventSessions.AsNoTracking().SingleOrDefault(
                item => item.TenantId == carrier.TenantId && item.Id == carrier.EventSessionId)
            ?? throw new InvalidOperationException("EventSessionAgendaItem requires an active session in the current tenant.");
        if (session.EventId != eventLocation.EventId)
        {
            throw new InvalidOperationException("EventSessionAgendaItem session and EventLocation must belong to the same event.");
        }

        ValidatePhysicalKeys(
            nameof(EventSessionAgendaItem),
            carrier.TenantId,
            eventLocation,
            carrier.LocationId,
            null);
    }

    private EventLocation FindActiveEventLocation(Guid tenantId, Guid eventLocationId)
    {
        EventLocation? eventLocation = ChangeTracker.Entries<EventLocation>()
            .Where(item => item.State != EntityState.Deleted)
            .Select(item => item.Entity)
            .SingleOrDefault(item => item.TenantId == tenantId && item.Id == eventLocationId)
            ?? EventLocations.AsNoTracking().SingleOrDefault(
                item => item.TenantId == tenantId && item.Id == eventLocationId);
        if (eventLocation is null || eventLocation.IsDeleted)
        {
            throw new InvalidOperationException("A carrier requires an active EventLocation in the current tenant.");
        }

        return eventLocation;
    }

    private void ValidatePhysicalKeys(
        string carrierName,
        Guid tenantId,
        EventLocation eventLocation,
        Guid? locationId,
        Guid? roomId)
    {
        if (locationId != eventLocation.LocationId)
        {
            throw new InvalidOperationException($"{carrierName} LocationId must match its EventLocation.");
        }

        if (!roomId.HasValue)
        {
            return;
        }

        bool matchingRoomExists = ChangeTracker.Entries<LocationRoom>()
            .Where(item => item.State != EntityState.Deleted)
            .Select(item => item.Entity)
            .Any(item => !item.IsDeleted
                && item.TenantId == tenantId
                && item.Id == roomId.Value
                && item.LocationId == locationId)
            || LocationRooms.AsNoTracking().Any(item =>
                item.TenantId == tenantId
                && item.Id == roomId.Value
                && item.LocationId == locationId);
        if (!matchingRoomExists)
        {
            throw new InvalidOperationException($"{carrierName} room must belong to its EventLocation's physical Location.");
        }
    }

    private void RequireCarrierTenant(Guid tenantId)
    {
        if (IsTenantFilterBypassed)
        {
            return;
        }

        Guid ambientTenantId = TenantFilterTenantId
            ?? throw new InvalidOperationException("A tenant context is required to persist EventLocation carriers.");
        if (tenantId != ambientTenantId)
        {
            throw new InvalidOperationException("EventLocation carriers must belong to the current tenant.");
        }
    }

    private Guid? GetCurrentUserId()
    {
        return CurrentUserService?.UserId;
    }

    private static void ClearAuditOwnership<T>(T tombstone)
        where T : IAuditableEntity, ISoftDeletable
    {
        tombstone.CreatedBy = null;
        tombstone.UpdatedBy = null;
        tombstone.DeletedBy = null;
    }

    private static string GeneratePublicCode()
    {
        return Guid.CreateVersion7().ToString("N")[..12];
    }
}
