using Explore.Domain.Enums;

namespace Explore.Domain;

public sealed record EventParticipationConfigurationValidationError(
    EventParticipationConfigurationErrorCode Code,
    string Message);
