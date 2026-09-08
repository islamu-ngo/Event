using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Contracts.Services;

namespace Explore.Blazor.Client.Services;

public sealed class NativeRegistrationFormService(
    IAuthenticatedRegistrationOrderClient authenticatedClient,
    IGuestRegistrationOrderClient guestClient,
    ILogger<NativeRegistrationFormService> logger) : INativeRegistrationFormService
{
    public async Task<NativeRegistrationRequirementCollectionView?> GetRequirementsAsync(
        Guid eventId, Guid orderId, GuestRegistrationOrderCapability? guestCapability,
        CancellationToken cancellationToken = default)
    {
        try
        {
            HalResourceOfNativeRegistrationRequirementProgressCollectionDto source = guestCapability is { } guest
                ? await guestClient.GetGuestNativeRegistrationRequirementProgressAsync(
                    eventId, orderId, guest.Value, cancellationToken: cancellationToken)
                : await authenticatedClient.GetAuthenticatedNativeRegistrationRequirementProgressAsync(
                    eventId, orderId, cancellationToken: cancellationToken);
            return NativeRegistrationRequirementCollectionView.From(source);
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Native registration requirements were unavailable. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
    }

    public async Task<NativeRegistrationAttemptView?> LaunchAsync(
        Guid eventId, Guid orderId, NativeRegistrationLaunchDescriptorView descriptor,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new LaunchNativeRegistrationAttemptRequest
        {
            RequirementId = descriptor.RequirementId,
            ChannelId = descriptor.ChannelId,
            FormId = descriptor.FormId,
            FormVersionId = descriptor.FormVersionId
        };
        try
        {
            HalResourceOfNativeRegistrationAttemptDto source = guestCapability is { } guest
                ? await guestClient.LaunchGuestNativeRegistrationAttemptAsync(
                    eventId, orderId, idempotencyKey, request, guest.Value, cancellationToken: cancellationToken)
                : await authenticatedClient.LaunchAuthenticatedNativeRegistrationAttemptAsync(
                    eventId, orderId, idempotencyKey, request, cancellationToken: cancellationToken);
            return NativeRegistrationAttemptView.From(source);
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Native registration attempt could not be launched. Status: {StatusCode}.", exception.StatusCode);
            return null;
        }
    }

    public async Task<NativeRegistrationActionResult> SubmitAsync(
        Guid eventId, Guid orderId, NativeRegistrationAttemptView attempt,
        RegistrationSubjectView subject, RegistrationFormSubmission submission,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new SubmitNativeRegistrationAttemptRequest
        {
            RequirementId = attempt.RequirementId,
            Answers = submission.Answers.Select(answer => new NativeRegistrationSubmissionAnswerRequest
            {
                FieldId = answer.Key,
                SubjectType = MapSubjectType(subject.SubjectType),
                SubjectId = subject.SubjectId,
                TicketAssignmentOrderLineId = subject.TicketAssignmentOrderLineId,
                Value = answer.Value!
            }).ToArray()
        };
        try
        {
            NativeRegistrationSubmissionDto response = guestCapability is { } guest
                ? await guestClient.SubmitGuestNativeRegistrationAttemptAsync(
                    eventId, orderId, attempt.AttemptId, idempotencyKey, request, guest.Value,
                    attempt.AttemptCapabilityToken, cancellationToken: cancellationToken)
                : await authenticatedClient.SubmitAuthenticatedNativeRegistrationAttemptAsync(
                    eventId, orderId, attempt.AttemptId, idempotencyKey, request,
                    attempt.AttemptCapabilityToken, cancellationToken: cancellationToken);
            return response.Accepted ? NativeRegistrationActionResult.Accepted : NativeRegistrationActionResult.Failed;
        }
        catch (ApiException<ValidationProblemDetails> exception)
        {
            return NativeRegistrationActionResult.Invalid(MapIssues(attempt, exception.Result?.Errors));
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Native registration answers could not be submitted. Status: {StatusCode}.", exception.StatusCode);
            return NativeRegistrationActionResult.Failed;
        }
    }

    public async Task<bool> SkipAsync(
        Guid eventId, Guid orderId, NativeRegistrationAttemptView attempt,
        GuestRegistrationOrderCapability? guestCapability, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var request = new SkipNativeRegistrationRequirementRequest { RequirementId = attempt.RequirementId };
        try
        {
            _ = guestCapability is { } guest
                ? await guestClient.SkipGuestNativeRegistrationRequirementAsync(
                    eventId, orderId, attempt.AttemptId, idempotencyKey, request, guest.Value,
                    attempt.AttemptCapabilityToken, cancellationToken: cancellationToken)
                : await authenticatedClient.SkipAuthenticatedNativeRegistrationRequirementAsync(
                    eventId, orderId, attempt.AttemptId, idempotencyKey, request,
                    attempt.AttemptCapabilityToken, cancellationToken: cancellationToken);
            return true;
        }
        catch (ApiException exception)
        {
            logger.LogWarning("Native registration requirement could not be skipped. Status: {StatusCode}.", exception.StatusCode);
            return false;
        }
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<string>> MapIssues(
        NativeRegistrationAttemptView attempt,
        IDictionary<string, ICollection<string>>? errors)
    {
        if (errors is null) return new Dictionary<Guid, IReadOnlyList<string>>();
        Dictionary<string, Guid> fields = attempt.Sections.SelectMany(section => section.Fields)
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single().Id, StringComparer.OrdinalIgnoreCase);
        return errors
            .Where(error => fields.ContainsKey(error.Key))
            .ToDictionary(error => fields[error.Key], error => (IReadOnlyList<string>)error.Value
                .Select(SafeIssueMessage).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static string SafeIssueMessage(string code) => code switch
    {
        "REQUIRED_FIELD_MISSING" => "Enter an answer.",
        "LENGTH_OUT_OF_RANGE" => "Use the allowed number of characters.",
        "INVALID_OPTION" => "Choose one of the available options.",
        _ => "Review the format of this answer."
    };

    private static RegistrationAnswerSubjectTypeEnum MapSubjectType(int subjectType) => subjectType switch
    {
        1 => RegistrationAnswerSubjectTypeEnum.RegistrationOrder,
        2 => RegistrationAnswerSubjectTypeEnum.Purchaser,
        3 => RegistrationAnswerSubjectTypeEnum.Participant,
        4 => RegistrationAnswerSubjectTypeEnum.TicketAssignment,
        5 => RegistrationAnswerSubjectTypeEnum.SessionSelection,
        _ => throw new ArgumentOutOfRangeException(nameof(subjectType), subjectType, "Unknown registration answer subject type.")
    };
}
