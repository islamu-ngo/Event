namespace Explore.Domain.Learning;

public enum LearningGuidanceAudience
{
    ActiveMembers = 1,
    TenantAdministrators = 2
}

/// <summary>
/// A learning evidence requirement, never a grant of production authority.
/// Orientation acknowledgement requires explicit self-acknowledgement evidence.
/// </summary>
public enum LearningRequirementPolicy
{
    Optional = 0,
    TenantAdminOrientationAcknowledgement = 1
}

/// <summary>
/// Family switches can only restrict global switches. Null audiences inherit the global audience.
/// </summary>
public sealed record LearningGuidanceFamilyOverride
{
    public LearningGuidanceFamilyOverride(LearningTourFamily family, bool enabled = true,
        bool automaticInvitations = true, LearningGuidanceAudience? invitationAudience = null,
        LearningGuidanceAudience? manualAccessAudience = null)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        if (invitationAudience.HasValue && !Enum.IsDefined(invitationAudience.Value))
            throw new ArgumentOutOfRangeException(nameof(invitationAudience));
        if (manualAccessAudience.HasValue && !Enum.IsDefined(manualAccessAudience.Value))
            throw new ArgumentOutOfRangeException(nameof(manualAccessAudience));

        Family = family;
        Enabled = enabled;
        AutomaticInvitations = automaticInvitations;
        InvitationAudience = invitationAudience;
        ManualAccessAudience = manualAccessAudience;
    }

    public LearningTourFamily Family { get; }
    public bool Enabled { get; }
    public bool AutomaticInvitations { get; }
    public LearningGuidanceAudience? InvitationAudience { get; }
    public LearningGuidanceAudience? ManualAccessAudience { get; }
}

/// <summary>
/// Immutable learning configuration. Evaluation neither starts learning nor changes existing
/// learning state; explicit Start and application-owned membership and capability checks remain required.
/// </summary>
public sealed record LearningGuidancePolicy
{
    public LearningGuidancePolicy(bool enabled = true, bool automaticInvitations = true,
        LearningGuidanceAudience invitationAudience = LearningGuidanceAudience.ActiveMembers,
        LearningGuidanceAudience manualAccessAudience = LearningGuidanceAudience.ActiveMembers,
        LearningRequirementPolicy requirementPolicy = LearningRequirementPolicy.Optional,
        IReadOnlyList<LearningGuidanceFamilyOverride>? familyOverrides = null)
    {
        if (!Enum.IsDefined(invitationAudience)) throw new ArgumentOutOfRangeException(nameof(invitationAudience));
        if (!Enum.IsDefined(manualAccessAudience)) throw new ArgumentOutOfRangeException(nameof(manualAccessAudience));
        if (!Enum.IsDefined(requirementPolicy)) throw new ArgumentOutOfRangeException(nameof(requirementPolicy));
        if (!enabled && requirementPolicy != LearningRequirementPolicy.Optional)
            throw new ArgumentException("Disabled learning cannot require orientation acknowledgement.", nameof(requirementPolicy));

        LearningGuidanceFamilyOverride[] snapshot = familyOverrides?.Take(5).ToArray() ?? [];
        if (snapshot.Length > 4 || snapshot.Any(item => item is null)
            || snapshot.Select(item => item.Family).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Use at most four distinct known-family overrides.", nameof(familyOverrides));

        Enabled = enabled;
        AutomaticInvitations = automaticInvitations;
        InvitationAudience = invitationAudience;
        ManualAccessAudience = manualAccessAudience;
        RequirementPolicy = requirementPolicy;
        FamilyOverrides = Array.AsReadOnly(snapshot);
    }

    public static LearningGuidancePolicy Default { get; } = new();

    public bool Enabled { get; }
    public bool AutomaticInvitations { get; }
    public LearningGuidanceAudience InvitationAudience { get; }
    public LearningGuidanceAudience ManualAccessAudience { get; }
    public LearningRequirementPolicy RequirementPolicy { get; }
    public IReadOnlyList<LearningGuidanceFamilyOverride> FamilyOverrides { get; }

    /// <summary>
    /// Evaluates invitation and manual access separately using current facts supplied by the application.
    /// Both audiences require active membership; administrator status alone is insufficient.
    /// Results do not establish acknowledgement evidence or authorize production operations.
    /// </summary>
    public (bool CanInvite, bool CanAccessManually) Evaluate(LearningTourFamily family,
        bool isActiveMember, bool isTenantAdministrator)
    {
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        LearningGuidanceFamilyOverride? familyOverride = FamilyOverrides.SingleOrDefault(item => item.Family == family);
        if (!Enabled || familyOverride?.Enabled == false || !isActiveMember)
            return (false, false);

        LearningGuidanceAudience invitationAudience = familyOverride?.InvitationAudience ?? InvitationAudience;
        LearningGuidanceAudience manualAccessAudience = familyOverride?.ManualAccessAudience ?? ManualAccessAudience;
        bool canInvite = AutomaticInvitations && familyOverride?.AutomaticInvitations != false
            && (invitationAudience == LearningGuidanceAudience.ActiveMembers || isTenantAdministrator);
        bool canAccessManually = manualAccessAudience == LearningGuidanceAudience.ActiveMembers || isTenantAdministrator;
        return (canInvite, canAccessManually);
    }
}
