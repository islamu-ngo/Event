using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Federation;
using Explore.Application.Settings.Groups;

namespace Explore.Application.Services.Lifecycle;

/// <summary>
/// Default implementation of <see cref="IEventLifecyclePolicyProvider"/>.
/// Composes hard invariants with optional tenant/instance policy overrides via
/// <see cref="ITenantPolicySettingService"/> and the governed ATProto publication profile.
/// </summary>
public sealed class EventLifecyclePolicyProvider : IEventLifecyclePolicyProvider
{
    private readonly ITenantPolicySettingService _tenantPolicySettingService;
    private readonly AtprotoEventGovernanceResolver _atprotoGovernanceResolver;

    public EventLifecyclePolicyProvider(
        ITenantPolicySettingService tenantPolicySettingService,
        AtprotoEventGovernanceResolver atprotoGovernanceResolver)
    {
        _tenantPolicySettingService = tenantPolicySettingService;
        _atprotoGovernanceResolver = atprotoGovernanceResolver;
    }

    /// <inheritdoc />
    public async Task<EventLifecyclePolicy> GetEffectivePolicyAsync(
        Guid? tenantId,
        ValidationProfile profile,
        CancellationToken cancellationToken)
    {
        if (tenantId is null)
        {
            var unscopedProfile = profile == ValidationProfile.EventPublishCommunityLexicon
                ? ValidationProfile.EventPublish
                : profile;
            return BuildHardInvariantPolicy(unscopedProfile);
        }

        var effectiveProfile = profile;
        if (profile is ValidationProfile.EventPublish or ValidationProfile.EventPublishCommunityLexicon)
        {
            AtprotoEventGovernance governance = await _atprotoGovernanceResolver.ResolveAsync(
                tenantId.Value,
                userId: null,
                cancellationToken);
            effectiveProfile = governance.EventsEnabled
                && governance.ValidationProfile == AtprotoFederationSettingGroup.CommunityLexiconProfile
                ? ValidationProfile.EventPublishCommunityLexicon
                : ValidationProfile.EventPublish;
        }

        EventLifecyclePolicy basePolicy = BuildHardInvariantPolicy(effectiveProfile);
        var tenantPolicy = await _tenantPolicySettingService.ReadEffectiveTenantSettingsAsync(tenantId.Value);

        return basePolicy with
        {
            RequiresApproval = tenantPolicy.RequireEventApproval,
            Source = "tenant-aware"
        };
    }

    private static EventLifecyclePolicy BuildHardInvariantPolicy(ValidationProfile profile)
    {
        return profile switch
        {
            ValidationProfile.EventDraftCreate => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.EventNativeSubmit => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status,
                    EventFieldKey.Visibility,
                    EventFieldKey.Format,
                    EventFieldKey.Type,
                    EventFieldKey.AudienceGender,
                    EventFieldKey.AudienceAge
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.EventImportCreate => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status,
                    EventFieldKey.ProvenanceSource,
                    EventFieldKey.ProvenanceExternalId
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.EventArchiveCreate => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.EventPublish => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status,
                    EventFieldKey.Visibility,
                    EventFieldKey.Format,
                    EventFieldKey.ScheduleSessions,
                    EventFieldKey.ScheduleFirstStart
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.EventPublishCommunityLexicon => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>
                {
                    EventFieldKey.Title,
                    EventFieldKey.Tenant,
                    EventFieldKey.Owner,
                    EventFieldKey.Status
                },
                RequiredSessionFields = new HashSet<Enum>()
            },
            ValidationProfile.SessionDraftCreate => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>(),
                RequiredSessionFields = new HashSet<Enum>
                {
                    EventSessionFieldKey.ParentEvent,
                    EventSessionFieldKey.Tenant,
                    EventSessionFieldKey.Status,
                    EventSessionFieldKey.Title
                }
            },
            ValidationProfile.SessionSchedule => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>(),
                RequiredSessionFields = new HashSet<Enum>
                {
                    EventSessionFieldKey.ParentEvent,
                    EventSessionFieldKey.Tenant,
                    EventSessionFieldKey.Status,
                    EventSessionFieldKey.ScheduleStart,
                    EventSessionFieldKey.ScheduleEnd
                }
            },
            ValidationProfile.SessionPublish => new EventLifecyclePolicy
            {
                Profile = profile,
                RequiredEventFields = new HashSet<Enum>(),
                RequiredSessionFields = new HashSet<Enum>
                {
                    EventSessionFieldKey.ParentEvent,
                    EventSessionFieldKey.Tenant,
                    EventSessionFieldKey.Status,
                    EventSessionFieldKey.Title,
                    EventSessionFieldKey.ScheduleStart,
                    EventSessionFieldKey.ScheduleEnd,
                    EventSessionFieldKey.ParentEventCompatibility
                }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown validation profile.")
        };
    }
}
