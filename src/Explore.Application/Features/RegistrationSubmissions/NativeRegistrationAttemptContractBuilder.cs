using Explore.Application.DTOs.RegistrationForms;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.RegistrationForms;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

internal static class NativeRegistrationAttemptContractBuilder
{
    public static NativeRegistrationFormDefinitionDto Form(RegistrationFormVersion version)
    {
        RegistrationFormVersionDto source = RegistrationFormAuthoringMapper.ToDto(version);
        return new(
            source.Id,
            source.Version,
            source.LanguageTag,
            source.SchemaHash,
            [.. source.Sections.Select(section => new NativeRegistrationFormSectionDto(
                section.Id,
                section.Ordinal,
                section.Title,
                [.. section.Fields.Select(field => new NativeRegistrationFormFieldDto(
                    field.Id,
                    field.Ordinal,
                    field.Namespace,
                    field.Key,
                    field.Label,
                    field.FieldTypeId,
                    field.FieldTypeCode,
                    field.FieldTypeName,
                    field.RequiresExplicitConsent,
                    field.ConsentPurposeCode,
                    field.ConsentTextVersion,
                    field.ConsentText,
                    field.IsRequired,
                    field.IsMulti,
                    field.MinLength,
                    field.MaxLength,
                    field.RegexPattern,
                    field.MinNumber,
                    field.MaxNumber,
                    field.MinDateTime,
                    field.MaxDateTime,
                    field.AllowedUrlSchemes,
                    [.. field.Options.Select(option => new NativeRegistrationFormFieldOptionDto(
                        option.Id, option.Ordinal, option.Key, option.Label, option.RetiredAt.HasValue))]))]))],
            [.. source.Rules.Select(rule => new NativeRegistrationFormRuleDto(
                rule.Id, rule.Ordinal, rule.TargetNamespace, rule.TargetKey, rule.Effect, rule.Condition))]);
    }

    public static IReadOnlyList<NativeRegistrationAnswerSubjectDto> Subjects(
        RegistrationOrder order,
        RegistrationRequirement requirement,
        IReadOnlyList<RegistrationParticipant> participants,
        IReadOnlyList<RegistrationTicketAssignment> assignments,
        IReadOnlyList<RegistrationRequirementFulfillment> fulfillments)
    {
        Dictionary<Guid, RegistrationOrderLine> lines = order.Lines.ToDictionary(line => line.Id);
        IEnumerable<(RegistrationAnswerSubjectTypeEnum Type, Guid Id, Guid? LineId)> candidates =
            (RegistrationRequirementSubjectTypeEnum)requirement.AppliesToSubjectTypeId switch
            {
                RegistrationRequirementSubjectTypeEnum.AllOrders =>
                    [(RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, null)],
                RegistrationRequirementSubjectTypeEnum.SpecificTicketType when requirement.AppliesToSubjectId is Guid ticketTypeId =>
                    assignments.Where(assignment =>
                            lines.TryGetValue(assignment.RegistrationOrderLineId, out RegistrationOrderLine? line) &&
                            line.TicketTypeId == ticketTypeId)
                        .Select(assignment => (
                            RegistrationAnswerSubjectTypeEnum.TicketAssignment,
                            assignment.Id,
                            (Guid?)assignment.RegistrationOrderLineId)),
                RegistrationRequirementSubjectTypeEnum.EveryParticipant =>
                    participants.Select(participant => (
                        RegistrationAnswerSubjectTypeEnum.Participant, participant.Id, (Guid?)null)),
                RegistrationRequirementSubjectTypeEnum.LeadBookerOnly =>
                    [(RegistrationAnswerSubjectTypeEnum.Purchaser, order.Id, null)],
                RegistrationRequirementSubjectTypeEnum.ChildParticipants =>
                    participants.Where(participant => participant.ParticipantTypeId == (int)ParticipantTypeEnum.Child)
                        .Select(participant => (
                            RegistrationAnswerSubjectTypeEnum.Participant, participant.Id, (Guid?)null)),
                RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection when requirement.AppliesToSubjectId is Guid sessionId =>
                    [(RegistrationAnswerSubjectTypeEnum.SessionSelection, sessionId, null)],
                _ => []
            };

        Dictionary<(int Type, Guid Id), RegistrationRequirementFulfillment> evidence = fulfillments
            .Where(value => value.RegistrationRequirementId == requirement.Id)
            .GroupBy(value => (value.SubjectTypeId, value.SubjectId))
            .ToDictionary(group => group.Key, group => group.OrderByDescending(value => value.RecordedAt).First());
        return [.. candidates
            .Distinct()
            .OrderBy(value => value.Type)
            .ThenBy(value => value.Id)
            .Select(value =>
            {
                evidence.TryGetValue(((int)value.Type, value.Id), out RegistrationRequirementFulfillment? fulfillment);
                string code = SubjectCode(value.Type);
                return new NativeRegistrationAnswerSubjectDto(
                    value.Type,
                    code,
                    value.Id,
                    $"{code}:{value.Id:D}",
                    value.LineId,
                    fulfillment is not null && !fulfillment.IsSkipped,
                    fulfillment?.IsSkipped == true);
            })];
    }

    public static NativeRegistrationRequirementProgressDto Progress(
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects)
    {
        int completed = subjects.Count(subject => subject.IsCompleted);
        int skipped = subjects.Count(subject => subject.IsSkipped);
        int pending = subjects.Count - completed - skipped;
        return new(subjects.Count, completed, skipped, pending, subjects.Count > 0 && pending == 0);
    }

    private static string SubjectCode(RegistrationAnswerSubjectTypeEnum type) => type switch
    {
        RegistrationAnswerSubjectTypeEnum.RegistrationOrder => "REGISTRATION_ORDER",
        RegistrationAnswerSubjectTypeEnum.TicketAssignment => "TICKET_ASSIGNMENT",
        RegistrationAnswerSubjectTypeEnum.SessionSelection => "SESSION_SELECTION",
        _ => type.ToString().ToUpperInvariant()
    };
}
