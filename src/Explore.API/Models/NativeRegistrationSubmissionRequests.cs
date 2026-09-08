using Explore.Domain.Enums;
using Explore.Application.DTOs.RegistrationSubmissions;

namespace Explore.API.Models;

public sealed record LaunchNativeRegistrationAttemptRequest(
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    Guid? BindingId = null,
    Guid? SupersededAttemptId = null);

public sealed record LaunchRegistrationProviderAttemptRequest(
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    Guid? SupersededAttemptId = null);

public sealed record SubmitNativeRegistrationAttemptRequest(
    Guid RequirementId,
    IReadOnlyList<NativeRegistrationSubmissionAnswerRequest> Answers);

public sealed record SkipNativeRegistrationRequirementRequest(Guid RequirementId);

public sealed record NativeRegistrationSubmissionAnswerRequest(
    Guid FieldId,
    RegistrationAnswerSubjectTypeEnum SubjectType,
    Guid SubjectId,
    Guid? TicketAssignmentOrderLineId,
    object? Value);

public sealed record NativeRegistrationAttemptDto(
    Guid AttemptId,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    DateTime ExpiresAt,
    string AttemptCapabilityToken,
    NativeRegistrationFormDefinitionDto Form,
    IReadOnlyList<NativeRegistrationAnswerSubjectDto> Subjects,
    NativeRegistrationRequirementProgressDto Progress);

public sealed record NativeRegistrationSubmissionDto(Guid SubmissionId, bool Accepted);

public sealed record NativeRegistrationSkipDto(NativeRegistrationRequirementProgressDto Progress);
