using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationRequirement : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationChannel> _channels = [];

    private RegistrationRequirement()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public int Ordinal { get; private set; }
    public int CriticalityId { get; private set; }
    public int CompletionEffectId { get; private set; }
    public int AnswerSyncModeId { get; private set; }
    public int AppliesToSubjectTypeId { get; private set; }
    public Guid? AppliesToSubjectId { get; private set; }
    public Guid AppliesToSubjectKey { get; private set; }
    public bool CanSkip { get; private set; }
    public IReadOnlyList<RegistrationChannel> Channels => _channels;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationRequirement Create(
        RegistrationWorkflow workflow,
        int ordinal,
        RegistrationRequirementCriticalityEnum criticality,
        bool canSkip,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        RegistrationAnswerSyncModeEnum answerSyncMode,
        RegistrationRequirementSubjectTypeEnum appliesToSubjectType,
        Guid? appliesToSubjectId,
        DateTime createdAt) => Create(
            Guid.CreateVersion7(), workflow, ordinal, criticality, canSkip, completionEffect, answerSyncMode,
            appliesToSubjectType, appliesToSubjectId, createdAt);

    public static RegistrationRequirement Create(
        Guid id,
        RegistrationWorkflow workflow,
        int ordinal,
        RegistrationRequirementCriticalityEnum criticality,
        bool canSkip,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        RegistrationAnswerSyncModeEnum answerSyncMode,
        RegistrationRequirementSubjectTypeEnum appliesToSubjectType,
        Guid? appliesToSubjectId,
        DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Requirement ordinal must be positive.");
        }

        if (id == Guid.Empty || !Enum.IsDefined(criticality) || !Enum.IsDefined(completionEffect) ||
            !Enum.IsDefined(answerSyncMode) || !Enum.IsDefined(appliesToSubjectType) || appliesToSubjectId == Guid.Empty)
        {
            throw new ArgumentException("Requirement identity and lookup values must be valid.");
        }

        ValidatePolicy(criticality, canSkip, completionEffect, appliesToSubjectType, appliesToSubjectId);

        return new RegistrationRequirement
        {
            Id = id,
            TenantId = workflow.TenantId,
            EventId = workflow.EventId,
            RegistrationWorkflowId = workflow.Id,
            Ordinal = ordinal,
            CriticalityId = (int)criticality,
            CanSkip = canSkip,
            CompletionEffectId = (int)completionEffect,
            AnswerSyncModeId = (int)answerSyncMode,
            AppliesToSubjectTypeId = (int)appliesToSubjectType,
            AppliesToSubjectId = appliesToSubjectId,
            CreatedAt = EnsureUtc(createdAt)
        };
    }

    public void AddChannel(RegistrationChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (channel.TenantId != TenantId || channel.EventId != EventId ||
            channel.RegistrationWorkflowId != RegistrationWorkflowId || channel.RegistrationRequirementId != Id ||
            _channels.Any(existing => !existing.IsDeleted && (existing.Id == channel.Id || existing.Ordinal == channel.Ordinal)))
        {
            throw new ArgumentException("Channel must be unique and owned by this requirement.", nameof(channel));
        }

        _channels.Add(channel);
    }

    internal void Update(
        int ordinal,
        RegistrationRequirementCriticalityEnum criticality,
        bool canSkip,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        RegistrationAnswerSyncModeEnum answerSyncMode,
        RegistrationRequirementSubjectTypeEnum appliesToSubjectType,
        Guid? appliesToSubjectId)
    {
        if (ordinal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ordinal), "Requirement ordinal must be positive.");
        }

        if (!Enum.IsDefined(criticality) || !Enum.IsDefined(completionEffect) || !Enum.IsDefined(answerSyncMode) ||
            !Enum.IsDefined(appliesToSubjectType) || appliesToSubjectId == Guid.Empty)
        {
            throw new ArgumentException("Requirement lookup values must be valid.");
        }

        ValidatePolicy(criticality, canSkip, completionEffect, appliesToSubjectType, appliesToSubjectId);
        Ordinal = ordinal;
        CriticalityId = (int)criticality;
        CanSkip = canSkip;
        CompletionEffectId = (int)completionEffect;
        AnswerSyncModeId = (int)answerSyncMode;
        AppliesToSubjectTypeId = (int)appliesToSubjectType;
        AppliesToSubjectId = appliesToSubjectId;
    }

    internal void Remove(DateTime removedAt)
    {
        EnsureUtc(removedAt);
        IsDeleted = true;
        DeletedAt = removedAt;
    }

    public RegistrationRequirementEvaluation Evaluate(
        RegistrationRequirementSubjectContext subject,
        IEnumerable<RegistrationChannelCompletion> channelCompletions,
        bool skippedByRegistrant)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(channelCompletions);
        if (subject.TenantId != TenantId || subject.RegistrationWorkflowId != RegistrationWorkflowId)
        {
            throw new ArgumentException("Evaluation subject is outside this tenant or workflow.", nameof(subject));
        }

        RegistrationChannelCompletion[] completions = channelCompletions.ToArray();
        if (completions.Select(value => value.RegistrationChannelId).Distinct().Count() != completions.Length ||
            completions.Any(value => value.TenantId != TenantId || value.RegistrationWorkflowId != RegistrationWorkflowId ||
                value.RegistrationRequirementId != Id || _channels.All(channel => channel.Id != value.RegistrationChannelId)))
        {
            throw new ArgumentException("Channel completions must be unique and owned by this requirement.", nameof(channelCompletions));
        }

        if (!AppliesTo(subject))
        {
            return new(Id, RegistrationRequirementEvaluationOutcome.NotApplicable, false, false);
        }

        if (skippedByRegistrant)
        {
            if (!CanSkip || CriticalityId == (int)RegistrationRequirementCriticalityEnum.Required)
            {
                throw new InvalidOperationException("REGISTRATION_REQUIREMENT_SKIP_FORBIDDEN");
            }

            return new(Id, RegistrationRequirementEvaluationOutcome.SkippedByRegistrant, false, false);
        }

        bool satisfied = AnswerSyncModeId switch
        {
            (int)RegistrationAnswerSyncModeEnum.NONE or (int)RegistrationAnswerSyncModeEnum.MIRROR_ONLY => false,
            (int)RegistrationAnswerSyncModeEnum.COMPLETION_ONLY => completions.Any(value => value.IsCompleted && value.IsVerified),
            _ => completions.Any(value => value.IsCompleted)
        };
        bool blocks = CriticalityId == (int)RegistrationRequirementCriticalityEnum.Required && !satisfied;
        RegistrationRequirementEvaluationOutcome outcome = satisfied
            ? RegistrationRequirementEvaluationOutcome.Satisfied
            : blocks
                ? RegistrationRequirementEvaluationOutcome.Blocking
                : RegistrationRequirementEvaluationOutcome.IncompleteNonBlocking;
        return new(Id, outcome, satisfied, blocks);
    }

    private bool AppliesTo(RegistrationRequirementSubjectContext subject) =>
        (RegistrationRequirementSubjectTypeEnum)AppliesToSubjectTypeId switch
        {
            RegistrationRequirementSubjectTypeEnum.AllOrders => true,
            RegistrationRequirementSubjectTypeEnum.SpecificTicketType => subject.TicketTypeId == AppliesToSubjectId,
            RegistrationRequirementSubjectTypeEnum.EveryParticipant => subject.ParticipantId.HasValue,
            RegistrationRequirementSubjectTypeEnum.LeadBookerOnly => subject.IsLeadBooker,
            RegistrationRequirementSubjectTypeEnum.ChildParticipants => subject.ParticipantId.HasValue && subject.IsChildParticipant,
            RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection => subject.SessionSelectionId == AppliesToSubjectId,
            _ => false
        };

    private static void ValidatePolicy(
        RegistrationRequirementCriticalityEnum criticality,
        bool canSkip,
        RegistrationRequirementCompletionEffectEnum completionEffect,
        RegistrationRequirementSubjectTypeEnum appliesToSubjectType,
        Guid? appliesToSubjectId)
    {
        bool needsSpecificSubject = appliesToSubjectType is RegistrationRequirementSubjectTypeEnum.SpecificTicketType or
            RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection;
        if (needsSpecificSubject != appliesToSubjectId.HasValue)
        {
            throw new ArgumentException("Only specific ticket or session requirements need a subject id.", nameof(appliesToSubjectId));
        }

        bool validPolicy = criticality switch
        {
            RegistrationRequirementCriticalityEnum.Required => !canSkip && completionEffect == RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationRequirementCriticalityEnum.Optional => canSkip && completionEffect == RegistrationRequirementCompletionEffectEnum.EnrichesRegistration,
            RegistrationRequirementCriticalityEnum.Informational or RegistrationRequirementCriticalityEnum.PostRegistration =>
                canSkip && completionEffect == RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect,
            _ => false
        };
        if (!validPolicy)
        {
            throw new ArgumentException("Criticality, completion effect, and skip policy are inconsistent.");
        }
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Timestamp must be a non-default UTC value.", nameof(value));
        }

        return value;
    }
}

