namespace Explore.Blazor.Client.Models.Analytics;

public enum ConsentState
{
    Uninitialized = 0,
    NoBannerImmediateInit = 1,
    BannerPendingCookieless = 2,
    BannerPendingBlocked = 3,
    Accepted = 4,
    DeclinedCookieless = 5,
    DeclinedDisabled = 6
}
