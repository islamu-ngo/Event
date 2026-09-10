namespace Explore.Blazor.Client.Models;

/// <summary>Local progress and recovery states for a guest registration start.</summary>
public enum GuestRegistrationStartPhase
{
    Idle,
    Issuing,
    Solving,
    Submitting,
    Uncertain,
    Unavailable,
    Cancelled,
    Expired,
    IntentChanged,
    RetryExhausted,
    Completed
}
