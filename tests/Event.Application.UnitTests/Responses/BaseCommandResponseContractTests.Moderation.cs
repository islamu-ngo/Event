using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace ApplicationUnitTests.Responses;

public sealed partial class BaseCommandResponseContractTests
{
    private static NativeFactoryScenario[] CreateModerationFactoryScenarios() =>
    [
        new(typeof(ModerationAllegationResponse), AssertAllegationFactories),
        new(typeof(ModerationAuthoritySourcesResponse), AssertAuthoritySourceFactories),
        new(typeof(ModerationMachineSubjectResponse), AssertMachineSubjectFactories),
        new(typeof(ModerationLifecycleResponse), AssertLifecycleFactories),
        new(typeof(ModerationQualificationResponse), AssertQualificationFactories),
        new(typeof(ModerationResourceSafetyResponse), AssertResourceSafetyFactories),
        new(typeof(ModerationPolicyPublicationCandidateResponse), AssertPublicationCandidateFactories),
        new(typeof(ModerationPolicyPublicationResponse), AssertPublicationFactories),
        new(typeof(ModerationReadReceiptResponse), AssertReadReceiptFactories),
        new(typeof(ModerationSubmissionReceiptResponse), AssertSubmissionReceiptFactories),
    ];

    [Test]
    public async Task EveryNativeOutcomeResponseExecutesItsTypedFactoryContract()
    {
        foreach (NativeFactoryScenario scenario in CreateModerationFactoryScenarios())
            await scenario.Verify();
    }

