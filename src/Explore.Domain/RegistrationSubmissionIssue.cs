using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationSubmissionIssue : ITenantEntity, IAuditableEntity
{
    private RegistrationSubmissionIssue()
    {
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid EventId { get; private set; }
    public Guid RegistrationAttemptId { get; private set; }
    public Guid RegistrationSubmissionId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public Guid? RegistrationFormFieldId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }

    public static RegistrationSubmissionIssue Create(
        RegistrationSubmission submission,
        string code,
        DateTime createdAt,
        Guid? fieldId = null)
    {
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (createdAt == default || createdAt.Kind != DateTimeKind.Utc || code.Length > 100 || fieldId == Guid.Empty)
        {
            throw new ArgumentException("Issue code, optional field identity, and UTC creation time must be valid.");
        }

        return new RegistrationSubmissionIssue
        {
            Id = Guid.CreateVersion7(),
            TenantId = submission.TenantId,
            EventId = submission.EventId,
            RegistrationAttemptId = submission.RegistrationAttemptId,
            RegistrationSubmissionId = submission.Id,
            RegistrationFormVersionId = submission.RegistrationFormVersionId,
            RegistrationFormFieldId = fieldId,
            Code = code.Trim().ToUpperInvariant(),
            CreatedAt = createdAt
        };
    }
}
