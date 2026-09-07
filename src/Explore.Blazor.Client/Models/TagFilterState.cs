// Neutral = no filter, Include = must be present, Exclude = must not be present.

namespace Explore.Blazor.Client.Models;

public enum TagFilterState
{
    Neutral = 0,
    Include = 1,
    Exclude = 2
}
