namespace Explore.Application.Authorization;

/// <summary>
/// Marks the sole request that may establish the initial instance administrator from a
/// deployment-owned configured-provider binding.
/// </summary>
/// <remarks>
/// Unlike <see cref="AuthorizeResourceAttribute"/>, this bootstrap authority does not require an
/// administrator principal to exist before the first administrator can be established.
/// <c>InstanceOnboardingCompletionOperation</c> enforces it for every caller by requiring an exact
/// verified provider account, generation, provider kind, and identity fingerprint before entering
/// the authority-transfer write path.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuthorizeConfiguredAdministratorClaimAttribute : Attribute;
