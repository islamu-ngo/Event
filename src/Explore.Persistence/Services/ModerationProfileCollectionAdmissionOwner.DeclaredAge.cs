using Explore.Application.Contracts.Services;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed partial class ModerationProfileCollectionAdmissionOwner
{
    private async Task<ModerationDeclaredAgeDisplay> ReadDeclaredAgeAsync(
        ModerationProfileCollectionAdmission admission, CancellationToken token)
    {
        await RequireCurrentAsync(admission.SubjectId, token);
        if (current is not { } retained) throw new AdmissionUnavailable();
        var metadata = await ReadAsync(retained.Acquisition, retained.Expected, retained.ConsentId, token);
        if (metadata is null || metadata.AgeSource.Calendar != ModerationChildAgeCalendar.Gregorian
            || metadata.AgeSource.TimeZone != ModerationChildAgeTimeZone.Utc
            || metadata.AgeSource.LeapDay != ModerationChildAgeLeapDay.MarchFirstInNonLeapYear)
            throw new AdmissionUnavailable();

        var pii = await context.UserPii.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == admission.SubjectId, token);
        if (pii is null) throw new AdmissionUnavailable();
        var receiptId = pii.ReadBirthDateCollectionReceiptId();
        var receipt = receiptId is { } id
            ? await context.Set<UserBirthDateCollectionReceipt>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == id && row.UserId == admission.SubjectId, token)
            : null;
        var now = clock.GetUtcNow();
        var until = admission.ValidUntil;
        var tomorrow = now.UtcDateTime.Date == DateTime.MaxValue.Date
            ? DateTimeOffset.MaxValue : new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        if (tomorrow < until) until = tomorrow;
        var original = retained.Expected.Original;
        var matches = receipt is not null && pii.ReadBirthDateRevision() > 0
            && receipt.ObservationId == original.ObservationId
            && receipt.AssessmentId == retained.Expected.SourceId
            && receipt.AssessmentAdoptionId == retained.Expected.AdoptionId
            && receipt.ConsentId == retained.ConsentId
            && receipt.PolicyId == original.PolicyId && receipt.PolicyRevision == original.PolicyRevision
            && receipt.PublicationId == original.PublicationId && receipt.ProfileId == original.ProfileId
            && receipt.CollectedAt <= now && now < receipt.ValidUntil;
        if (matches && receipt!.ValidUntil < until) until = receipt.ValidUntil;
        if (until <= now) throw new AdmissionUnavailable();

        var policy = new ModerationAgePolicy(metadata.AgeSource.PolicyId, metadata.AgeSource.PolicyRevision,
            metadata.Binding.Jurisdiction, TimeZoneInfo.Utc, ModerationLeapDayAnniversary.March1);
        var outcome = policy.EvaluateDeclaration(matches ? pii.ReadDeclaredBirthDate() : null,
            metadata.AgeSource.DebateMinimumAge, now);
        var kind = outcome == ModerationDeclaredAgeOutcome.Unknown
            ? ModerationAgeEvidenceKind.Unknown : ModerationAgeEvidenceKind.Declared;
        var evidence = ModerationAgeEvidence.Record(Guid.CreateVersion7(), metadata.Binding,
            metadata.AgeSource.DebateMinimumAge, kind, outcome, null, now, until);
        var evaluated = evidence.EvaluateMetadata(metadata.Binding, null, now);
        return new(evaluated.Kind, evaluated.Outcome, evaluated.MinimumAge, pii.ReadBirthDateRevision(), now, until);
    }
}
