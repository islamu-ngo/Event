using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationAnswerTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TypedFactoriesSnapshotEverySupportedRelationalValueFamily()
    {
        AnswerScope text = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope integer = Scope(RegistrationFieldTypeEnum.Integer, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope decimalScope = Scope(RegistrationFieldTypeEnum.Decimal, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope boolean = Scope(RegistrationFieldTypeEnum.Boolean, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope date = Scope(RegistrationFieldTypeEnum.Date, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope time = Scope(RegistrationFieldTypeEnum.Time, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope instant = Scope(RegistrationFieldTypeEnum.Instant, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope option = Scope(RegistrationFieldTypeEnum.SingleChoice, RegistrationRequirementSubjectTypeEnum.AllOrders);
        RegistrationFormFieldOption selected = RegistrationFormFieldOption.Create(
            Guid.CreateVersion7(), option.Field, 1, "yes", "Yes", UtcNow);

        RegistrationAnswer[] answers =
        [
            RegistrationAnswer.CreateText(text.Submission, text.Field, text.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, text.OrderId, 1, "value", UtcNow),
            RegistrationAnswer.CreateInteger(integer.Submission, integer.Field, integer.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, integer.OrderId, 1, 42, UtcNow),
            RegistrationAnswer.CreateDecimal(decimalScope.Submission, decimalScope.Field, decimalScope.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, decimalScope.OrderId, 1, 12.50m, UtcNow),
            RegistrationAnswer.CreateBoolean(boolean.Submission, boolean.Field, boolean.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, boolean.OrderId, 1, true, UtcNow),
            RegistrationAnswer.CreateDate(date.Submission, date.Field, date.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, date.OrderId, 1, new DateOnly(2026, 8, 2), UtcNow),
            RegistrationAnswer.CreateTime(time.Submission, time.Field, time.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, time.OrderId, 1, new TimeOnly(12, 30), UtcNow),
            RegistrationAnswer.CreateInstant(instant.Submission, instant.Field, instant.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, instant.OrderId, 1, UtcNow.AddHours(1), UtcNow),
            RegistrationAnswer.CreateOption(option.Submission, option.Field, option.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, option.OrderId, 1, selected, UtcNow)
        ];

        await Assert.That(answers.Select(answer => answer.FieldTypeId)).IsEquivalentTo(new[] { 1, 3, 4, 5, 6, 7, 8, 14 });
        await Assert.That(answers.All(answer => answer.Ordinal == 1 && answer.RegistrationOrderId == answer.OrderSubjectId)).IsTrue();
        await Assert.That(answers[0].TextValue).IsEqualTo("value");
        await Assert.That(answers[1].IntegerValue).IsEqualTo(42L);
        await Assert.That(answers[2].DecimalValue).IsEqualTo(12.50m);
        await Assert.That(answers[3].BooleanValue).IsTrue();
        await Assert.That(answers[4].DateValue).IsEqualTo(new DateOnly(2026, 8, 2));
        await Assert.That(answers[5].TimeValue).IsEqualTo(new TimeOnly(12, 30));
        await Assert.That(answers[6].InstantValue).IsEqualTo(UtcNow.AddHours(1));
        await Assert.That(answers[7].SelectedOptionId).IsEqualTo(selected.Id);
    }

    [Test]
    public async Task SubjectFactoriesPermitExactlyTheFiveDeclaredSubjectKinds()
    {
        Guid participantId = Guid.CreateVersion7();
        Guid assignmentId = Guid.CreateVersion7();
        Guid sessionSelectionId = Guid.CreateVersion7();
        AnswerScope order = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope purchaser = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly);
        AnswerScope participant = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.EveryParticipant);
        AnswerScope assignment = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.SpecificTicketType, Guid.CreateVersion7());
        AnswerScope session = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection, sessionSelectionId);

        RegistrationAnswer[] answers =
        [
            RegistrationAnswer.CreateText(order.Submission, order.Field, order.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.OrderId, 1, "order", UtcNow),
            RegistrationAnswer.CreateText(purchaser.Submission, purchaser.Field, purchaser.Requirement,
                RegistrationAnswerSubjectTypeEnum.Purchaser, purchaser.OrderId, 1, "purchaser", UtcNow),
            RegistrationAnswer.CreateText(participant.Submission, participant.Field, participant.Requirement,
                RegistrationAnswerSubjectTypeEnum.Participant, participantId, 1, "participant", UtcNow),
            RegistrationAnswer.CreateText(assignment.Submission, assignment.Field, assignment.Requirement,
                RegistrationAnswerSubjectTypeEnum.TicketAssignment, assignmentId, 1, "assignment", UtcNow,
                Guid.CreateVersion7()),
            RegistrationAnswer.CreateText(session.Submission, session.Field, session.Requirement,
                RegistrationAnswerSubjectTypeEnum.SessionSelection, sessionSelectionId, 1, "session", UtcNow)
        ];

        await Assert.That(answers.Select(answer => answer.AnswerSubjectTypeId)).IsEquivalentTo(new[] { 1, 2, 3, 4, 5 });
        await Assert.That(answers.Count(answer => answer.OrderSubjectId.HasValue)).IsEqualTo(1);
        await Assert.That(answers.Count(answer => answer.PurchaserSubjectId.HasValue)).IsEqualTo(1);
        await Assert.That(answers.Count(answer => answer.ParticipantSubjectId.HasValue)).IsEqualTo(1);
        await Assert.That(answers.Count(answer => answer.TicketAssignmentSubjectId.HasValue)).IsEqualTo(1);
        await Assert.That(answers.Count(answer => answer.SessionSelectionSubjectId.HasValue)).IsEqualTo(1);
    }

    [Test]
    public async Task ConstructionRejectsWrongTypeSubjectOrdinalAndDedicatedLaterFamilies()
    {
        AnswerScope order = Scope(RegistrationFieldTypeEnum.Integer, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope participant = Scope(RegistrationFieldTypeEnum.ShortText, RegistrationRequirementSubjectTypeEnum.EveryParticipant);
        AnswerScope consent = Scope(RegistrationFieldTypeEnum.Consent, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly);
        AnswerScope file = Scope(RegistrationFieldTypeEnum.File, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope opaque = Scope(RegistrationFieldTypeEnum.OpaqueExternal, RegistrationRequirementSubjectTypeEnum.AllOrders);
        AnswerScope ticket = Scope(
            RegistrationFieldTypeEnum.ShortText,
            RegistrationRequirementSubjectTypeEnum.SpecificTicketType,
            Guid.CreateVersion7());

        await Assert.That(() => RegistrationAnswer.CreateText(order.Submission, order.Field, order.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.OrderId, 1, "wrong", UtcNow)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationAnswer.CreateText(participant.Submission, participant.Field, participant.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, participant.OrderId, 1, "wrong", UtcNow)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationAnswer.CreateInteger(order.Submission, order.Field, order.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.OrderId, 0, 1, UtcNow)).Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => RegistrationAnswer.CreateBoolean(consent.Submission, consent.Field, consent.Requirement,
            RegistrationAnswerSubjectTypeEnum.Purchaser, consent.OrderId, 1, true, UtcNow)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationAnswer.CreateText(file.Submission, file.Field, file.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, file.OrderId, 1, "file", UtcNow)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationAnswer.CreateText(opaque.Submission, opaque.Field, opaque.Requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, opaque.OrderId, 1, "opaque", UtcNow)).Throws<ArgumentException>();
        await Assert.That(() => RegistrationAnswer.CreateText(ticket.Submission, ticket.Field, ticket.Requirement,
            RegistrationAnswerSubjectTypeEnum.TicketAssignment, Guid.CreateVersion7(), 1, "ticket", UtcNow))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task SensitiveValueStoresOnlyVersionedCiphertextAndRejectsPlaintextShapes()
    {
        AnswerScope scope = Scope(RegistrationFieldTypeEnum.Email, RegistrationRequirementSubjectTypeEnum.LeadBookerOnly);
        string ciphertext = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        RegistrationSensitiveAnswerValue sensitive = RegistrationSensitiveAnswerValue.Create(
            scope.TenantId, ciphertext, 3, UtcNow);
        RegistrationAnswer answer = RegistrationAnswer.CreateSensitive(
            scope.Submission, scope.Field, scope.Requirement,
            RegistrationAnswerSubjectTypeEnum.Purchaser, scope.OrderId, 1, sensitive, UtcNow);

        await Assert.That(answer.SensitiveAnswerValueId).IsEqualTo(sensitive.Id);
        await Assert.That(sensitive.Ciphertext).IsEqualTo(ciphertext);
        await Assert.That(sensitive.KeyVersion).IsEqualTo(3);
        await Assert.That(typeof(RegistrationSensitiveAnswerValue).GetProperties()
            .Any(property => property.Name.Contains("Plaintext", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(() => RegistrationSensitiveAnswerValue.Create(scope.TenantId, "not-base64", 1, UtcNow))
            .Throws<ArgumentException>();
    }

    private static AnswerScope Scope(
        RegistrationFieldTypeEnum fieldType,
        RegistrationRequirementSubjectTypeEnum requirementSubjectType,
        Guid? requirementSubjectId = null)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        Guid orderId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "ATTENDEE_REGISTRATION", UtcNow);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL, requirementSubjectType, requirementSubjectId, UtcNow);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, UtcNow);
        RegistrationForm form = RegistrationForm.Create(tenantId, eventId, "platform.registration", "answers", "Answers", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        RegistrationFormSection section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Section", UtcNow);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "registration", "answer", "Answer", fieldType, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, fieldType == RegistrationFieldTypeEnum.Consent,
            false, UtcNow,
            fieldType == RegistrationFieldTypeEnum.Consent ? "terms" : null,
            fieldType == RegistrationFieldTypeEnum.Consent ? "v1" : null,
            fieldType == RegistrationFieldTypeEnum.Consent ? "I agree to the registration terms." : null);
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            tenantId, eventId, orderId, workflow.Id, requirement.Id, channel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(Hash("capability")), null, null, UtcNow, UtcNow.AddMinutes(10));
        RegistrationSubmission submission = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt, RegistrationEvidenceHash.Create(Hash("evidence")), UtcNow.AddMinutes(1), null);
        return new(tenantId, orderId, submission, requirement, field);
    }

    private static string Hash(string value) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record AnswerScope(
        Guid TenantId,
        Guid OrderId,
        RegistrationSubmission Submission,
        RegistrationRequirement Requirement,
        RegistrationFormField Field);
}
