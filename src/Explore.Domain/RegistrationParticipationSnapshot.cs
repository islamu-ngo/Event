using Explore.Domain.Enums;

namespace Explore.Domain;

public sealed record RegistrationParticipationSnapshot
{
    private RegistrationParticipationSnapshot(
        Guid configurationVersion,
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy)
    {
        ConfigurationVersion = configurationVersion;
        ParticipationHandlingModeId = participationHandlingModeId;
        AdvanceRegistrationObligationId = advanceRegistrationObligationId;
        IdentityAccessModeId = identityAccessModeId;
        GuestRecoveryPolicy = guestRecoveryPolicy;
    }

    public Guid ConfigurationVersion { get; }

    public int ParticipationHandlingModeId { get; }

    public int AdvanceRegistrationObligationId { get; }

    public int? IdentityAccessModeId { get; }

    public GuestRecoveryPolicyEnum? GuestRecoveryPolicy { get; }

    public static RegistrationParticipationSnapshot Create(
        Guid configurationVersion,
        int participationHandlingModeId,
        int advanceRegistrationObligationId,
        int? identityAccessModeId,
        GuestRecoveryPolicyEnum? guestRecoveryPolicy)
    {
        if (configurationVersion == Guid.Empty || participationHandlingModeId <= 0 || advanceRegistrationObligationId <= 0)
        {
            throw new ArgumentException("A versioned participation configuration is required.");
        }

        if (identityAccessModeId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identityAccessModeId));
        }

        return new RegistrationParticipationSnapshot(
            configurationVersion,
            participationHandlingModeId,
            advanceRegistrationObligationId,
            identityAccessModeId,
            guestRecoveryPolicy);
    }

    public static RegistrationParticipationSnapshot From(EventParticipationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return Create(
            configuration.ConcurrencyStamp,
            configuration.ParticipationHandlingModeId,
            configuration.AdvanceRegistrationObligationId,
            configuration.IdentityAccessModeId,
            configuration.GuestRecoveryPolicy);
    }
}
