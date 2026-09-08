using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class ParticipationRequirementAttachment :
    ITenantEntity,
    IAuditableEntity,
    ISoftDeletable,
    IConcurrencyAware
{
    private ParticipationRequirementAttachment()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid ParticipationConfigurationId { get; private set; }
    public Guid RegistrationWorkflowId { get; private set; }
    public Guid RegistrationRequirementId { get; private set; }
    public RegistrationRequirement? RegistrationRequirement { get; private set; }
    public Guid? RegistrationFormId { get; private set; }
    public Guid? RegistrationFormVersionId { get; private set; }
    public RegistrationFormVersion? RegistrationFormVersion { get; private set; }
    public bool IsStandaloneQuestionnaire { get; private set; }
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    internal static ParticipationRequirementAttachment Create(
        Guid id,
        EventParticipationConfiguration configuration,
        RegistrationWorkflow workflow,
        RegistrationRequirement requirement,
        RegistrationFormVersion? formVersion,
        bool isStandaloneQuestionnaire,
        DateTime createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Attachment id is required.", nameof(id));
        }

        return new ParticipationRequirementAttachment
        {
            Id = id,
            TenantId = configuration.TenantId,
            EventId = configuration.Id,
            ParticipationConfigurationId = configuration.Id,
            RegistrationWorkflowId = workflow.Id,
            RegistrationRequirementId = requirement.Id,
            RegistrationRequirement = requirement,
            RegistrationFormId = formVersion?.RegistrationFormId,
            RegistrationFormVersionId = formVersion?.Id,
            RegistrationFormVersion = formVersion,
            IsStandaloneQuestionnaire = isStandaloneQuestionnaire,
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    internal void Detach(DateTime detachedAt)
    {
        IsDeleted = true;
        DeletedAt = detachedAt;
        ConcurrencyStamp = Guid.CreateVersion7();
    }
}
