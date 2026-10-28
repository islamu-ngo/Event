using System.Globalization;
using System.Text;
using Explore.Domain.Interfaces;

namespace Explore.Domain.Learning;

public sealed class LearningPracticeState : ITenantEntity
{
    private Guid _tenantId;
    private LearningPracticeState() { }

    public Guid Id { get; private set; }
    public Guid TenantId
    {
        get => _tenantId;
        private set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningPracticeState));
    }
    Guid ITenantEntity.TenantId
    {
        get => TenantId;
        set => TenantIdentity.Set(ref _tenantId, value, nameof(LearningPracticeState));
    }
    public Guid UserLearningStateId { get; private set; }
    public string ContextKey { get; private set; } = "self";
    public string RecipeKey { get; private set; } = string.Empty;
    public int RecipeVersion { get; private set; }
    public long Revision { get; private set; }
    public LearningPracticeAction LastAction { get; private set; }
    public LearningPracticeChoice Choice { get; private set; }
    public int Step { get; private set; }
    public int Value { get; private set; }
    public bool IsConfirmed { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid LastIntentId { get; private set; }
    public string LastIntentFingerprint { get; private set; } = string.Empty;

    /// <summary>
    /// UTF-8 size of the canonical machine-state projection. No caller-authored text is stored.
    /// The fixed scalar shape also bounds all 120 records below the learner payload cap.
    /// </summary>
    public int PayloadBytes => Encoding.UTF8.GetByteCount(string.Create(CultureInfo.InvariantCulture,
        $"{Id:D}|{TenantId:D}|{UserLearningStateId:D}|{ContextKey}|{RecipeKey}|{RecipeVersion}|{Revision}|{(int)LastAction}|{(int)Choice}|{Step}|{Value}|{IsConfirmed}|{UpdatedAtUtc:O}|{LastIntentId:D}|{LastIntentFingerprint}"));

    internal static LearningPracticeState Create(Guid id, UserLearningState owner, string contextKey,
        string recipeKey, int recipeVersion) =>
        new()
        {
            Id = id, TenantId = owner.TenantId, UserLearningStateId = owner.Id,
            ContextKey = contextKey, RecipeKey = recipeKey, RecipeVersion = recipeVersion
        };

    internal void Apply(LearningPracticeAction action, LearningPracticeChoice choice, int step, int value,
        Guid intentId, string fingerprint, DateTime utcNow)
    {
        switch (action)
        {
            case LearningPracticeAction.Start when Revision == 0:
            case LearningPracticeAction.Reset:
                Choice = LearningPracticeChoice.None;
                Step = 0;
                Value = 0;
                IsConfirmed = false;
                break;
            case LearningPracticeAction.Choose when Revision > 0 && !IsConfirmed:
                if (choice == LearningPracticeChoice.None || step < Step)
                    throw new InvalidOperationException("Choose an authored option without moving backwards.");
                Choice = choice;
                Step = step;
                Value = value;
                break;
            case LearningPracticeAction.Confirm when Revision > 0 && !IsConfirmed && Choice != LearningPracticeChoice.None:
                Step = 2;
                IsConfirmed = true;
                break;
            default:
                throw new InvalidOperationException("The practice action is not valid in the current state.");
        }
        LastAction = action;
        LastIntentId = intentId;
        LastIntentFingerprint = fingerprint;
        UpdatedAtUtc = utcNow;
        Revision++;
    }

    internal LearningPracticeState Project(LearningPracticeAction action, LearningPracticeChoice choice,
        int step, int value, Guid intentId, string fingerprint, DateTime utcNow)
    {
        var projection = (LearningPracticeState)MemberwiseClone();
        projection.Apply(action, choice, step, value, intentId, fingerprint, utcNow);
        return projection;
    }
}
