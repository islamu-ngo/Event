namespace Explore.Application.Contracts.Infrastructure;

using System.Collections.Generic;
using System.Reflection;
using Explore.Application.Authorization;

public interface IAuthorizationProvider
{
    Task<AuthorizationDecision> AuthorizeAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuthorizationDecision>> AuthorizeBatchAsync(
        IReadOnlyList<AuthorizationRequest> requests,
        CancellationToken cancellationToken = default);
}

public sealed record AuthorizationCapability
{
    internal AuthorizationCapability(string resourceKind, string action)
    {
        ResourceKind = resourceKind;
        Action = action;
    }

    public string ResourceKind { get; }

    public string Action { get; }
}

public static class AuthorizationCapabilityCatalog
{
    private static readonly Lazy<IReadOnlySet<string>> KnownResourceKinds = new(BuildResourceKinds);
    private static readonly Lazy<IReadOnlySet<string>> KnownActions = new(BuildActions);

    public static AuthorizationCapability Require(string resourceKind, string action)
    {
        if (string.IsNullOrWhiteSpace(resourceKind) || !KnownResourceKinds.Value.Contains(resourceKind))
            throw new ArgumentException("Authorization resource kind is not in the catalog.", nameof(resourceKind));

        if (string.IsNullOrWhiteSpace(action) || !KnownActions.Value.Contains(action))
            throw new ArgumentException("Authorization action is not in the catalog.", nameof(action));

        return new AuthorizationCapability(resourceKind, action);
    }

    private static IReadOnlySet<string> BuildResourceKinds() =>
        typeof(ResourceKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> BuildActions()
    {
        var actionTypes = new[] { typeof(AuthorizationActions) }
            .Concat(typeof(AuthorizationActions).GetNestedTypes(BindingFlags.Public));

        return actionTypes
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(field => field.FieldType == typeof(string))
            .Select(field => (string)field.GetValue(null)!)
            .ToHashSet(StringComparer.Ordinal);
    }
}

public sealed record AuthorizationSubject(Guid? UserId = null, bool IsMachine = false)
{
    public static readonly AuthorizationSubject Ambient = new();
}

public sealed record AuthorizationTenant(Guid? TenantId = null, Guid? OrganizationId = null)
{
    public static readonly AuthorizationTenant Ambient = new();
}

public enum AuthorizationDecisionOutcome
{
    Deny = 0,
    Allow = 1
}

public static class AuthorizationDecisionReasonCodes
{
    public const string Allowed = "allowed";
    public const string Denied = "denied";
    public const string InvalidRequest = "invalid_request";
    public const string MissingSubject = "missing_subject";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderError = "provider_error";
}

public sealed record AuthorizationProviderMetadata(string ProviderId, string? ObservedRevision = null)
{
    public static readonly AuthorizationProviderMetadata Runtime = new("runtime");
    public static readonly AuthorizationProviderMetadata Local = new("local");
    public static readonly AuthorizationProviderMetadata Cerbos = new("cerbos");
}

public sealed record AuthorizationDecision(
    AuthorizationDecisionOutcome Outcome,
    string ReasonCode,
    AuthorizationProviderMetadata Provider)
{
    public bool IsAllowed => Outcome == AuthorizationDecisionOutcome.Allow;

    public static AuthorizationDecision Allow(
        AuthorizationProviderMetadata provider,
        string reasonCode = AuthorizationDecisionReasonCodes.Allowed) =>
        new(AuthorizationDecisionOutcome.Allow, reasonCode, provider);

    public static AuthorizationDecision Deny(
        AuthorizationProviderMetadata provider,
        string reasonCode = AuthorizationDecisionReasonCodes.Denied) =>
        new(AuthorizationDecisionOutcome.Deny, reasonCode, provider);
}

/// <summary>
/// The single provider-neutral authorization question. Policy inputs are limited to the closed
/// <see cref="IAuthorizationFacts"/> catalog: there is no caller-authored attribute dictionary, so a
/// request can never widen its own authority by inventing policy inputs.
/// </summary>
public sealed record AuthorizationRequest(
    AuthorizationCapability Capability,
    string ResourceId,
    AuthorizationScope? Scope = null,
    IAuthorizationFacts? Facts = null,
    AuthorizationSubject? Subject = null,
    AuthorizationTenant? Tenant = null)
{
    public AuthorizationRequest(
        string resourceKind,
        string resourceId,
        string action,
        AuthorizationScope? Scope = null,
        IAuthorizationFacts? Facts = null,
        AuthorizationSubject? Subject = null,
        AuthorizationTenant? Tenant = null)
        : this(
            AuthorizationCapabilityCatalog.Require(resourceKind, action),
            resourceId,
            Scope,
            Facts,
            Subject,
            Tenant)
    {
    }

    public string ResourceKind => Capability.ResourceKind;

    public string Action => Capability.Action;

    /// <summary>
    /// Structural identity of the decision this request asks for. Facts are records, so equality is
    /// value-based and two requests collapse only when their trusted policy inputs are identical.
    /// </summary>
    public AuthorizationRequestKey ToDeduplicationKey() => new(
        ResourceKind,
        ResourceId,
        Action,
        Scope?.TenantId,
        Scope?.OrganizationId,
        Facts);
}

public sealed record AuthorizationRequestKey(
    string ResourceKind,
    string ResourceId,
    string Action,
    string? TenantScope,
    string? OrganizationScope,
    IAuthorizationFacts? Facts);
