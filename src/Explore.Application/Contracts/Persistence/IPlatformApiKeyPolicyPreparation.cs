using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Closed original actions. Database write capability alone does not select an action.</summary>
public enum InstanceApiKeyAction { Read = 1, Update = 2, Revoke = 3, Report = 4 }

/// <summary>Dedicated source purposes, never ordinary Runtime/Global purpose aliases.</summary>
public enum PlatformApiKeySourcePurpose { Read = 1, Write = 2, Report = 3 }

public sealed record PlatformApiKeySourceFacts(
    HumanPrincipalSourceFacts Principal, HumanAuthorizationRouteFacts Route, HumanSupportAccessSourceFacts Support)
{
    public override string ToString() => nameof(PlatformApiKeySourceFacts);
}

/// <summary>
/// Prepares the original policy question while the dedicated owner is idle.
/// The persistence owner creates and retains the result; lower scope callers cannot supply it.
/// </summary>
public interface IPlatformApiKeyPolicyPreparation
{
    Task<IPreparedPlatformApiKeyPolicy> PrepareAsync(InstanceApiKeyAction action, Guid? keyId,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, PlatformApiKeySourceFacts original,
        CancellationToken cancellationToken);
}

public interface IPreparedPlatformApiKeyPolicy
{
    void ValidateSources(PlatformApiKeySourceFacts current);
    void ValidateTime(PlatformApiKeySourceFacts current, DateTimeOffset now);
}

public static class PlatformApiKeyActionPolicy
{
    public static AuthorizationRequest Request(InstanceApiKeyAction action, Guid? keyId) => action switch
    {
        InstanceApiKeyAction.Read when keyId != Guid.Empty => new(ResourceKinds.InstanceSetting,
            keyId is { } id ? $"instance.api-keys.{id:N}" : "instance.api-keys.mine", AuthorizationActions.View),
        InstanceApiKeyAction.Update when keyId is { } id && id != Guid.Empty =>
            new(ResourceKinds.InstanceSetting, $"instance.api-keys.{id:N}", AuthorizationActions.Update),
        InstanceApiKeyAction.Revoke when keyId is { } id && id != Guid.Empty =>
            new(ResourceKinds.InstanceSetting, $"instance.api-keys.{id:N}", AuthorizationActions.Delete),
        InstanceApiKeyAction.Report when keyId is null =>
            new(ResourceKinds.InstanceSetting, "instance.api-keys.usage-report", AuthorizationActions.View),
        _ => throw new ArgumentException("The original platform key action and target are required.")
    };
}