    private static async Task AssertAllegationFactories()
    {
        ModerationCaseIntakeAcknowledgement acknowledgement = CreateIntakeAcknowledgement();
        ModerationAllegationResponse success = ModerationAllegationResponse.FromOutcome(
            ResultId, new(ModerationCaseIntakeCommitOutcome.Accepted, acknowledgement));
        await AssertNativeSuccess(success, acknowledgement.Id, ("AcceptedAt", FixtureOffset));

        (ModerationCaseIntakeCommitOutcome Outcome, string Code)[] failures =
        [
            (ModerationCaseIntakeCommitOutcome.Conflict, FailureCodes.ConcurrencyConflict),
            (ModerationCaseIntakeCommitOutcome.Unavailable, "ModerationCaseIntakeUnavailable"),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Outcome)
            .Append(ModerationCaseIntakeCommitOutcome.Accepted));
        foreach (var (outcome, code) in failures)
        {
            // Even an extraneous genuine acknowledgement must not escape a failed outcome.
            await AssertNativeFailure(ModerationAllegationResponse.FromOutcome(ResultId, new(outcome, acknowledgement)),
                code, outcome == ModerationCaseIntakeCommitOutcome.Conflict ? ResultId : Guid.Empty);
        }
        await AssertNativeFailure(ModerationAllegationResponse.FromOutcome(ResultId,
            new(ModerationCaseIntakeCommitOutcome.Accepted)), "ModerationCaseIntakeUnavailable");
        await AssertNativeFailure(ModerationAllegationResponse.FromOutcome(ResultId,
            new((ModerationCaseIntakeCommitOutcome)int.MaxValue, acknowledgement)), "ModerationCaseIntakeUnavailable");
        await AssertNativeInvalid(ModerationAllegationResponse.Invalid);
    }

    private static async Task AssertAuthoritySourceFactories()
    {
        ModerationHostBinding binding = CreateHostBinding();
        ModerationQualificationEpoch epoch = ModerationQualificationEpoch.Initialize(ChoiceId, binding, new Tenant
        {
            Id = TenantId,
            FullName = "Fixture tenant",
            Slug = "fixture-tenant",
            TenantStatusId = 1,
            TenantStatus = new TenantStatus { Id = 1, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true },
        });
        ModerationAuthoritySourceResult established = ModerationAuthoritySourceResult.Established(binding, epoch);
        ModerationAuthoritySourcesResponse success = ModerationAuthoritySourcesResponse.FromOutcome(RefundId, established);
        await AssertNativeSuccess(success, binding.Id, ("EpochId", ChoiceId), ("Revision", 1L));

        (ModerationAuthoritySourceResult Result, string Code)[] failures =
        [
            (ModerationAuthoritySourceResult.Unavailable(), "ModerationAuthorityUnavailable"),
            (ModerationAuthoritySourceResult.Unauthenticated(), FailureCodes.AuthenticationRequired),
            (ModerationAuthoritySourceResult.Denied(), FailureCodes.AdminRequired),
            (ModerationAuthoritySourceResult.Conflict(), FailureCodes.ConcurrencyConflict),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Result.Outcome).Append(established.Outcome));
        foreach (var (result, code) in failures)
            await AssertNativeFailure(ModerationAuthoritySourcesResponse.FromOutcome(RefundId, result), code,
                result.Outcome == ModerationAuthoritySourceOutcome.Conflict ? RefundId : Guid.Empty);
        await AssertNativeInvalid(ModerationAuthoritySourcesResponse.Invalid);
    }

    private static async Task AssertMachineSubjectFactories()
    {
        ModerationMachineSubjectBinding binding = ModerationMachineSubjectBinding.Establish(
            ChoiceId, ResultId, ModerationMachineCredentialSource.Persisted, SyntheticKeyId,
            TenantId, ExternalApiKeyOwnerType.Organization, RelatedId, RefundId);
        ModerationMachineSubjectBindingResult established = ModerationMachineSubjectBindingResult.Established(binding);
        ModerationMachineSubjectResponse success = ModerationMachineSubjectResponse.FromOutcome(ResultId, established);
        await AssertNativeSuccess(success, ChoiceId, ("ServicePrincipalId", RefundId), ("Revision", 1L));

        (ModerationMachineSubjectBindingResult Result, string Code)[] failures =
        [
            (ModerationMachineSubjectBindingResult.Unavailable(), "ModerationAuthorityUnavailable"),
            (ModerationMachineSubjectBindingResult.Unauthenticated(), FailureCodes.AuthenticationRequired),
            (ModerationMachineSubjectBindingResult.Denied(), FailureCodes.AdminRequired),
            (ModerationMachineSubjectBindingResult.Conflict(), FailureCodes.ConcurrencyConflict),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Result.Outcome).Append(established.Outcome));
        foreach (var (result, code) in failures)
            await AssertNativeFailure(ModerationMachineSubjectResponse.FromOutcome(ResultId, result), code,
                result.Outcome == ModerationAuthoritySourceOutcome.Conflict ? ResultId : Guid.Empty);
        await AssertNativeInvalid(ModerationMachineSubjectResponse.Invalid);
    }

    private static async Task AssertLifecycleFactories()
    {
        ModerationOperationResult applied = ModerationOperationResult.Applied(ChoiceId, RefundId, 3);
        ModerationLifecycleResponse success = ModerationLifecycleResponse.FromOutcome(ResultId, applied);
        await AssertNativeSuccess(success, ChoiceId, ("SanctionId", RefundId), ("Revision", 3L));
        await AssertNativeSuccess(ModerationLifecycleResponse.FromOutcome(ResultId,
            ModerationOperationResult.Applied(ChoiceId, null, 4)), ChoiceId, ("SanctionId", null), ("Revision", 4L));

        (ModerationOperationResult Result, string Code)[] failures =
        [
            (ModerationOperationResult.Unavailable(), "ModerationAuthorityUnavailable"),
            (ModerationOperationResult.Denied(), FailureCodes.AdminRequired),
            (ModerationOperationResult.Conflict(), FailureCodes.ConcurrencyConflict),
            (ModerationOperationResult.NotFound(), FailureCodes.NotFound),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Result.Outcome).Append(applied.Outcome));
        foreach (var (result, code) in failures)
            await AssertNativeFailure(ModerationLifecycleResponse.FromOutcome(ResultId, result), code,
                result.Outcome == ModerationOperationOutcome.Conflict ? ResultId : Guid.Empty);

        AuthorityScope scope = CreateModerationScope();
        ModerationRestrictionEvaluation evaluation = EffectiveRestrictions.Evaluate(
            SubjectReference.Create(ModerationSubjectKind.Human, RelatedId, scope.Parent!), scope,
            ModerationCapability.Publish, FixtureOffset, [], [], ModerationAuthorityState.Known);
        ModerationOperationResult evaluated = ModerationOperationResult.Evaluated(evaluation);
        await Assert.That(evaluated.Outcome).IsEqualTo(ModerationOperationOutcome.Known);
        await AssertNativeFailure(ModerationLifecycleResponse.FromOutcome(ResultId, evaluated), "ModerationAuthorityUnavailable");
        await AssertNativeInvalid(ModerationLifecycleResponse.Invalid);
    }

    private static async Task AssertQualificationFactories()
    {
        ModerationQualificationMutationReceipt receipt = ModerationQualificationMutationReceipt.Complete(
            ResultId, new string('a', 64), ChoiceId, RelatedId, TenantId,
            ModerationQualificationSourceKind.Case, RefundId, RelatedId, 3, 4, FixtureOffset);
        ModerationQualificationResponse success = ModerationQualificationResponse.FromOutcome(
            ResultId, new(ModerationQualificationWriteOutcome.Known, receipt));
        await AssertNativeSuccess(success, RefundId, ("Revision", 3L), ("QualificationRevision", 4L));

        (ModerationQualificationWriteOutcome Outcome, string Code)[] failures =
        [
            (ModerationQualificationWriteOutcome.Unauthenticated, FailureCodes.AuthenticationRequired),
            (ModerationQualificationWriteOutcome.Denied, FailureCodes.AdminRequired),
            (ModerationQualificationWriteOutcome.Conflict, FailureCodes.ConcurrencyConflict),
            (ModerationQualificationWriteOutcome.NotFound, FailureCodes.NotFound),
            (ModerationQualificationWriteOutcome.Unavailable, "ModerationAuthorityUnavailable"),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Outcome).Append(ModerationQualificationWriteOutcome.Known));
        foreach (var (outcome, code) in failures)
            await AssertNativeFailure(ModerationQualificationResponse.FromOutcome(ResultId, new(outcome, receipt)), code,
                outcome == ModerationQualificationWriteOutcome.Conflict ? ResultId : Guid.Empty);
        await AssertNativeFailure(ModerationQualificationResponse.FromOutcome(ResultId,
            new(ModerationQualificationWriteOutcome.Known)), "ModerationAuthorityUnavailable");
        await AssertNativeFailure(ModerationQualificationResponse.FromOutcome(ResultId,
            new((ModerationQualificationWriteOutcome)int.MaxValue, receipt)), "ModerationAuthorityUnavailable");
        await AssertNativeInvalid(ModerationQualificationResponse.Invalid);
    }

    private static async Task AssertResourceSafetyFactories()
    {
        ModerationResourceSafetyResponse success = ModerationResourceSafetyResponse.From(
            ModerationResourceSafetyOutcome.Known, ResultId, ChoiceId, FixtureOffset);
        await AssertNativeSuccess(success, ChoiceId, ("RecordedAt", FixtureOffset));
        await AssertNativeSuccess(ModerationResourceSafetyResponse.From(
            ModerationResourceSafetyOutcome.Known, ResultId, ChoiceId, null), ChoiceId, ("RecordedAt", null));

        (ModerationResourceSafetyOutcome Outcome, string Code)[] failures =
        [
            (ModerationResourceSafetyOutcome.Unavailable, "ModerationAuthorityUnavailable"),
            (ModerationResourceSafetyOutcome.Denied, FailureCodes.AdminRequired),
            (ModerationResourceSafetyOutcome.Conflict, FailureCodes.ConcurrencyConflict),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Outcome).Append(ModerationResourceSafetyOutcome.Known));
        foreach (var (outcome, code) in failures)
            await AssertNativeFailure(ModerationResourceSafetyResponse.From(outcome, ResultId, ChoiceId, FixtureOffset),
                code, outcome == ModerationResourceSafetyOutcome.Conflict ? ResultId : Guid.Empty);
        await AssertNativeFailure(ModerationResourceSafetyResponse.From(
            ModerationResourceSafetyOutcome.Known, ResultId, null, FixtureOffset), "ModerationAuthorityUnavailable");
        await AssertNativeFailure(ModerationResourceSafetyResponse.From(
            (ModerationResourceSafetyOutcome)int.MaxValue, ResultId, ChoiceId, FixtureOffset), "ModerationAuthorityUnavailable");
        await AssertNativeInvalid(ModerationResourceSafetyResponse.Invalid);
    }

    private static async Task AssertPublicationCandidateFactories()
    {
        ModerationPolicyPublication snapshot = CreatePublicationSnapshot();
        ModerationPolicyPublicationCandidate candidate = ModerationPolicyPublicationCandidate.Prepare(
            snapshot, RefundId, snapshot.OperationId, snapshot.RequestDigest, new string('b', 64),
            snapshot.PrincipalId, FixtureOffset, FixtureOffset.AddMinutes(5));
        ModerationPolicyPublicationCandidateResponse success = ModerationPolicyPublicationCandidateResponse.FromOutcome(
            ResultId, new(ModerationPolicyPublicationOutcome.Known, candidate));
        await AssertNativeSuccess(success, RefundId, ("Revision", 1L), ("ValidUntil", FixtureOffset.AddMinutes(5)));

        foreach (var (outcome, code) in await PublicationFailureCases())
        {
            ModerationPolicyPublicationCandidateResponse response = ModerationPolicyPublicationCandidateResponse.FromOutcome(
                ResultId, new(outcome, candidate));
            await AssertNativeFailure(response,
                outcome == ModerationPolicyPublicationOutcome.Invalid ? null : code ?? "ModerationPublicationPreparationUnavailable",
                outcome == ModerationPolicyPublicationOutcome.Conflict ? ResultId : Guid.Empty,
                outcome == ModerationPolicyPublicationOutcome.Invalid ? ["Moderation publication preparation is invalid."] : null);
        }
        await AssertNativeFailure(ModerationPolicyPublicationCandidateResponse.FromOutcome(ResultId,
            new(ModerationPolicyPublicationOutcome.Known)), "ModerationPublicationPreparationUnavailable");
        await AssertNativeFailure(ModerationPolicyPublicationCandidateResponse.FromOutcome(ResultId,
            new((ModerationPolicyPublicationOutcome)int.MaxValue, candidate)), "ModerationPublicationPreparationUnavailable");
        await AssertNativeInvalid(ModerationPolicyPublicationCandidateResponse.Invalid);
    }

    private static async Task AssertPublicationFactories()
    {
        ModerationPolicyPublication publication = CreatePublicationSnapshot(publishedActive: false);
        ModerationPolicyPublicationResponse success = ModerationPolicyPublicationResponse.FromOutcome(
            ResultId, new(ModerationPolicyPublicationOutcome.Known, publication));
        await AssertNativeSuccess(success, publication.PolicyId,
            ("PublicationId", publication.Id), ("Revision", 1L), ("PublishedActive", false));

        ModerationPolicyPublication active = CreatePublicationSnapshot(publishedActive: true);
        await AssertNativeSuccess(ModerationPolicyPublicationResponse.FromOutcome(
            ResultId, new(ModerationPolicyPublicationOutcome.Known, active)), active.PolicyId,
            ("PublicationId", active.Id), ("Revision", 1L), ("PublishedActive", true));

        foreach (var (outcome, code) in await PublicationFailureCases())
        {
            ModerationPolicyPublicationResponse response = ModerationPolicyPublicationResponse.FromOutcome(
                ResultId, new(outcome, publication));
            await AssertNativeFailure(response,
                outcome == ModerationPolicyPublicationOutcome.Invalid ? null : code ?? "ModerationPublicationUnavailable",
                outcome == ModerationPolicyPublicationOutcome.Conflict ? ResultId : Guid.Empty,
                outcome == ModerationPolicyPublicationOutcome.Invalid ? ["Moderation publication intent is invalid."] : null);
        }
        await AssertNativeFailure(ModerationPolicyPublicationResponse.FromOutcome(ResultId,
            new(ModerationPolicyPublicationOutcome.Known)), "ModerationPublicationUnavailable");
        await AssertNativeFailure(ModerationPolicyPublicationResponse.FromOutcome(ResultId,
            new((ModerationPolicyPublicationOutcome)int.MaxValue, publication)), "ModerationPublicationUnavailable");
        await AssertNativeInvalid(ModerationPolicyPublicationResponse.Invalid);
    }

    private static async Task<(ModerationPolicyPublicationOutcome Outcome, string? Code)[]> PublicationFailureCases()
    {
        (ModerationPolicyPublicationOutcome Outcome, string? Code)[] failures =
        [
            (ModerationPolicyPublicationOutcome.Invalid, null),
            (ModerationPolicyPublicationOutcome.Unauthenticated, FailureCodes.AuthenticationRequired),
            (ModerationPolicyPublicationOutcome.Denied, FailureCodes.AdminRequired),
            (ModerationPolicyPublicationOutcome.Conflict, FailureCodes.ConcurrencyConflict),
            (ModerationPolicyPublicationOutcome.Unavailable, null),
        ];
        await AssertOutcomeCoverage(failures.Select(item => item.Outcome).Append(ModerationPolicyPublicationOutcome.Known));
        return failures;
    }

    private static Task AssertReadReceiptFactories() => AssertReceiptFactories<ModerationReadReceiptResponse>(
        ModerationReadReceiptResponse.FromCommitted, response => response.PresentedToken,
        response => response.ExpiresAt, ModerationReadReceiptResponse.Invalid, ModerationCaseRelationshipKind.CaseReadGrant);

    private static Task AssertSubmissionReceiptFactories() => AssertReceiptFactories<ModerationSubmissionReceiptResponse>(
        ModerationSubmissionReceiptResponse.FromCommitted, response => response.PresentedToken,
        response => response.ExpiresAt, ModerationSubmissionReceiptResponse.Invalid, ModerationCaseRelationshipKind.EventRole);

    private static async Task AssertReceiptFactories<TResponse>(
        Func<ModerationCaseIssuedReceipt?, TResponse> fromCommitted,
        Func<TResponse, ReadOnlyMemory<byte>> readToken,
        Func<TResponse, DateTimeOffset?> readExpiry,
        Func<IEnumerable<string>, BaseCommandResponse<Guid>> invalid,
        ModerationCaseRelationshipKind relationshipKind) where TResponse : BaseCommandResponse<Guid>
    {
        byte[] token = RandomNumberGenerator.GetBytes(32);
        byte[] expected = token.ToArray();
        ModerationCaseTargetReceipt receipt = CreateTargetReceipt(token, relationshipKind);
        TResponse response = fromCommitted(new(receipt, token));
        await AssertNativeSuccess(response, receipt.Id,
            ("PresentedToken", new ReadOnlyMemory<byte>(expected)), ("ExpiresAt", FixtureOffset.AddMinutes(5)));
        CryptographicOperations.ZeroMemory(token);
        await Assert.That(readToken(response).Span.SequenceEqual(expected)).IsTrue();
        if (MemoryMarshal.TryGetArray(readToken(response), out ArraySegment<byte> exposed))
            Array.Clear(exposed.Array!, exposed.Offset, exposed.Count);
        await Assert.That(readToken(response).Span.SequenceEqual(expected)).IsTrue();
        await Assert.That(readExpiry(response)).IsEqualTo(FixtureOffset.AddMinutes(5));
        await Assert.That(response.ToString()).DoesNotContain(Convert.ToBase64String(expected));
        await AssertNativeFailure(fromCommitted(null), "ModerationCaseIntakeUnavailable");
        await AssertNativeInvalid(invalid);
    }

    private static async Task AssertNativeSuccess(
        BaseCommandResponse<Guid> response, Guid id, params (string Name, object? Value)[] payload)
    {
        await Assert.That(response.IsSuccess).IsTrue().Because(response.GetType().Name);
        await Assert.That(response.Id).IsEqualTo(id);
        await Assert.That(response.Message).IsNull();
        await Assert.That(response.FailureCode).IsNull();
        await Assert.That(response.Errors).IsNull();
        await Assert.That(response.QuotaExceeded).IsNull();
        await Assert.That(ResponsePayloadProperties(response.GetType(), includeJsonIgnored: true).Select(property => property.Name))
            .IsEquivalentTo(payload.Select(item => item.Name));
        foreach (var (name, expected) in payload)
        {
            object? actual = response.GetType().GetProperty(name)!.GetValue(response);
            if (expected is ReadOnlyMemory<byte> bytes)
                await Assert.That(((ReadOnlyMemory<byte>)actual!).Span.SequenceEqual(bytes.Span)).IsTrue();
            else
                await Assert.That(actual).IsEqualTo(expected);
        }
    }

    private static async Task AssertNativeFailure(
        BaseCommandResponse<Guid> response, string? code, Guid id = default, IReadOnlyList<string>? errors = null)
    {
        await Assert.That(response.IsSuccess).IsFalse().Because(response.GetType().Name);
        await Assert.That(response.Id).IsEqualTo(id);
        await Assert.That(response.FailureCode).IsEqualTo(code);
        await Assert.That(response.QuotaExceeded).IsNull();
        await Assert.That(response.Message).IsEqualTo(errors?[0]);
        if (errors is null)
            await Assert.That(response.Errors).IsNull();
        else
            await Assert.That(response.Errors!).IsEquivalentTo(errors);
        foreach (var property in ResponsePayloadProperties(response.GetType(), includeJsonIgnored: true))
        {
            object? value = property.GetValue(response);
            if (value is ReadOnlyMemory<byte> bytes)
                await Assert.That(bytes.IsEmpty).IsTrue();
            else
                await Assert.That(value).IsEqualTo(SafeDefault(property.PropertyType));
        }
    }

    private static async Task AssertNativeInvalid(Func<IEnumerable<string>, BaseCommandResponse<Guid>> invalid)
    {
        var errors = new List<string>(FeatureErrors);
        BaseCommandResponse<Guid> response = invalid(errors);
        errors.Clear();
        await AssertNativeFailure(response, null, errors: FeatureErrors);
        if (response.Errors is IList<string> mutable)
            await Assert.That(() => mutable.Add("changed")).Throws<NotSupportedException>();
        await Assert.That(() => invalid(null!)).Throws<ArgumentNullException>();
        await Assert.That(() => invalid([])).Throws<ArgumentException>();
        await Assert.That(() => invalid(BlankValidationErrors)).Throws<ArgumentException>();
    }

    private static async Task AssertOutcomeCoverage<TOutcome>(IEnumerable<TOutcome> covered) where TOutcome : struct, Enum =>
        await Assert.That(covered).IsEquivalentTo(Enum.GetValues<TOutcome>());

    private static BaseCommandResponse<Guid>[] CreateEnvelopeFailures() =>
    [
        BaseCommandResponse.Validation<Guid>(OneValidationError, id: ResultId),
        BaseCommandResponse.NotFound<Guid>(id: ResultId),
        BaseCommandResponse.Conflict(ResultId),
        BaseCommandResponse.Authentication<Guid>(),
        BaseCommandResponse.Authorization<Guid>(),
        BaseCommandResponse.Quota("quota.blocked", CreateQuota(), id: ResultId),
        BaseCommandResponse.Failure("fixture.unavailable", errors: FeatureErrors, id: ResultId),
    ];

    private static ModerationHostBinding CreateHostBinding() =>
        ModerationHostBinding.Establish(ResultId, RelatedId, ChoiceId, RefundId, Guid.CreateVersion7());

    private static AuthorityScope CreateModerationScope() => AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(RelatedId), ChoiceId), RefundId), TenantId);

    private static ModerationCaseTargetReceipt CreateTargetReceipt(
        byte[] token, ModerationCaseRelationshipKind relationshipKind = ModerationCaseRelationshipKind.EventRole)
    {
        AuthorityScope scope = CreateModerationScope();
        ModerationEvidenceReference evidence = ModerationEvidenceReference.Create(
            ModerationCaseTarget.Create(ModerationCaseTargetKind.LocalIncident, Guid.CreateVersion7(), scope, Guid.CreateVersion7()),
            scope, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        return ModerationCaseTargetReceipt.Issue(ChoiceId, scope, RelatedId, ResultId, RefundId, 1,
            Guid.CreateVersion7(), relationshipKind, ModerationCaseRelationshipGeneration.FromRevision(1),
            Guid.CreateVersion7(), 1, evidence, SHA256.HashData(token), FixtureOffset, FixtureOffset.AddMinutes(5));
    }

    private static ModerationCaseIntakeAcknowledgement CreateIntakeAcknowledgement()
    {
        byte[] token = RandomNumberGenerator.GetBytes(32);
        ModerationCaseTargetReceipt receipt = CreateTargetReceipt(token);
        AuthorityScope scope = CreateModerationScope();
        ModerationCase allegation = ModerationCase.OpenAllegation(
            Guid.CreateVersion7(), scope, receipt.Evidence, FixtureOffset);
        receipt.Consume(TenantId, RelatedId, ResultId, RefundId, 1, receipt.RelationshipSourceId,
            receipt.RelationshipGeneration, receipt.Evidence, SHA256.HashData(token), allegation.Id, ResultId, 1, FixtureOffset);
        ModerationOperationalBudgetSource budget = ModerationOperationalBudgetSource.Record(
            Guid.CreateVersion7(), ResultId, RelatedId, TenantId,
            new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1)),
            ChoiceId, RefundId, 2, FixtureOffset.AddMinutes(-5), FixtureOffset.AddHours(2));
        ModerationOperationalBudgetAdoption adoption = ModerationOperationalBudgetAdoption.Adopt(
            Guid.CreateVersion7(), budget, ChoiceId, 3, FixtureOffset.AddMinutes(-2), FixtureOffset.AddHours(1));
        return ModerationCaseIntakeAcknowledgement.Capture(Guid.CreateVersion7(), allegation, receipt,
            HMACSHA256.HashData(token, ResultId.ToByteArray()), budget, adoption);
    }

    private static ModerationPolicyPublication CreatePublicationSnapshot(bool? publishedActive = null)
    {
        var id = Guid.CreateVersion7();
        var profileId = Guid.CreateVersion7();
        ModerationReferenceProfile reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG05);
        var response = reference.Responses[0];
        var strategies = new[] { 0 }.Concat(reference.Strategies.Order().Select(strategy => (int)strategy)).ToArray();
        var plans = new[] { ModerationReferenceCatalogue.GetBundle(reference.Bundle).Resolve(reference.ProtectionPurpose) }
            .Concat(reference.Strategies.Order().Select(ModerationReferenceCatalogue.GetStrategy))
            .Select(plan => plan with { Recognition = ModerationRecognitionMode.NoCaptureFromThisControl }).ToArray();
        return new ModerationPolicyPublication
        {
            Id = id,
            PolicyId = RelatedId,
            Revision = 1,
            AdoptedRevision = publishedActive is null ? null : 1,
            PreviousPublicationId = null,
            HostBindingId = ResultId,
            TrustDomainId = RelatedId,
            SolutionId = ChoiceId,
            InstanceId = RefundId,
            JurisdictionTenantId = TenantId,
            TenantId = TenantId,
            PrincipalId = ChoiceId,
            OperationId = ResultId,
            RequestDigest = new string('a', 64),
            DeclaredStateDigest = new string('c', 64),
            AuthoritySourceId = Guid.CreateVersion7(),
            AuthorityRevision = 3,
            QualificationRevision = 4,
            WriterGeneration = "native-moderation-publication-v1",
            CatalogueRevision = ModerationReferenceCatalogue.Revision,
            InventoryRevision = 1,
            InventoryFingerprint = new string('d', 64),
            PublishedActive = publishedActive ?? false,
            PublishedAt = FixtureOffset,
            EvidenceValidUntil = publishedActive == true ? FixtureOffset.AddHours(1) : null,
            MaximumFiniteDurationTicks = TimeSpan.FromDays(36600).Ticks,
            MaximumReviewDelayTicks = TimeSpan.FromDays(36600).Ticks,
            MinimumApprovals = 0,
            MaximumApprovals = 2,
            MinimumContestationCalendarMonths = 6,
            Classes = [new ModerationPolicyPublicationClass
            {
                PublicationId = id, Code = "class.CFG05", Revision = 1, Meaning = "Configured moderation definition.",
                Ground = reference.Ground, Basis = reference.Basis,
            }],
            Reasons = [new ModerationPolicyPublicationReason
            {
                PublicationId = id, Code = "reason.CFG05", Revision = 1, Meaning = "Configured moderation reason.",
                ClassCode = "class.CFG05", ClassRevision = 1,
            }],
            Modes = [new ModerationPolicyPublicationMode { PublicationId = id, Mode = 1 }],
            Evidence = publishedActive == true ? [new ModerationPolicyPublicationEvidence
            {
                PublicationId = id, RequirementDigest = new string('e', 64), ApprovalId = null,
                ApprovalRevision = null, SourceId = Guid.CreateVersion7(), SourceRevision = 1,
                WriterGeneration = "native-moderation-publication-v1", ObservedAt = FixtureOffset,
                ValidUntil = FixtureOffset.AddHours(1),
            }] : [],
            Profiles = [new ModerationPolicyPublicationProfile
            {
                PublicationId = id, ProfileId = profileId, RuleCode = "profile.CFG05", RuleRevision = 1,
                ReasonCode = "reason.CFG05", ReasonRevision = 1, ReferenceProfile = reference.Id,
                ReferenceRevision = reference.Revision, Grade = response.Grade, Bundle = reference.Bundle,
                ProtectionPurpose = reference.ProtectionPurpose, TimingKind = response.Timing.Kind,
                DurationTicks = response.Timing.Duration.Ticks, RequiredApprovals = response.RequiredApprovals,
                ContestationCalendarMonths = 6, Recognition = ModerationReferenceRecognition.Never, Supported = false,
                Scopes = [new ModerationPolicyPublicationScope { PublicationId = id, ProfileId = profileId, Kind = ModerationScopeKind.Tenant }],
                Subjects = [new ModerationPolicyPublicationSubject
                {
                    PublicationId = id, ProfileId = profileId, Kind = reference.SubjectKinds.Order().First(),
                }],
                Effects = reference.Effects.Select((effect, ordinal) => new ModerationPolicyPublicationEffect
                {
                    PublicationId = id, ProfileId = profileId, Ordinal = ordinal, Kind = effect.Kind,
                    Mode = effect.Mode, Target = effect.Target, Capability = effect.Capability,
                    Execution = effect.Execution, SemanticOwner = 0,
                }).ToArray(),
                Plans = plans.Select((plan, ordinal) => new ModerationPolicyPublicationPlan
                {
                    PublicationId = id, ProfileId = profileId, Strategy = strategies[ordinal],
                    Content = plan.Content, Management = plan.Management, Resources = plan.Resources,
                    NewAdmission = plan.NewAdmission, ExistingAdmission = plan.ExistingAdmission,
                    PrivateDisclosure = plan.PrivateDisclosure, Settlement = plan.Settlement, Capacity = plan.Capacity,
                    Credentials = plan.Credentials, Notifications = plan.Notifications, Recognition = plan.Recognition,
                    PendingWork = plan.PendingWork, Restoration = plan.Restoration,
                }).ToArray(),
                Consumers = [], Prerequisites = [],
            }],
        };
    }

    private sealed record NativeFactoryScenario(Type ResponseType, Func<Task> Verify);
}
