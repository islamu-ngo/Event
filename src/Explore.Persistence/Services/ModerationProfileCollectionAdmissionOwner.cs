using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Before-body existing-Human ProfileCompletion admission on the actual colocated privacy
/// transaction. Neither client nominations nor persisted consent snapshots supply authority.
/// Unsupported prerequisite owners and other transaction topologies remain unavailable.
/// </summary>
public sealed partial class ModerationProfileCollectionAdmissionOwner(
    ExploreDbContext context, IAdminContext admin, IMachinePrincipalAccessor machine,
    IHttpContextAccessor http, IPrivacyIdentityFenceAuthority privacy,
    ModerationCapabilityRegistry inventory, TimeProvider clock) : IModerationProfileCollectionAdmissionOwner
{
    private CurrentAdmission? current;

    public async Task<bool> ExecuteAsync(ModerationProfileSubjectConsentAssessment assessment, Guid? consentId,
        Func<ModerationProfileCollectionAdmission, CancellationToken, Task> consume,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(consume);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is not null || context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Profile collection requires its own clean native privacy owner.");
        if (assessment.Original is not { } original || original.ProfileId == Guid.Empty
            || !Enum.IsDefined(original.Mode) || machine.IsMachineCaller
            || http.HttpContext?.User.Identity?.IsAuthenticated != true)
            return false;
        var originalTrust = context.ModerationTrustDomainId;
        ModerationProfileCollectionIndividualAcquisition? acquisition = null;
        ModerationProfileCollectionDeadline? deadline = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        CancellationTokenRegistration expiry = default;
        var entered = 0;
        try
        {
            return await privacy.ExecuteEnrollmentSerializedAsync(async token =>
            {
                // A body stream or external consumer must never be replayed by an owner retry.
                if (Interlocked.Exchange(ref entered, 1) != 0)
                    throw new AdmissionUnavailable();
                context.ChangeTracker.Clear();
                acquisition = await ModerationProfileCollectionIndividualAcquisition.AcquireAsync(
                    context, admin, machine, http, privacy, inventory, clock, original.ProfileId, original.Mode, token);
                if (acquisition is null) throw new AdmissionUnavailable();
                var metadata = await ReadAsync(acquisition, assessment, consentId, token)
                    ?? throw new AdmissionUnavailable();
                var now = clock.GetUtcNow();
                var until = metadata.Prerequisites.Select(item => item.Reference.ValidUntil)
                    .Append(metadata.Binding.ValidUntil).Append(metadata.Assessment.Reference.ValidUntil).Min();
                if (until <= now) throw new AdmissionUnavailable();
                ModerationCollectionReceipt receipt;
                try
                {
                    receipt = ModerationCollectionReceipt.Record(Guid.CreateVersion7(), metadata.Binding,
                        metadata.Assessment, metadata.Prerequisites, now, until);
                }
                catch (ArgumentException) { throw new AdmissionUnavailable(); }
                current = new(acquisition, assessment, consentId, receipt);
                deadline = new(clock, until);
                expiry = deadline.Token.Register(linked.Cancel);
                await RequireCurrentAsync(acquisition.SubjectId, linked.Token);
                await consume(new(acquisition.SubjectId, until), linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                if (!await IsCurrentAsync(token)) throw new AdmissionUnavailable();
                return true;
            }, async token =>
            {
                if (!await IsCurrentAsync(token)) throw new AdmissionUnavailable();
                token.ThrowIfCancellationRequested();
            }, linked.Token);
        }
        catch (AdmissionUnavailable) { return false; }
        catch (DbUpdateConcurrencyException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested
            && deadline?.Token.IsCancellationRequested == true)
        { return false; }
        finally
        {
            expiry.Dispose();
            deadline?.Dispose();
            current = null;
            acquisition?.Dispose();
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = originalTrust;
        }
    }

    public async Task RequireCurrentAsync(Guid subjectId, CancellationToken cancellationToken = default)
    {
        if (current is null || subjectId != current.Acquisition.SubjectId || !await IsCurrentAsync(cancellationToken))
            throw new AuthorizationException("Current native profile collection admission is required.");
    }

    private async Task<bool> IsCurrentAsync(CancellationToken token)
    {
        if (current is not { } retained || !retained.Acquisition.OwnsContext(context)) return false;
        var metadata = await ReadAsync(retained.Acquisition, retained.Expected, retained.ConsentId, token);
        return metadata is not null && retained.Receipt.MatchesCurrentMetadata(
            metadata.Binding, metadata.Assessment, metadata.Prerequisites, clock.GetUtcNow());
    }

    private async Task<Metadata?> ReadAsync(ModerationProfileCollectionIndividualAcquisition acquisition,
        ModerationProfileSubjectConsentAssessment expected, Guid? consentId, CancellationToken token)
    {
        var observations = new ModerationProfileCollectionSubjectObservationOwner(
            context, admin, machine, http, privacy, inventory, clock);
        var observation = await observations.ReadCurrentAsync(acquisition, expected.Original.ObservationId, token);
        if (observation is null || observation.SubjectHumanId != acquisition.SubjectId) return null;
        var assessments = new ModerationProfileCollectionAssessmentRepository(
            context, admin, machine, http, privacy, clock, inventory);
        var assessment = await assessments.ReadCurrentAsync(acquisition, expected, token);
        if (assessment is null || assessment.Source.SubjectHumanId != acquisition.SubjectId) return null;
        var parents = await acquisition.ReadAssessmentParentsAsync(observation, token);
        if (parents is null) return null;
        try
        {
            var now = clock.GetUtcNow();
            var binding = observation.ToBinding();
            var applicability = assessment.ToAssessment(assessment.Source, parents.ProcedureSource,
                parents.ProcedureAdoption, parents.AgeSource, parents.AgeAdoption, binding, now);
            // Never filter unsupported kinds out of the independently assessed complete set.
            if (applicability.Required.Any(kind => kind != ModerationCollectionPrerequisiteKind.SubjectConsent))
                return null;
            var prerequisites = new List<ModerationCollectionPrerequisite>();
            if (applicability.Required.Contains(ModerationCollectionPrerequisiteKind.SubjectConsent))
            {
                if (consentId is not { } id) return null;
                var consents = new ModerationProfileSubjectConsentOwner(context, admin, machine, http, privacy, inventory, clock);
                var consent = await consents.ReadCurrentForCollectionAsync(
                    acquisition, observation, assessment, expected, id, token);
                if (consent is null) return null;
                prerequisites.Add(consent.ToPrerequisite(parents.ProcedureSource, parents.ProcedureAdoption,
                    parents.AgeSource, parents.AgeAdoption, binding, applicability, clock.GetUtcNow()));
            }
            else if (consentId is not null) return null;
            return await acquisition.RevalidateAsync(token)
                ? new(binding, applicability, prerequisites.ToArray(), parents.AgeSource) : null;
        }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private sealed record Metadata(ModerationIndividualBinding Binding,
        ModerationCollectionPrerequisiteAssessment Assessment, ModerationCollectionPrerequisite[] Prerequisites,
        ModerationChildAgePolicySource AgeSource)
    {
        public override string ToString() => nameof(Metadata);
    }

    private sealed record CurrentAdmission(ModerationProfileCollectionIndividualAcquisition Acquisition,
        ModerationProfileSubjectConsentAssessment Expected, Guid? ConsentId, ModerationCollectionReceipt Receipt)
    {
        public override string ToString() => nameof(CurrentAdmission);
    }

    private sealed class AdmissionUnavailable : Exception;
}
