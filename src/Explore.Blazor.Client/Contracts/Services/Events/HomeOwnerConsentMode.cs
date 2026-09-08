namespace Explore.Blazor.Client.Contracts.Services.Events;

public enum HomeOwnerConsentMode
{
    /// <summary>Mark a location as a private home and become its first consenting owner.</summary>
    Classify = 0,

    /// <summary>Accept ownership of a location that is already a private home.</summary>
    Transfer = 1
}
