namespace Explore.Domain.Enums;

public enum EventRegistrationPolicyEnum
{
    WholeEventOnly = 1,
    WholeDayOnly = 2,
    SessionSelectionOnly = 3,
    WholeEventOrDay = 4,
    WholeEventOrSession = 5,
    Flexible = 6
}
