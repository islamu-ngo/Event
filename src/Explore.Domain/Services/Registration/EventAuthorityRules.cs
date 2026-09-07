using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class EventAuthorityRules
{
    public static EventAuthority Resolve(
        int provenanceTypeId,
        Guid publishingActorId,
        Guid? organizerActorId)
    {
        if (publishingActorId == Guid.Empty
            || !Enum.IsDefined(typeof(EventProvenanceTypeEnum), provenanceTypeId))
        {
            return default;
        }

        var hasOrganizerAuthority = organizerActorId is { } actorId && actorId != Guid.Empty;
        return new EventAuthority(
            HasListingAuthority: true,
            HasParticipationManagementAuthority: hasOrganizerAuthority,
            HasDataCollectionAuthority: hasOrganizerAuthority,
            HasCommercialAuthority: hasOrganizerAuthority);
    }

    public static IReadOnlyList<EventParticipationConfigurationValidationError> ValidateParticipationConfiguration(
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy)
    {
        var errors = new List<EventParticipationConfigurationValidationError>();

        if (!Enum.IsDefined(typeof(ParticipationHandlingModeEnum), participationHandlingModeId))
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.UnknownParticipationHandlingMode,
                "Participation handling mode is unknown."));
        }

        if (!Enum.IsDefined(typeof(AdvanceRegistrationObligationEnum), advanceRegistrationObligationId))
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.UnknownAdvanceRegistrationObligation,
                "Advance-registration obligation is unknown."));
        }

        if (identityAccessModeId.HasValue
            && !Enum.IsDefined(typeof(IdentityAccessModeEnum), identityAccessModeId.Value))
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.UnknownIdentityAccessMode,
                "Identity-access mode is unknown."));
        }

        if (guestRecoveryPolicy.HasValue
            && !Enum.IsDefined(guestRecoveryPolicy.Value))
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.UnknownGuestRecoveryPolicy,
                "Guest-recovery policy is unknown."));
        }

        if (errors.Count > 0)
        {
            return errors;
        }

        var handlingMode = (ParticipationHandlingModeEnum)participationHandlingModeId;
        var obligation = (AdvanceRegistrationObligationEnum)advanceRegistrationObligationId;

        switch (handlingMode)
        {
            case ParticipationHandlingModeEnum.InformationOnly:
            case ParticipationHandlingModeEnum.WalkIn:
                RequireNotApplicableObligation(obligation, errors);
                RequireAbsentIdentityAndRecovery(identityAccessModeId, guestRecoveryPolicy, errors);
                break;
            case ParticipationHandlingModeEnum.ExternalManaged:
                RequireAdvanceRegistrationObligation(obligation, errors);
                RequireAbsentIdentityAndRecovery(identityAccessModeId, guestRecoveryPolicy, errors);
                break;
            case ParticipationHandlingModeEnum.PlatformManaged:
                RequireAdvanceRegistrationObligation(obligation, errors);
                ValidatePlatformIdentityAndRecovery(identityAccessModeId, guestRecoveryPolicy, errors);
                break;
        }

        return errors;
    }

    public static bool IsNativeWorkflowAllowed(int participationHandlingModeId) =>
        Enum.IsDefined(typeof(ParticipationHandlingModeEnum), participationHandlingModeId)
        && participationHandlingModeId == (int)ParticipationHandlingModeEnum.PlatformManaged;

    public static bool IsPublicActionAllowed(int participationHandlingModeId, int eventPublicActionKindId)
    {
        if (!Enum.IsDefined(typeof(ParticipationHandlingModeEnum), participationHandlingModeId)
            || !Enum.IsDefined(typeof(EventPublicActionKindEnum), eventPublicActionKindId))
        {
            return false;
        }

        var handlingMode = (ParticipationHandlingModeEnum)participationHandlingModeId;
        return (EventPublicActionKindEnum)eventPublicActionKindId switch
        {
            EventPublicActionKindEnum.ExternalRegistration =>
                handlingMode == ParticipationHandlingModeEnum.ExternalManaged,
            EventPublicActionKindEnum.OptionalQuestionnaire =>
                handlingMode != ParticipationHandlingModeEnum.InformationOnly,
            _ => true
        };
    }

    private static void RequireNotApplicableObligation(
        AdvanceRegistrationObligationEnum obligation,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (obligation != AdvanceRegistrationObligationEnum.NotApplicable)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed,
                "This participation mode requires a not-applicable advance-registration obligation."));
        }
    }

    private static void RequireAdvanceRegistrationObligation(
        AdvanceRegistrationObligationEnum obligation,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (obligation == AdvanceRegistrationObligationEnum.NotApplicable)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.AdvanceRegistrationObligationNotAllowed,
                "This participation mode requires an optional or required advance-registration obligation."));
        }
    }

    private static void RequireAbsentIdentityAndRecovery(
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (identityAccessModeId.HasValue)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.IdentityAccessModeMustBeAbsent,
                "Identity-access mode must be absent for this participation mode."));
        }

        if (guestRecoveryPolicy.HasValue)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.GuestRecoveryPolicyMustBeAbsent,
                "Guest-recovery policy must be absent for this participation mode."));
        }
    }

    private static void ValidatePlatformIdentityAndRecovery(
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (!identityAccessModeId.HasValue)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.IdentityAccessModeRequired,
                "Platform-managed participation requires an identity-access mode."));
            return;
        }

        switch ((IdentityAccessModeEnum)identityAccessModeId.Value)
        {
            case IdentityAccessModeEnum.AccountRequired:
                if (guestRecoveryPolicy.HasValue)
                {
                    errors.Add(Error(
                        EventParticipationConfigurationErrorCode.GuestRecoveryPolicyMustBeAbsent,
                        "Account-required participation cannot have a guest-recovery policy."));
                }

                break;
            case IdentityAccessModeEnum.GuestAllowed:
                ValidateGuestRecoveryPolicy(guestRecoveryPolicy, errors);
                break;
            case IdentityAccessModeEnum.CapabilityTokenAllowed:
                ValidateCapabilityRecoveryPolicy(guestRecoveryPolicy, errors);
                break;
        }
    }

    private static void ValidateGuestRecoveryPolicy(
        GuestRecoveryPolicyEnum? guestRecoveryPolicy,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (!guestRecoveryPolicy.HasValue)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.GuestRecoveryPolicyRequired,
                "Guest participation requires an email recovery policy."));
        }
        else if (guestRecoveryPolicy is not GuestRecoveryPolicyEnum.VerifiedEmailRequired
                 and not GuestRecoveryPolicyEnum.UnverifiedEmailAccepted
                 and not GuestRecoveryPolicyEnum.EmailOptional)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.GuestRecoveryPolicyNotAllowed,
                "Guest participation requires a verified, unverified, or optional email recovery policy."));
        }
    }

    private static void ValidateCapabilityRecoveryPolicy(
        GuestRecoveryPolicyEnum? guestRecoveryPolicy,
        ICollection<EventParticipationConfigurationValidationError> errors)
    {
        if (!guestRecoveryPolicy.HasValue)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.GuestRecoveryPolicyRequired,
                "Capability-token participation requires an explicit recovery policy."));
        }
        else if (guestRecoveryPolicy is not GuestRecoveryPolicyEnum.CapabilityLinkOnly
                 and not GuestRecoveryPolicyEnum.NoRecovery)
        {
            errors.Add(Error(
                EventParticipationConfigurationErrorCode.GuestRecoveryPolicyNotAllowed,
                "Capability-token participation requires capability-link-only or no recovery."));
        }
    }

    private static EventParticipationConfigurationValidationError Error(
        EventParticipationConfigurationErrorCode code,
        string message) => new(code, message);
}

public readonly record struct EventAuthority(
    bool HasListingAuthority,
    bool HasParticipationManagementAuthority,
    bool HasDataCollectionAuthority,
    bool HasCommercialAuthority);
