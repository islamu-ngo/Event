namespace Explore.Application.Features.ConfigurationManifest.Managed;

public enum ConfigurationManagedPlanMode
{
    DriftOnly,
    Apply
}

public enum ConfigurationManagedFieldIntent
{
    Set,
    Delete,
    Relinquish
}

public enum ConfigurationManagedFieldOutcome
{
    Unchanged,
    Drift,
    Set,
    Delete,
    Relinquish,
    Conflict
}

public sealed record ConfigurationManagedFieldRequest(
    string FieldPath,
    string? CurrentDigest,
    string? DesiredDigest,
    string? CurrentManager,
    string RequestedManager,
    ConfigurationManagedFieldIntent Intent,
    bool TakeoverApproved);

public sealed record ConfigurationManagedFieldDecision(
    string FieldPath,
    ConfigurationManagedFieldOutcome Outcome,
    string ReasonCode,
    string? ResultingManager);

public sealed record ConfigurationManagedPlan(
    ConfigurationManagedPlanMode Mode,
    IReadOnlyList<ConfigurationManagedFieldDecision> Fields,
    bool CanApply)
{
    public override string ToString() => nameof(ConfigurationManagedPlan);
}

public static class ConfigurationManagedOwnershipPlanner
{
    public static ConfigurationManagedPlan Plan(
        ConfigurationManagedPlanMode mode,
        IReadOnlyCollection<ConfigurationManagedFieldRequest> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (fields.Count == 0
            || fields.Select(field => field.FieldPath)
                .Distinct(StringComparer.Ordinal)
                .Count() != fields.Count)
        {
            throw new ArgumentException(
                "Managed field requests must be non-empty and unique.",
                nameof(fields));
        }

        ConfigurationManagedFieldDecision[] decisions =
        [.. fields
            .OrderBy(field => field.FieldPath, StringComparer.Ordinal)
            .Select(field => Decide(mode, field))];
        return new ConfigurationManagedPlan(
            mode,
            decisions,
            mode == ConfigurationManagedPlanMode.Apply
            && decisions.All(decision =>
                decision.Outcome != ConfigurationManagedFieldOutcome.Conflict));
    }

    private static ConfigurationManagedFieldDecision Decide(
        ConfigurationManagedPlanMode mode,
        ConfigurationManagedFieldRequest field)
    {
        Validate(field);
        bool ownedByRequester = string.Equals(
            field.CurrentManager,
            field.RequestedManager,
            StringComparison.Ordinal);
        bool ownedByAnother = field.CurrentManager is not null && !ownedByRequester;

        if (field.Intent == ConfigurationManagedFieldIntent.Relinquish)
        {
            return ownedByRequester
                ? Decision(
                    field,
                    mode == ConfigurationManagedPlanMode.DriftOnly
                        ? ConfigurationManagedFieldOutcome.Drift
                        : ConfigurationManagedFieldOutcome.Relinquish,
                    "configuration_managed_relinquishment_explicit",
                    resultingManager: null)
                : Conflict(field, "configuration_managed_relinquishment_not_owner");
        }

        if (ownedByAnother && !field.TakeoverApproved)
            return Conflict(field, "configuration_managed_takeover_consent_required");

        if (field.Intent == ConfigurationManagedFieldIntent.Delete)
        {
            if (!ownedByRequester)
                return Conflict(field, "configuration_managed_delete_not_owned");
            return Decision(
                field,
                mode == ConfigurationManagedPlanMode.DriftOnly
                    ? ConfigurationManagedFieldOutcome.Drift
                    : ConfigurationManagedFieldOutcome.Delete,
                "configuration_managed_owned_deletion_previewed",
                field.RequestedManager);
        }

        if (string.Equals(
                field.CurrentDigest,
                field.DesiredDigest,
                StringComparison.Ordinal))
        {
            return Decision(
                field,
                ConfigurationManagedFieldOutcome.Unchanged,
                "configuration_managed_field_unchanged",
                field.CurrentManager ?? field.RequestedManager);
        }

        return Decision(
            field,
            mode == ConfigurationManagedPlanMode.DriftOnly
                ? ConfigurationManagedFieldOutcome.Drift
                : ConfigurationManagedFieldOutcome.Set,
            ownedByAnother
                ? "configuration_managed_takeover_approved"
                : "configuration_managed_drift_detected",
            field.RequestedManager);
    }

    private static ConfigurationManagedFieldDecision Conflict(
        ConfigurationManagedFieldRequest field,
        string reason) =>
        Decision(
            field,
            ConfigurationManagedFieldOutcome.Conflict,
            reason,
            field.CurrentManager);

    private static ConfigurationManagedFieldDecision Decision(
        ConfigurationManagedFieldRequest field,
        ConfigurationManagedFieldOutcome outcome,
        string reason,
        string? resultingManager) =>
        new(field.FieldPath, outcome, reason, resultingManager);

    private static void Validate(ConfigurationManagedFieldRequest field)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field.FieldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(field.RequestedManager);
        if (!field.FieldPath.StartsWith("/", StringComparison.Ordinal)
            || field.FieldPath.Contains("..", StringComparison.Ordinal)
            || field.Intent == ConfigurationManagedFieldIntent.Set
                && field.DesiredDigest is null)
        {
            throw new ArgumentException("Managed field request is invalid.");
        }
    }
}