public enum RegistrationRequirementEvaluationOutcome
{
    Satisfied = 1,
    Blocking = 2,
    IncompleteNonBlocking = 3,
    NotApplicable = 4,
    SkippedByRegistrant = 5
}

public sealed record RegistrationRequirementEvaluation(
    Guid RegistrationRequirementId,
    RegistrationRequirementEvaluationOutcome Outcome,
    bool IsSatisfied,
    bool BlocksRegistration);

public sealed record RegistrationChannelCompletion(
    Guid TenantId,
    Guid RegistrationWorkflowId,
    Guid RegistrationRequirementId,
    Guid RegistrationChannelId,
    bool IsCompleted,
    bool IsVerified);

public sealed record RegistrationRequirementSubjectContext
{
    private RegistrationRequirementSubjectContext()
    {
    }

    public Guid TenantId { get; private init; }
    public Guid RegistrationWorkflowId { get; private init; }
    public Guid? TicketTypeId { get; private init; }
    public Guid? ParticipantId { get; private init; }
    public bool IsLeadBooker { get; private init; }
    public bool IsChildParticipant { get; private init; }
    public Guid? SessionSelectionId { get; private init; }

    public static RegistrationRequirementSubjectContext Create(
        Guid tenantId,
        Guid registrationWorkflowId,
        Guid? ticketTypeId = null,
        Guid? participantId = null,
        bool isLeadBooker = false,
        bool isChildParticipant = false,
        Guid? sessionSelectionId = null)
    {
        if (tenantId == Guid.Empty || registrationWorkflowId == Guid.Empty || ticketTypeId == Guid.Empty ||
            participantId == Guid.Empty || sessionSelectionId == Guid.Empty || isChildParticipant && !participantId.HasValue)
        {
            throw new ArgumentException("Subject identity and classification must be valid.");
        }

        return new()
        {
            TenantId = tenantId,
            RegistrationWorkflowId = registrationWorkflowId,
            TicketTypeId = ticketTypeId,
            ParticipantId = participantId,
            IsLeadBooker = isLeadBooker,
            IsChildParticipant = isChildParticipant,
            SessionSelectionId = sessionSelectionId
        };
    }
}
