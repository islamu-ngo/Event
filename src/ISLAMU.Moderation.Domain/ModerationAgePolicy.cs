namespace ISLAMU.Moderation.Domain;

public enum ModerationLeapDayAnniversary { March1 = 1, February28 = 2 }

/// <summary>A declaration's calendar result, never assured eligibility or an access grant.</summary>
public enum ModerationDeclaredAgeOutcome { Unknown = 1, UnderThreshold = 2, AtLeastThreshold = 3 }

/// <summary>
/// Immutable calendar semantics for one jurisdiction's proposed age-policy revision.
/// Native adoption, lawful collection and current assurance remain separate authorities.
/// No birth date is retained by this policy or returned by its evaluation.
/// </summary>
public sealed record ModerationAgePolicy
{
    public ModerationAgePolicy(Guid policyId, long revision, AuthorityScope jurisdiction)
        : this(policyId, revision, jurisdiction, TimeZoneInfo.Utc, ModerationLeapDayAnniversary.March1) { }

    public ModerationAgePolicy(
        Guid policyId, long revision, AuthorityScope jurisdiction,
        TimeZoneInfo authorityTimeZone, ModerationLeapDayAnniversary leapDayAnniversary)
    {
        ModerationPolicy.RequireVersion7(policyId, nameof(policyId));
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        ArgumentNullException.ThrowIfNull(jurisdiction);
        ArgumentNullException.ThrowIfNull(authorityTimeZone);
        if (!Enum.IsDefined(leapDayAnniversary))
            throw new ArgumentOutOfRangeException(nameof(leapDayAnniversary));
        PolicyId = policyId;
        Revision = revision;
        Jurisdiction = jurisdiction;
        AuthorityTimeZone = authorityTimeZone;
        LeapDayAnniversary = leapDayAnniversary;
    }

    public Guid PolicyId { get; }
    public long Revision { get; }
    public AuthorityScope Jurisdiction { get; }
    public TimeZoneInfo AuthorityTimeZone { get; }
    public ModerationLeapDayAnniversary LeapDayAnniversary { get; }

    public ModerationDeclaredAgeOutcome EvaluateDeclaration(
        DateOnly? birthDate, int minimumAge, DateTimeOffset observedAt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumAge);
        if (birthDate is not { } birth) return ModerationDeclaredAgeOutcome.Unknown;
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(observedAt, AuthorityTimeZone).DateTime);
        if (birth > date) return ModerationDeclaredAgeOutcome.Unknown;
        var elapsedYears = date.Year - birth.Year;
        if (elapsedYears < minimumAge) return ModerationDeclaredAgeOutcome.UnderThreshold;
        if (elapsedYears > minimumAge) return ModerationDeclaredAgeOutcome.AtLeastThreshold;
        var anniversary = birth.Month == 2 && birth.Day == 29 && !DateTime.IsLeapYear(date.Year)
            ? LeapDayAnniversary == ModerationLeapDayAnniversary.March1
                ? new DateOnly(date.Year, 3, 1)
                : new DateOnly(date.Year, 2, 28)
            : new DateOnly(date.Year, birth.Month, birth.Day);
        return date >= anniversary
            ? ModerationDeclaredAgeOutcome.AtLeastThreshold
            : ModerationDeclaredAgeOutcome.UnderThreshold;
    }
}
