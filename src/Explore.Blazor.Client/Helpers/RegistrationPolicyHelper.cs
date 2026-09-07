namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Client-side registration policy logic mirroring the server-owned registration policy rules.
/// Keep these numeric policy and scope identifiers aligned with the generated API contract.
/// </summary>
public static class RegistrationPolicyHelper
{
    public const int ScopeEvent = 1;
    public const int ScopeDay = 2;
    public const int ScopeSessionSelection = 3;

    private const int PolicyWholeEventOnly = 1;
    private const int PolicyWholeDayOnly = 2;
    private const int PolicySessionSelectionOnly = 3;
    private const int PolicyWholeEventOrDay = 4;
    private const int PolicyWholeEventOrSession = 5;
    private const int PolicyFlexible = 6;

    /// <summary>
    /// Returns the list of allowed scope IDs for the given policy.
    /// A null policy is treated as Flexible (all scopes allowed).
    /// </summary>
    public static IReadOnlyList<int> GetAllowedScopes(int? policyId)
    {
        var policy = policyId ?? PolicyFlexible;

        return policy switch
        {
            PolicyWholeEventOnly => [ScopeEvent],
            PolicyWholeDayOnly => [ScopeDay],
            PolicySessionSelectionOnly => [ScopeSessionSelection],
            PolicyWholeEventOrDay => [ScopeEvent, ScopeDay],
            PolicyWholeEventOrSession => [ScopeEvent, ScopeSessionSelection],
            PolicyFlexible => [ScopeEvent, ScopeDay, ScopeSessionSelection],
            _ => [ScopeSessionSelection]
        };
    }

    /// <summary>
    /// Returns a human-readable label for a registration scope.
    /// </summary>
    public static string GetScopeLabel(int scopeId) => scopeId switch
    {
        ScopeEvent => "Register for the entire event",
        ScopeDay => "Register for a specific day",
        ScopeSessionSelection => "Register for specific sessions",
        _ => "Register"
    };

    /// <summary>
    /// Returns a description for a registration scope.
    /// </summary>
    public static string GetScopeDescription(int scopeId) => scopeId switch
    {
        ScopeEvent => "You will be registered for all days and sessions",
        ScopeDay => "Choose a day to attend",
        ScopeSessionSelection => "Pick individual sessions to attend",
        _ => string.Empty
    };
}
