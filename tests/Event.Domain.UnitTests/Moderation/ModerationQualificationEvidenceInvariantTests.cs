using System.Reflection;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationQualificationEvidenceInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Id = Guid.Parse("019a1234-5678-7001-8000-000000000001");
    private static readonly Guid CaseId = Guid.Parse("019a1234-5678-7002-8000-000000000002");
    private static readonly Guid TrustId = Guid.Parse("019a1234-5678-7003-8000-000000000003");
    private static readonly Guid TenantId = Guid.Parse("019a1234-5678-7004-8000-000000000004");
    private static readonly Guid Subject = Guid.Parse("019a1234-5678-7005-8000-000000000005");
    private static readonly Guid Reviewer = Guid.Parse("019a1234-5678-7006-8000-000000000006");
    private static readonly Guid Controller = Guid.Parse("019a1234-5678-7007-8000-000000000007");
    private static readonly Guid Assessor = Guid.Parse("019a1234-5678-7008-8000-000000000008");
    private static readonly Guid Binding = Guid.Parse("019a1234-5678-7009-8000-000000000009");
    private const string PrivateText = "Private case-specific factual explanation.";
    private const string Digest = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Test]
    public async Task ResponseRetainsActualSubjectAuthorshipWithoutQualificationFields()
    {
        var evidence = Response();
        evidence.ValidatePersistedState();
        await Assert.That(evidence.SubjectHumanId).IsEqualTo((Guid?)Subject);
        await Assert.That(evidence.AssertedByHumanId).IsEqualTo(Subject);
        await Assert.That(evidence.ReviewerHumanId).IsNull();
        await Assert.That(evidence.ControllerHumanId).IsNull();
        await Assert.That(evidence.ConflictState).IsNull();
        await Assert.That(evidence.ValidUntil).IsNull();
        await Assert.That(evidence.Revision).IsEqualTo(1L);
        await Assert.That(() => ModerationQualificationEvidence.RecordResponse(
            Id, CaseId, TrustId, TenantId, Subject, Assessor, PrivateText, Now)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task HydrationPreservesExactScalarSnapshotAndValidationDoesNotMutate(int purpose)
    {
        var source = Evidence(purpose);
        var row = Hydrate(source);
        var before = Values(row);
        row.ValidatePersistedState();
        row.ValidatePersistedState();
        await Assert.That(Values(row).SequenceEqual(before)).IsTrue();
        await Assert.That(Values(row).SequenceEqual(Values(source))).IsTrue();
        await Assert.That(row.Id).IsEqualTo(Id);
        await Assert.That(row.CaseId).IsEqualTo(CaseId);
        await Assert.That(row.TrustDomainId).IsEqualTo(TrustId);
        await Assert.That(row.TenantId).IsEqualTo(TenantId);
        await Assert.That(row.RecordedAt).IsEqualTo(Now);
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Justification).IsEqualTo(PrivateText);
        await Assert.That(row.ToString().Contains(PrivateText, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ReviewerAndControllerCannotAssessTheirOwnQualification(int purpose)
    {
        foreach (var actor in new[] { Reviewer, Controller, Guid.Empty })
        {
            await Assert.That(() => Evidence(purpose, actor)).Throws<ArgumentException>();
        }
        var row = Evidence(purpose);
        await Assert.That(row.ReviewerHumanId).IsEqualTo((Guid?)Reviewer);
        await Assert.That(row.ControllerHumanId).IsEqualTo((Guid?)Controller);
        await Assert.That(row.AssertedByHumanId).IsEqualTo(Assessor);
    }

    [Test]
    public async Task UnknownSubjectUrgencyIsAllowedButKnownSubjectCannotAssess()
    {
        var nonhuman = ModerationQualificationEvidence.QualifyUrgency(
            Id, CaseId, TrustId, TenantId, null, Assessor, PrivateText, Now.AddHours(1), Now);
        nonhuman.ValidatePersistedState();
        await Assert.That(nonhuman.SubjectHumanId).IsNull();
        await Assert.That(() => ModerationQualificationEvidence.QualifyUrgency(
            Id, CaseId, TrustId, TenantId, Subject, Subject, PrivateText, Now.AddHours(1), Now))
            .Throws<ArgumentException>();
        await Assert.That(() => ModerationQualificationEvidence.QualifyUrgency(
            Id, CaseId, TrustId, TenantId, Guid.Empty, Assessor, PrivateText, Now.AddHours(1), Now))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(4)]
    [Arguments(-1)]
    public async Task UnresolvedAndUnknownConflictOrdinalsCannotAttest(int state)
    {
        await Assert.That(() => Conflict(state)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    public async Task AcceptedConflictOrdinalsRetainTheirMeaning(int state)
    {
        var evidence = Conflict(state);
        await Assert.That(evidence.ConflictState).IsEqualTo((int?)state);
        await Assert.That(evidence.Purpose).IsEqualTo(ModerationQualificationEvidencePurpose.Conflict);
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task AttestationRequiresStrictlyFutureFiniteExpiry(int purpose)
    {
        foreach (var until in new[] { Now.AddTicks(-1), Now, DateTimeOffset.MaxValue })
        {
            await Assert.That(() => Evidence(purpose, until: until)).Throws<ArgumentException>();
        }
        var evidence = Evidence(purpose, until: Now.AddTicks(1));
        await Assert.That(evidence.ValidUntil).IsEqualTo((DateTimeOffset?)Now.AddTicks(1));
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task EmptyPunctuationOnlyAndOversizeTextCannotBecomeEvidence(int purpose)
    {
        foreach (var text in new[] { "", " \r\n\t ", "...", new string('x', 4001) })
        {
            await Assert.That(() => Evidence(purpose, text: text)).Throws<ArgumentException>();
        }
        var bounded = Evidence(purpose, text: new string('x', 4000));
        await Assert.That(bounded.Justification.Length).IsEqualTo(4000);
        await Assert.That(Evidence(purpose, text: "  factual explanation  ").Justification)
            .IsEqualTo("factual explanation");
    }

    [Test]
    public async Task HydrationRejectsPurposeFieldSmugglingAndMissingFields()
    {
        foreach (var (purpose, property, value) in new (int, string, object?)[]
        {
            (1, "SubjectHumanId", null), (1, "AssertedByHumanId", Assessor),
            (1, "ReviewerHumanId", Reviewer), (1, "ControllerHumanId", Controller),
            (1, "ConflictState", 2), (1, "ValidUntil", Now.AddHours(1)),
            (2, "ReviewerHumanId", null), (2, "ControllerHumanId", null),
            (2, "SubjectHumanId", Subject), (2, "ConflictState", 2),
            (3, "ReviewerHumanId", null), (3, "ControllerHumanId", null),
            (3, "SubjectHumanId", Subject), (3, "ConflictState", null), (3, "ConflictState", 1),
            (4, "ReviewerHumanId", Reviewer), (4, "ControllerHumanId", Controller),
            (4, "ConflictState", 3), (4, "AssertedByHumanId", Subject)
        })
        {
            var row = Hydrate(Evidence(purpose));
            Set(row, property, value);
            await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task HydrationRejectsInvalidIdentityRevisionTextAndRecordedInstant(int purpose)
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("Id", Guid.Empty), ("Id", Guid.Parse("12345678-1234-4234-8234-123456789abc")),
            ("CaseId", Guid.Empty), ("TrustDomainId", Guid.Empty), ("TenantId", Guid.Empty),
            ("AssertedByHumanId", Guid.Empty), ("Purpose", (ModerationQualificationEvidencePurpose)99),
            ("Revision", 0L), ("Revision", 2L), ("Justification", null),
            ("Justification", " "), ("Justification", new string('x', 4001)),
            ("RecordedAt", DateTimeOffset.MinValue), ("RecordedAt", DateTimeOffset.MaxValue),
            ("RecordedAt", Now.ToOffset(TimeSpan.FromHours(1)))
        })
        {
            var row = Hydrate(Evidence(purpose));
            Set(row, property, value);
            await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task HydratedExpiryCannotBeMissingBackdatedOrOpenEnded(int purpose)
    {
        foreach (var until in new DateTimeOffset?[]
        {
            null, Now, Now.AddTicks(-1), DateTimeOffset.MaxValue,
            Now.AddHours(1).ToOffset(TimeSpan.FromHours(1))
        })
        {
            var row = Hydrate(Evidence(purpose));
            Set(row, "ValidUntil", until);
            await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    [Arguments(ModerationQualificationSourceKind.Case)]
    [Arguments(ModerationQualificationSourceKind.Evidence)]
    [Arguments(ModerationQualificationSourceKind.Reviewer)]
    [Arguments(ModerationQualificationSourceKind.Finding)]
    [Arguments(ModerationQualificationSourceKind.Policy)]
    [Arguments(ModerationQualificationSourceKind.QualificationPolicy)]
    public async Task ReceiptRetainsExactMutationCoordinatesWithoutPrivateText(ModerationQualificationSourceKind kind)
    {
        var source = Receipt(kind);
        var row = Hydrate(source);
        row.ValidatePersistedState();
        await Assert.That(Values(row).SequenceEqual(Values(source))).IsTrue();
        await Assert.That(row.Id).IsEqualTo(Id);
        await Assert.That(row.RequestDigest).IsEqualTo(Digest);
        await Assert.That(row.HostBindingId).IsEqualTo(Binding);
        await Assert.That(row.TrustDomainId).IsEqualTo(TrustId);
        await Assert.That(row.TenantId).IsEqualTo(TenantId);
        await Assert.That(row.SourceKind).IsEqualTo(kind);
        await Assert.That(row.SourceId).IsEqualTo(CaseId);
        await Assert.That(row.PrincipalId).IsEqualTo(Assessor);
        await Assert.That(row.ResultRevision).IsEqualTo(2L);
        await Assert.That(row.QualificationRevision).IsEqualTo(3L);
        await Assert.That(row.CompletedAt).IsEqualTo(Now);
        await Assert.That(row.ToString().Contains(Digest, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReceiptHydrationRejectsInvalidCoordinatesDigestsRevisionsAndTimestamps()
    {
        foreach (var name in new[] { "Id", "HostBindingId", "TrustDomainId", "TenantId", "SourceId" })
        {
            foreach (var value in new[] { Guid.Empty, Guid.Parse("12345678-1234-4234-8234-123456789abc") })
            {
                var row = Hydrate(Receipt());
                Set(row, name, value);
                await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
            }
        }
        foreach (var (property, value) in new (string, object?)[]
        {
            ("PrincipalId", Guid.Empty), ("SourceKind", (ModerationQualificationSourceKind)0),
            ("SourceKind", (ModerationQualificationSourceKind)(-1)),
            ("SourceKind", (ModerationQualificationSourceKind)int.MaxValue), ("RequestDigest", null),
            ("RequestDigest", new string('a', 63)), ("RequestDigest", new string('a', 65)),
            ("RequestDigest", new string('g', 64)), ("ResultRevision", 0L),
            ("QualificationRevision", -1L), ("CompletedAt", DateTimeOffset.MinValue),
            ("CompletedAt", DateTimeOffset.MaxValue), ("CompletedAt", Now.ToOffset(TimeSpan.FromHours(1)))
        })
        {
            var row = Hydrate(Receipt());
            Set(row, property, value);
            await Assert.That(() => row.ValidatePersistedState()).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ReceiptFactoryRejectsInvalidOutcomeBeforePublishing()
    {
        await Assert.That(() => ModerationQualificationMutationReceipt.Complete(
            Id, PrivateText, Binding, TrustId, TenantId, ModerationQualificationSourceKind.Case,
            CaseId, Assessor, 2, 3, Now)).Throws<ArgumentException>();
        await Assert.That(() => ModerationQualificationMutationReceipt.Complete(
            Id, Digest, Binding, TrustId, TenantId, ModerationQualificationSourceKind.Case,
            CaseId, Assessor, 0, 3, Now)).Throws<ArgumentException>();
        await Assert.That(() => ModerationQualificationMutationReceipt.Complete(
            Id, Digest, Binding, TrustId, TenantId, ModerationQualificationSourceKind.Case,
            CaseId, Assessor, 2, 0, Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task FactoriesNormalizeInstantsWithoutExtendingValidity()
    {
        var offsetNow = Now.ToOffset(TimeSpan.FromHours(3));
        var evidence = ModerationQualificationEvidence.QualifyUrgency(Id, CaseId, TrustId,
            TenantId, Subject, Assessor, PrivateText, offsetNow.AddHours(1), offsetNow);
        await Assert.That(evidence.RecordedAt.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(evidence.ValidUntil).IsEqualTo((DateTimeOffset?)Now.AddHours(1));
        var receipt = ModerationQualificationMutationReceipt.Complete(Id, Digest, Binding,
            TrustId, TenantId, ModerationQualificationSourceKind.Evidence, Id, Assessor, 1, 2, offsetNow);
        await Assert.That(receipt.CompletedAt.Offset).IsEqualTo(TimeSpan.Zero);
    }

    private static ModerationQualificationEvidence Response() => Evidence(1);

    private static ModerationQualificationEvidence Conflict(int state) =>
        ModerationQualificationEvidence.AssessConflict(Id, CaseId, TrustId, TenantId,
            Reviewer, Controller, Assessor, state, PrivateText, Now.AddHours(1), Now);

    private static ModerationQualificationEvidence Evidence(
        int purpose, Guid? assessor = null, DateTimeOffset? until = null, string text = PrivateText) => purpose switch
        {
            1 => ModerationQualificationEvidence.RecordResponse(
                Id, CaseId, TrustId, TenantId, Subject, Subject, text, Now),
            2 => ModerationQualificationEvidence.QualifyController(Id, CaseId, TrustId, TenantId,
                Reviewer, Controller, assessor ?? Assessor, text, until ?? Now.AddHours(1), Now),
            3 => ModerationQualificationEvidence.AssessConflict(Id, CaseId, TrustId, TenantId,
                Reviewer, Controller, assessor ?? Assessor, 2, text, until ?? Now.AddHours(1), Now),
            4 => ModerationQualificationEvidence.QualifyUrgency(Id, CaseId, TrustId, TenantId,
                Subject, assessor ?? Assessor, text, until ?? Now.AddHours(1), Now),
            _ => throw new ArgumentOutOfRangeException(nameof(purpose))
        };

    private static ModerationQualificationMutationReceipt Receipt(
        ModerationQualificationSourceKind kind = ModerationQualificationSourceKind.Case) =>
        ModerationQualificationMutationReceipt.Complete(Id, Digest, Binding, TrustId,
            TenantId, kind, CaseId, Assessor, 2, 3, Now);

    private static object?[] Values(object row) =>
        row.GetType().GetProperties().Select(property => property.GetValue(row)).ToArray();

    private static T Hydrate<T>(T source) where T : class
    {
        var constructor = typeof(T).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new InvalidOperationException("Missing private hydration constructor.");
        var row = (T)constructor.Invoke(null);
        foreach (var property in typeof(T).GetProperties())
        {
            Set(row, property.Name, property.GetValue(source));
        }
        return row;
    }

    private static void Set(object row, string name, object? value)
    {
        var property = row.GetType().GetProperty(name)
            ?? throw new InvalidOperationException($"Missing scalar {name}.");
        if (property.SetMethod is not { IsPrivate: true })
        {
            throw new InvalidOperationException($"{name} requires a private setter.");
        }
        property.SetValue(row, value);
    }
}
