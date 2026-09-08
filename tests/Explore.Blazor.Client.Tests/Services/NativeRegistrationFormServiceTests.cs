using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class NativeRegistrationFormServiceTests
{
    private readonly IAuthenticatedRegistrationOrderClient _api = Substitute.For<IAuthenticatedRegistrationOrderClient>();
    private readonly NativeRegistrationFormService _service;

    public NativeRegistrationFormServiceTests() =>
        _service = new NativeRegistrationFormService(
            _api,
            Substitute.For<IGuestRegistrationOrderClient>(),
            Substitute.For<ILogger<NativeRegistrationFormService>>());

    [Test]
    public async Task SubmitAuthenticated_UsesExactSubjectAttemptCapabilityAndIdempotency()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        Guid fieldId = Guid.CreateVersion7();
        var subject = new RegistrationSubjectView(2, Guid.CreateVersion7(), "participant:1", null, false, false);
        var attempt = new NativeRegistrationAttemptView(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "opaque-attempt-capability", "en",
            [new RegistrationSectionView(Guid.CreateVersion7(), 1, "Details",
                [new RegistrationFieldView(fieldId, 1, "attendee", "name", "Name", "SHORT_TEXT",
                    true, false, null, null, null, null, null, null, null, null, null, null, [])])],
            [], [subject], new RegistrationRequirementProgressView(1, 0, 0, 1, false),
            new Dictionary<string, HalLink> { ["submit"] = new() { Href = "/submit", Method = "POST" } });
        string idempotencyKey = Guid.CreateVersion7().ToString("N");
        _api.SubmitAuthenticatedNativeRegistrationAttemptAsync(
                eventId, orderId, attempt.AttemptId, idempotencyKey, Arg.Any<SubmitNativeRegistrationAttemptRequest>(),
                attempt.AttemptCapabilityToken, null, null, Arg.Any<CancellationToken>())
            .Returns(new NativeRegistrationSubmissionDto { Accepted = true, SubmissionId = Guid.CreateVersion7() });

        NativeRegistrationActionResult result = await _service.SubmitAsync(
            eventId, orderId, attempt, subject,
            new RegistrationFormSubmission(new Dictionary<Guid, object?> { [fieldId] = "Amina" }),
            null, idempotencyKey);

        await Assert.That(result.Success).IsTrue();
        await _api.Received(1).SubmitAuthenticatedNativeRegistrationAttemptAsync(
            eventId, orderId, attempt.AttemptId, idempotencyKey,
            Arg.Is<SubmitNativeRegistrationAttemptRequest>((SubmitNativeRegistrationAttemptRequest request) =>
                request.RequirementId == attempt.RequirementId &&
                request.Answers.Count == 1 &&
                request.Answers.Single().FieldId == fieldId &&
                request.Answers.Single().SubjectId == subject.SubjectId &&
                request.Answers.Single().SubjectType == RegistrationAnswerSubjectTypeEnum.Purchaser),
            attempt.AttemptCapabilityToken, null, null, Arg.Any<CancellationToken>());
    }
}
