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

public sealed record KeycloakChangeStep
{
    public KeycloakChangeStep(
        string stepId,
        KeycloakStep kind,
        KeycloakResourceKind resourceKind,
        string targetId,
        KeycloakStepPrecondition precondition,
        string? expectedFingerprint,
        string desiredFingerprint)
    {
        StepId = Required(stepId, nameof(stepId));
        Kind = kind;
        ResourceKind = resourceKind;
        TargetId = Required(targetId, nameof(targetId));
        Precondition = precondition;
        ExpectedFingerprint = NormalizeOptional(expectedFingerprint);
        DesiredFingerprint = Required(
            desiredFingerprint,
            nameof(desiredFingerprint));

        if (!Enum.IsDefined(kind)
            || !Enum.IsDefined(resourceKind)
            || !Enum.IsDefined(precondition)
            || (precondition == KeycloakStepPrecondition.MustBeAbsent
                && ExpectedFingerprint is not null)
            || (precondition == KeycloakStepPrecondition.MustMatchFingerprint
                && ExpectedFingerprint is null))
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

    public string DesiredFingerprint { get; }

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
                .Count() != _steps.Length)
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
