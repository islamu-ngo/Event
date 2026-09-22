using System.Collections.ObjectModel;

namespace Explore.Domain.Keycloak;

public enum KeycloakStep
{
    CreateRealm,
    CreateClient,
    CreateMapper,
    UpdateMapper
}

public enum KeycloakResourceKind
{
    Realm,
    Client,
    ProtocolMapper
}

public enum KeycloakStepPrecondition
{
    MustBeAbsent,
    MustMatchFingerprint
}

public enum KeycloakDesiredKind
{
    Realm,
    ConfidentialBffClient,
    BearerOnlyApiClient,
    SubjectMapper,
    AudienceMapper
}

/// <summary>Closed, nonsecret provider projection approved by an operator.</summary>
public sealed record KeycloakDesiredProjection
{
    public KeycloakDesiredProjection(
        KeycloakDesiredKind kind,
        string resourceName,
        IReadOnlyList<string>? redirectUris = null,
        IReadOnlyList<string>? webOrigins = null,
        string? audience = null,
        string? providerResourceId = null)
    {
        Kind = kind;
        ResourceName = Required(resourceName);
        RedirectUris = (redirectUris ?? []).ToArray();
        WebOrigins = (webOrigins ?? []).ToArray();
        Audience = string.IsNullOrWhiteSpace(audience) ? null : audience.Trim();
        string? normalizedProviderResourceId =
            string.IsNullOrWhiteSpace(providerResourceId)
                ? null
                : providerResourceId.Trim();
        Guid parsedProviderResourceId = Guid.Empty;
        if (kind == KeycloakDesiredKind.Realm
            && (!Guid.TryParse(
                    normalizedProviderResourceId,
                    out parsedProviderResourceId)
                || parsedProviderResourceId == Guid.Empty))
        {
            throw new ArgumentException(
                "A realm projection requires a provider UUID.");
        }

        if (kind != KeycloakDesiredKind.Realm
            && normalizedProviderResourceId is not null)
        {
            throw new ArgumentException(
                "Only a realm projection may carry a provider resource ID.");
        }

        ProviderResourceId = kind == KeycloakDesiredKind.Realm
            ? parsedProviderResourceId.ToString("D")
            : null;
    }

    public KeycloakDesiredKind Kind { get; }
    public string ResourceName { get; }
    public IReadOnlyList<string> RedirectUris { get; }
    public IReadOnlyList<string> WebOrigins { get; }
    public string? Audience { get; }
    public string? ProviderResourceId { get; }

    public static KeycloakDesiredProjection Realm(
        string realm,
        string providerResourceId) =>
        new(
            KeycloakDesiredKind.Realm,
            realm,
            providerResourceId: providerResourceId);

    public static KeycloakDesiredProjection ConfidentialClient(
        string clientId,
        IReadOnlyList<string> redirectUris,
        IReadOnlyList<string> webOrigins) =>
        new(KeycloakDesiredKind.ConfidentialBffClient, clientId, redirectUris, webOrigins);

    public static KeycloakDesiredProjection BearerOnlyClient(string clientId) =>
        new(KeycloakDesiredKind.BearerOnlyApiClient, clientId);

    public static KeycloakDesiredProjection Mapper(
        string name,
        KeycloakMapperSemantic semantic,
        string? audience = null) =>
        new(
            semantic == KeycloakMapperSemantic.Subject
                ? KeycloakDesiredKind.SubjectMapper
                : KeycloakDesiredKind.AudienceMapper,
            name,
            audience: audience);

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A desired resource name is required.")
            : value.Trim();
}

