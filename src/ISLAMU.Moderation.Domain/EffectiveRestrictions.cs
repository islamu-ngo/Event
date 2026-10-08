using System.Collections.Frozen;

namespace ISLAMU.Moderation.Domain;

public enum ModerationCapability
{
    Publish = 1,
    Upload = 2,
    ReadResource = 3,
    Manage = 4,
    Participate = 5,
    Admission = 6,
    PrivateLogistics = 7,
    Enrollment = 8,
    ProtectedStatus = 9,
    Contestation = 10,
    Support = 11,
    PrivacyErasure = 12
}

public enum ModerationAuthorityState
{
    Known = 1,
    Unknown = 2,
    Pending = 3,
    Unavailable = 4
}

public enum ModerationRestrictionOutcome
{
    Unrestricted = 1,
    Denied = 2,
    Unavailable = 3
}

/// <summary>Describes only the moderation overlay; unrestricted is not an authorization grant.</summary>
public sealed record ModerationRestrictionEvaluation
{
    internal ModerationRestrictionEvaluation(ModerationRestrictionOutcome outcome, IEnumerable<Guid> contributionIds)
    {
        Outcome = outcome;
        ContributionIds = Array.AsReadOnly(contributionIds.ToArray());
    }

    public ModerationRestrictionOutcome Outcome { get; }
    public IReadOnlyList<Guid> ContributionIds { get; }
}

/// <summary>Composes only matching contributions at the actual evaluation instant.</summary>
public static class EffectiveRestrictions
{
    public static ModerationRestrictionEvaluation Evaluate(
        SubjectReference subject, AuthorityScope target, ModerationCapability capability, DateTimeOffset now,
        IEnumerable<ModerationSanction> sanctions, IEnumerable<ModerationProtectiveMeasure> protection,
        ModerationAuthorityState authority)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(sanctions);
        ArgumentNullException.ThrowIfNull(protection);
        if (!Enum.IsDefined(capability) || subject.TrustDomainId != target.TrustDomainId)
        {
            return new ModerationRestrictionEvaluation(ModerationRestrictionOutcome.Unavailable, []);
        }

        if (IsProtectedRemedy(capability))
        {
            return new ModerationRestrictionEvaluation(ModerationRestrictionOutcome.Unrestricted, []);
        }

        if (authority != ModerationAuthorityState.Known)
        {
            return new ModerationRestrictionEvaluation(ModerationRestrictionOutcome.Unavailable, []);
        }

        var contributions = sanctions
            .Where(sanction => sanction.Subject == subject && sanction.Scope.Contains(target)
                && sanction.Capabilities.Contains(capability) && sanction.IsEffectiveAt(now))
            .Select(sanction => sanction.Id)
            .Concat(protection
                .Where(measure => measure.Subject == subject && measure.Scope.Contains(target)
                    && measure.Capabilities.Contains(capability) && measure.IsEffectiveAt(now))
                .Select(measure => measure.Id))
            .Distinct()
            .Order()
            .ToArray();

        return new ModerationRestrictionEvaluation(
            contributions.Length == 0 ? ModerationRestrictionOutcome.Unrestricted : ModerationRestrictionOutcome.Denied,
            contributions);
    }

    /// <summary>These finite control-plane purposes still require positive subject/reviewer authority in the host.</summary>
    public static bool IsProtectedRemedy(ModerationCapability capability) =>
        capability is ModerationCapability.ProtectedStatus or ModerationCapability.Contestation
            or ModerationCapability.Support or ModerationCapability.PrivacyErasure;

    internal static FrozenSet<ModerationCapability> CreateCapabilitySet(IEnumerable<ModerationCapability> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var snapshot = capabilities.ToFrozenSet();
        if (snapshot.Count == 0 || snapshot.Any(capability => !Enum.IsDefined(capability) || IsProtectedRemedy(capability)))
        {
            throw new ArgumentException("A contribution requires supported restrictive capabilities, not protected remedies.", nameof(capabilities));
        }

        return snapshot;
    }
}
