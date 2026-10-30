using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class UserLearningState : ITenantEntity
{
    private Guid _tenantId;
    private readonly List<LearningTourProgress> _tours = [];
    private readonly List<LearningPracticeState> _practices = [];
    private UserLearningState() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(UserLearningState));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(UserLearningState));
    }
    public Guid UserId { get; private set; }
    public long Revision { get; private set; }
    public bool SuppressAutomaticTours { get; private set; }
    public bool IsHidden { get; private set; }
    public bool IsPinned { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? ActiveClaimId { get; private set; }
    public Guid? ActiveTourId { get; private set; }
    public string? ActiveContextKey { get; private set; }
    public Guid? ActiveEventContextId { get; private set; }
    public string? ActivePlanFingerprint { get; private set; }
    public string? ActivePolicySourceStamp { get; private set; }
    public DateTime? ClaimExpiresAtUtc { get; private set; }
    public Guid? LastIntentId { get; private set; }
    public string? LastIntentFingerprint { get; private set; }
    public IReadOnlyCollection<LearningTourProgress> Tours => _tours.AsReadOnly();
    public IReadOnlyCollection<LearningPracticeState> Practices => _practices.AsReadOnly();
    public int TotalPracticePayloadBytes => _practices.Sum(p => p.PayloadBytes);

    public void ClaimSemanticTour(Guid progressId, LearningTourDefinition definition,
        Guid claimId, Guid intentId, long expectedRevision, DateTime utcNow,
        string planFingerprint, string policySourceStamp, string contextKey = "self",
        Guid? eventContextId = null, bool replay = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateTour(progressId, definition.Family, definition.MaterialVersion);
        LearningGuard.Identity(claimId, nameof(claimId));
        LearningGuard.Context(contextKey);
        RequirePlanStamp(planFingerprint);
        RequirePlanStamp(policySourceStamp);
        if (eventContextId.HasValue) LearningGuard.Identity(eventContextId.Value, nameof(eventContextId));
        string fingerprint = LearningGuard.Fingerprint(
            $"semantic-claim|{progressId:D}|{definition.Family}|{definition.MaterialVersion}|{claimId:D}|{intentId:D}|{expectedRevision}|{contextKey}|{eventContextId:D}|{replay}|{planFingerprint}|{policySourceStamp}");
        if (IsDuplicate(intentId, fingerprint, utcNow)) return;
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        if (ActiveClaimId == claimId || HasLiveClaim(utcNow))
            throw new InvalidOperationException("A learning tour already owns the active claim.");
        LearningTourProgress? existing = FindTour(progressId, definition.Family, definition.MaterialVersion);
        if (!replay && (SuppressAutomaticTours || existing?.SnoozedUntilUtc > utcNow
            || existing?.Outcome == LearningTourOutcome.Skipped || HasTourCoverage(definition)))
            throw new InvalidOperationException("This tour requires explicit manual replay.");
        DateTime expiry = utcNow.AddSeconds(120);
        LearningTourProgress progress = existing ?? AddSemanticTour(progressId, definition, utcNow);
        progress.CarryCompatibleEvidence(definition, _tours.Where(tour => tour.Family == definition.Family));
        progress.BeginSemantic(intentId, fingerprint, utcNow);
        ActiveClaimId = claimId;
        ActiveTourId = progress.Id;
        ActiveContextKey = contextKey;
        ActiveEventContextId = eventContextId;
        ActivePlanFingerprint = planFingerprint;
        ActivePolicySourceStamp = policySourceStamp;
        ClaimExpiresAtUtc = expiry;
        Remember(intentId, fingerprint, utcNow);
    }

    public void RecordSemanticTourAction(Guid progressId, LearningTourDefinition definition,
        Guid? claimId, Guid intentId, LearningTourAction action, string? stepKey,
        int? stepVersion, LearningSnoozeDuration? snoozeDuration, string planFingerprint,
        string policySourceStamp, long expectedRevision, DateTime utcNow,
        string contextKey = "self", Guid? eventContextId = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ValidateTour(progressId, definition.Family, definition.MaterialVersion);
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        RequirePlanStamp(planFingerprint);
        RequirePlanStamp(policySourceStamp);
        LearningGuard.Context(contextKey);
        if (eventContextId.HasValue) LearningGuard.Identity(eventContextId.Value, nameof(eventContextId));
        LearningTourStep? step = null;
        if (action == LearningTourAction.Progress)
            step = definition.Steps.SingleOrDefault(value => value.StepKey == stepKey && value.StepVersion == stepVersion)
                ?? throw new ArgumentException("The semantic step is not currently applicable.", nameof(stepKey));
        else if (stepKey is not null || stepVersion.HasValue)
            throw new ArgumentException("Only Progress accepts semantic step fields.", nameof(stepKey));
        if ((action == LearningTourAction.Snooze) != snoozeDuration.HasValue
            || snoozeDuration.HasValue && !Enum.IsDefined(snoozeDuration.Value))
            throw new ArgumentException("Snooze requires one closed duration.", nameof(snoozeDuration));
        if (claimId.HasValue) LearningGuard.Identity(claimId.Value, nameof(claimId));
        string fingerprint = LearningGuard.Fingerprint(
            $"semantic-action|{progressId:D}|{definition.Family}|{definition.MaterialVersion}|{claimId:D}|{intentId:D}|{action}|{stepKey}|{stepVersion}|{snoozeDuration}|{planFingerprint}|{policySourceStamp}|{expectedRevision}|{contextKey}|{eventContextId:D}");
        if (IsDuplicate(intentId, fingerprint, utcNow)) return;
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        LearningTourProgress? existing = FindTour(progressId, definition.Family, definition.MaterialVersion);
        if (claimId.HasValue)
        {
            if (!HasLiveClaim(utcNow) || ActiveClaimId != claimId || ActiveTourId != existing?.Id
                || ActiveContextKey != contextKey || ActiveEventContextId != eventContextId
                || ActivePlanFingerprint != planFingerprint || ActivePolicySourceStamp != policySourceStamp)
                throw new InvalidOperationException("The learning claim or its accepted sources changed.");
        }
        else if (action is not (LearningTourAction.Skip or LearningTourAction.Snooze) || HasLiveClaim(utcNow))
            throw new InvalidOperationException("Only an unclaimed invitation may be skipped or snoozed.");
        if (action == LearningTourAction.Complete && (existing is null || !existing.Covers(definition)))
            throw new InvalidOperationException("Complete every applicable semantic step before completing the tour.");
        if (step is not null && existing!.Steps.Any(value =>
                value.StepKey == step.StepKey && value.StepVersion == step.StepVersion))
            throw new InvalidOperationException("Only new explicit semantic progress renews a claim.");
        DateTime? snoozedUntil = snoozeDuration switch
        {
            LearningSnoozeDuration.OneHour => utcNow.AddHours(1),
            LearningSnoozeDuration.OneDay => utcNow.AddDays(1),
            LearningSnoozeDuration.OneWeek => utcNow.AddDays(7),
            _ => null
        };
        LearningTourProgress progress = existing ?? AddSemanticTour(progressId, definition, utcNow);
        progress.CarryCompatibleEvidence(definition, _tours.Where(tour => tour.Family == definition.Family));
        progress.RecordSemantic(action, step, snoozedUntil, intentId, fingerprint, utcNow);
        if (action == LearningTourAction.Progress) ClaimExpiresAtUtc = utcNow.AddSeconds(120);
        else ReleaseClaim();
        Remember(intentId, fingerprint, utcNow);
    }

    public bool HasTourCoverage(LearningTourDefinition definition) =>
        definition.Steps.All(step => _tours
            .Where(tour => tour.Family == definition.Family)
            .SelectMany(tour => tour.Steps)
            .Any(completed => completed.StepKey == step.StepKey
                && completed.StepVersion == step.StepVersion));

    private LearningTourProgress AddSemanticTour(Guid progressId, LearningTourDefinition definition, DateTime utcNow)
    {
        LearningTourProgress[] history = _tours.Where(tour => tour.Family == definition.Family)
            .OrderBy(tour => tour.MaterialVersion).ToArray();
        LearningTourProgress progress = LearningTourProgress.Create(progressId, this,
            definition.Family, definition.MaterialVersion, utcNow);
        progress.CarryCompatibleEvidence(definition, history);
        if (history.Length == 2) _tours.Remove(history[0]);
        _tours.Add(progress);
        return progress;
    }

    private static void RequirePlanStamp(string value)
    {
        if (value is null || value.Length != 64 || value.Any(character => !char.IsAsciiHexDigit(character)))
            throw new ArgumentException("A bounded plan or policy source fingerprint is required.", nameof(value));
    }

    public static UserLearningState Create(Guid id, Guid tenantId, Guid userId, DateTime utcNow)
    {
        LearningGuard.Identity(id, nameof(id));
        LearningGuard.Identity(tenantId, nameof(tenantId));
        LearningGuard.Identity(userId, nameof(userId));
        LearningGuard.Utc(utcNow);
        return new() { Id = id, TenantId = tenantId, UserId = userId, UpdatedAtUtc = utcNow };
    }

    public void UpdatePreferences(bool suppressAutomaticTours, bool hidden, bool pinned,
        long expectedRevision, DateTime utcNow)
    {
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        SuppressAutomaticTours = suppressAutomaticTours;
        IsHidden = hidden;
        IsPinned = pinned;
        Advance(utcNow);
    }

    public void ApplyPractice(Guid practiceId, string contextKey, string recipeKey, int recipeVersion,
        LearningPracticeAction action, LearningPracticeChoice choice, int step, int value,
        Guid intentId, long expectedRevision, long expectedPracticeRevision, DateTime utcNow)
    {
        LearningGuard.Identity(practiceId, nameof(practiceId));
        LearningGuard.Context(contextKey);
        if (recipeKey is not ("event-details" or "sessions-program" or "ticket-capacity" or
            "attendees-team" or "publication-lifecycle" or "ai-example-review"))
            throw new ArgumentException("The practice recipe is not part of the shipped pack.", nameof(recipeKey));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (!Enum.IsDefined(choice)) throw new ArgumentOutOfRangeException(nameof(choice));
        if (step is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(step));
        if (value is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(value));
        string fingerprint = LearningGuard.Fingerprint(
            $"practice|{practiceId:D}|{contextKey}|{recipeKey}|{recipeVersion}|{action}|{choice}|{step}|{value}|{intentId:D}|{expectedRevision}|{expectedPracticeRevision}");
        if (IsDuplicate(intentId, fingerprint, utcNow)) return;
        LearningGuard.Mutation(Revision, expectedRevision, UpdatedAtUtc, utcNow);
        if (recipeVersion != 1)
            throw new InvalidOperationException("The authored recipe version is stale or unsupported.");
        LearningPracticeState? existing = _practices.SingleOrDefault(p => p.ContextKey == contextKey && p.RecipeKey == recipeKey);
        if (existing is not null && existing.Id != practiceId ||
            _practices.Any(p => p.Id == practiceId && p != existing))
            throw new InvalidOperationException("Practice identity does not match its canonical context and recipe.");
        if ((existing?.Revision ?? 0) != expectedPracticeRevision || existing?.Revision == long.MaxValue)
            throw new InvalidOperationException("The practice revision is stale.");
        if (action != LearningPracticeAction.Choose &&
            (choice != LearningPracticeChoice.None || step != 0 || value != 0))
            throw new ArgumentException("Only Choose accepts authored choice fields.");
        if (action == LearningPracticeAction.Clear)
        {
            if (existing is null)
                throw new InvalidOperationException("There is no practice to clear.");
            // Context membership is intentionally not needed to remove the owner's orphaned data.
            _practices.Remove(existing);
        }
        else
        {
            if (existing is null)
            {
                if (action != LearningPracticeAction.Start)
                    throw new InvalidOperationException("Start the recipe before recording practice.");
                if (!_practices.Any(p => p.ContextKey == contextKey) &&
                    _practices.Select(p => p.ContextKey).Distinct(StringComparer.Ordinal).Count() >= 20)
                    throw new InvalidOperationException("A learner can retain at most twenty practice contexts.");
            }
            LearningPracticeState practice = existing ??
                LearningPracticeState.Create(practiceId, this, contextKey, recipeKey, recipeVersion);
            LearningPracticeState projected = practice.Project(action, choice, step, value, intentId, fingerprint, utcNow);
            if (projected.PayloadBytes > 1024 ||
                TotalPracticePayloadBytes - (existing?.PayloadBytes ?? 0) + projected.PayloadBytes > 131072)
                throw new InvalidOperationException("The bounded practice payload capacity is exhausted.");
            practice.Apply(action, choice, step, value, intentId, fingerprint, utcNow);
            if (existing is null) _practices.Add(practice);
        }
        Remember(intentId, fingerprint, utcNow);
    }

    private bool HasLiveClaim(DateTime utcNow) => ActiveClaimId.HasValue && ClaimExpiresAtUtc > utcNow;

    private static void ValidateTour(Guid progressId, LearningTourFamily family, int materialVersion)
    {
        LearningGuard.Identity(progressId, nameof(progressId));
        if (!Enum.IsDefined(family)) throw new ArgumentOutOfRangeException(nameof(family));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(materialVersion);
    }

    private LearningTourProgress? FindTour(Guid progressId, LearningTourFamily family, int materialVersion)
    {
        LearningTourProgress? existing = _tours.SingleOrDefault(t => t.Family == family && t.MaterialVersion == materialVersion);
        if (existing is not null && existing.Id != progressId || _tours.Any(t => t.Id == progressId && t != existing))
            throw new InvalidOperationException("Tour identity does not match its family and material version.");
        int newest = _tours.Where(t => t.Family == family).Select(t => t.MaterialVersion).DefaultIfEmpty().Max();
        if (materialVersion < newest && existing is null)
            throw new InvalidOperationException("This tour material version is no longer retained.");
        return existing;
    }

    private bool IsDuplicate(Guid intentId, string fingerprint, DateTime utcNow)
    {
        LearningGuard.Identity(intentId, nameof(intentId));
        LearningGuard.Utc(utcNow);
        string? recorded = LastIntentId == intentId ? LastIntentFingerprint :
            _tours.FirstOrDefault(t => t.LastIntentId == intentId)?.LastIntentFingerprint ??
            _practices.FirstOrDefault(p => p.LastIntentId == intentId)?.LastIntentFingerprint;
        if (recorded is null) return false;
        if (!string.Equals(recorded, fingerprint, StringComparison.Ordinal))
            throw new InvalidOperationException("The learning intent was already used with a different payload.");
        return true;
    }

    private void ReleaseClaim()
    {
        ActiveClaimId = null;
        ActiveTourId = null;
        ActiveContextKey = null;
        ActiveEventContextId = null;
        ActivePlanFingerprint = null;
        ActivePolicySourceStamp = null;
        ClaimExpiresAtUtc = null;
    }

    private void Remember(Guid intentId, string fingerprint, DateTime utcNow)
    {
        LastIntentId = intentId;
        LastIntentFingerprint = fingerprint;
        Advance(utcNow);
    }

    private void Advance(DateTime utcNow)
    {
        Revision++;
        UpdatedAtUtc = utcNow;
    }
}

internal static class LearningGuard
{
    internal static void Identity(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
            throw new ArgumentException("Learning identities must be RFC 4122 UUIDv7 values.", parameterName);
    }

    internal static void Utc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Learning transitions require explicit UTC server time.", nameof(value));
    }

    internal static void Mutation(long revision, long expected, DateTime updatedAtUtc, DateTime utcNow)
    {
        Utc(utcNow);
        if (expected != revision || revision == long.MaxValue)
            throw new InvalidOperationException("The learning revision is stale or exhausted.");
        if (utcNow < updatedAtUtc)
            throw new InvalidOperationException("Learning transition time cannot move backwards.");
    }

    internal static void Context(string value)
    {
        if (value == "self") return;
        if (value is null || !value.StartsWith("actor:", StringComparison.Ordinal) ||
            !Guid.TryParseExact(value.AsSpan(6), "D", out Guid actorId) ||
            actorId == Guid.Empty || value != $"actor:{actorId:D}")
            throw new ArgumentException("Use the canonical self or actor context key.", nameof(value));
    }

    internal static string Fingerprint(FormattableString payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString(CultureInfo.InvariantCulture))));
}