public sealed record KeycloakChangeStep
{
    public KeycloakChangeStep(
        string stepId,
        KeycloakStep kind,
        KeycloakResourceKind resourceKind,
        string targetId,
        KeycloakStepPrecondition precondition,
        string? expectedFingerprint,
        string? expectedIdentityFingerprint,
        string desiredFingerprint,
        string bindingFingerprint,
        KeycloakDesiredProjection desired)
    {
        StepId = Required(stepId, nameof(stepId));
        Kind = kind;
        ResourceKind = resourceKind;
        TargetId = Required(targetId, nameof(targetId));
        Precondition = precondition;
        ExpectedFingerprint = NormalizeOptional(expectedFingerprint);
        ExpectedIdentityFingerprint = NormalizeOptional(
            expectedIdentityFingerprint);
        DesiredFingerprint = Required(
            desiredFingerprint,
            nameof(desiredFingerprint));
        BindingFingerprint = Required(
            bindingFingerprint,
            nameof(bindingFingerprint));
        Desired = desired
            ?? throw new ArgumentNullException(nameof(desired));

        if (!Enum.IsDefined(kind)
            || !Enum.IsDefined(resourceKind)
            || !Enum.IsDefined(precondition)
            || (precondition == KeycloakStepPrecondition.MustBeAbsent
                && (ExpectedFingerprint is not null
                    || ExpectedIdentityFingerprint is not null))
            || (precondition == KeycloakStepPrecondition.MustMatchFingerprint
                && (ExpectedFingerprint is null
                    || ExpectedIdentityFingerprint is null))
            || !IsStructurallyValid(kind, resourceKind, precondition, StepId, TargetId, Desired))
        {
            throw new ArgumentException(
                "The step projection or precondition is invalid.");
        }
    }

    public string StepId { get; }

    public KeycloakStep Kind { get; }

    public KeycloakResourceKind ResourceKind { get; }

    public string TargetId { get; }

    public KeycloakStepPrecondition Precondition { get; }

    public string? ExpectedFingerprint { get; }

    public string? ExpectedIdentityFingerprint { get; }

    public string DesiredFingerprint { get; }

    public string BindingFingerprint { get; }

    public KeycloakDesiredProjection Desired { get; }

    private static bool IsStructurallyValid(
        KeycloakStep kind,
        KeycloakResourceKind resourceKind,
        KeycloakStepPrecondition precondition,
        string stepId,
        string targetId,
        KeycloakDesiredProjection desired) =>
        kind switch
        {
            KeycloakStep.CreateRealm => resourceKind == KeycloakResourceKind.Realm
                && precondition == KeycloakStepPrecondition.MustBeAbsent
                && desired.Kind == KeycloakDesiredKind.Realm,
            KeycloakStep.CreateClient => resourceKind == KeycloakResourceKind.Client
                && precondition == KeycloakStepPrecondition.MustBeAbsent
                && desired.ResourceName == targetId
                && desired.Kind is (
                    KeycloakDesiredKind.ConfidentialBffClient
                    or KeycloakDesiredKind.BearerOnlyApiClient)
                && (!stepId.Equals("client:api", StringComparison.Ordinal)
                    || desired.Kind
                        == KeycloakDesiredKind.BearerOnlyApiClient)
                && (!stepId.Equals("client:bff", StringComparison.Ordinal)
                    || desired.Kind
                        == KeycloakDesiredKind.ConfidentialBffClient),
            KeycloakStep.CreateMapper => resourceKind == KeycloakResourceKind.ProtocolMapper
                && precondition == KeycloakStepPrecondition.MustBeAbsent
                && desired.Kind is (
                    KeycloakDesiredKind.SubjectMapper
                    or KeycloakDesiredKind.AudienceMapper),
            KeycloakStep.UpdateMapper => resourceKind == KeycloakResourceKind.ProtocolMapper
                && precondition == KeycloakStepPrecondition.MustMatchFingerprint
                && desired.Kind is (
                    KeycloakDesiredKind.SubjectMapper
                    or KeycloakDesiredKind.AudienceMapper),
            _ => false
        };

