using Explore.Domain.Enums;

namespace Explore.Domain;

public sealed class AdmissionCheckInDecision
{
    internal AdmissionCheckInDecision(
        AdmissionCheckInResultCodeEnum resultCode,
        AdmissionCheckInEvent? @event,
        AdmissionCheckInState nextState)
    {
        ResultCode = resultCode;
        Event = @event;
        NextState = nextState;
    }

    public AdmissionCheckInResultCodeEnum ResultCode { get; }
    public AdmissionCheckInEvent? Event { get; }
    public AdmissionCheckInState NextState { get; }
}