    private static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A value is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record KeycloakChangeSet
{
    private readonly KeycloakChangeStep[] _steps;
    private readonly ReadOnlyCollection<KeycloakChangeStep> _stepsView;

    public KeycloakChangeSet(IEnumerable<KeycloakChangeStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps.ToArray();
        if (_steps.Length == 0
            || _steps.Select(step => step.StepId)
                .Distinct(StringComparer.Ordinal)
                .Count() != _steps.Length
            || (_steps.Any(step => step.Kind == KeycloakStep.CreateRealm)
                && _steps[0].Kind != KeycloakStep.CreateRealm)
            || _steps.Where(step => step.Kind == KeycloakStep.CreateClient)
                .Select(step => step.TargetId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != _steps.Count(step => step.Kind == KeycloakStep.CreateClient))
        {
            throw new ArgumentException(
                "Steps must be non-empty with unique identifiers.",
                nameof(steps));
        }

        _stepsView = Array.AsReadOnly(_steps);
    }

    public IReadOnlyList<KeycloakChangeStep> Steps => _stepsView;
}

public enum KeycloakStepOutcomeKind
{
    Applied,
    Verified,
    NoChange,
    Conflict,
    FailedBeforeWrite,
    OutcomeUnknown,
    SkippedCancelled
}

public sealed record KeycloakStepOutcome
{
    public KeycloakStepOutcome(
        string stepId,
        KeycloakStepOutcomeKind kind,
        string? providerResourceId = null,
        string? observedFingerprint = null)
    {
        if (string.IsNullOrWhiteSpace(stepId) || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("A valid step outcome is required.");
        }

        StepId = stepId.Trim();
        Kind = kind;
        ProviderResourceId = NormalizeOptional(providerResourceId);
        ObservedFingerprint = NormalizeOptional(observedFingerprint);
    }

    public string StepId { get; }

    public KeycloakStepOutcomeKind Kind { get; }

    public string? ProviderResourceId { get; }

    public string? ObservedFingerprint { get; }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record KeycloakStepOutcomeSet
{
    private readonly KeycloakStepOutcome[] _items;
    private readonly ReadOnlyCollection<KeycloakStepOutcome> _itemsView;

    public KeycloakStepOutcomeSet(IEnumerable<KeycloakStepOutcome>? items = null)
    {
        _items = items?.ToArray() ?? [];
        if (_items.Select(item => item.StepId)
                .Distinct(StringComparer.Ordinal)
                .Count() != _items.Length)
        {
            throw new ArgumentException(
                "Only one outcome may be recorded for a step.",
                nameof(items));
        }

        _itemsView = Array.AsReadOnly(_items);
    }

    public IReadOnlyList<KeycloakStepOutcome> Items => _itemsView;

    public KeycloakStepOutcomeSet Add(KeycloakStepOutcome outcome) =>
        _items.Any(item =>
            string.Equals(item.StepId, outcome.StepId, StringComparison.Ordinal))
            ? throw new InvalidOperationException(
                "The step already has a recovery outcome.")
            : new KeycloakStepOutcomeSet(_items.Append(outcome));

    public KeycloakStepOutcomeSet Reconcile(KeycloakStepOutcome outcome)
    {
        int index = Array.FindIndex(
            _items,
            item => string.Equals(
                item.StepId,
                outcome.StepId,
                StringComparison.Ordinal));
        if (index < 0
            || _items[index].Kind != KeycloakStepOutcomeKind.OutcomeUnknown
            || outcome.Kind is KeycloakStepOutcomeKind.OutcomeUnknown
                or KeycloakStepOutcomeKind.SkippedCancelled)
        {
            throw new InvalidOperationException(
                "Only an uncertain step may be settled by provider read-back.");
        }

        KeycloakStepOutcome[] reconciled = _items.ToArray();
        reconciled[index] = outcome;
        return new KeycloakStepOutcomeSet(reconciled);
    }
}

public sealed record KeycloakTarget
{
    public KeycloakTarget(
        Guid instanceId,
        string authority,
        string realm,
        string client)
    {
        if (instanceId == Guid.Empty)
        {
            throw new ArgumentException(
                "Instance identity is required.",
                nameof(instanceId));
        }

        InstanceId = instanceId;
        Authority = NormalizeAuthority(authority);
        AuthorityKey = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Authority)))
            .ToLowerInvariant();
        Realm = NormalizeIdentifier(realm, nameof(realm));
        Client = NormalizeIdentifier(client, nameof(client));
    }

    public Guid InstanceId { get; }

    public string Authority { get; }

    public string AuthorityKey { get; }

    public string Realm { get; }

    public string Client { get; }

    private static string NormalizeAuthority(string authority)
    {
        if (!Uri.TryCreate(authority?.Trim(), UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment))
        {
            throw new ArgumentException(
                "A canonical absolute provider authority is required.",
                nameof(authority));
        }

        var builder = new UriBuilder(parsed)
        {
            Scheme = parsed.Scheme.ToLowerInvariant(),
            Host = parsed.IdnHost.ToLowerInvariant(),
            Path = parsed.AbsolutePath == "/"
                ? string.Empty
                : parsed.AbsolutePath.TrimEnd('/'),
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/');
    }

    private static string NormalizeIdentifier(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "A target identifier is required.",
                parameterName);
        }

        return value.Trim().Normalize();
    }
}
