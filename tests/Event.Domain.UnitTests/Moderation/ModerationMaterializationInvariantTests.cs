using System.Reflection;
using System.Runtime.ExceptionServices;
using ISLAMU.Moderation.Domain;

namespace Event.Domain.UnitTests.Moderation;

public class ModerationMaterializationInvariantTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task NonpositiveRevisionCannotEscapeMaterialization(int kind)
    {
        var row = CreateAggregate(kind);
        Set(row, "Revision", 0L);

        await Reject(row);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ScalarHydrationPreservesIdentityAndCannotChangeStateDuringValidation(int kind)
    {
        var source = CreateAggregate(kind);
        var row = Hydrate(source);
        var before = Scalars(row).Select(property => property.GetValue(row)).ToArray();

        Validate(row);
        Validate(row);

        await Assert.That(Scalars(row).Select(property => property.GetValue(row)).SequenceEqual(before)).IsTrue();
        await Assert.That(Get<SubjectReference>(row, "Subject")).IsEqualTo(Get<SubjectReference>(source, "Subject"));
        await Assert.That(Get<AuthorityScope>(row, "Scope")).IsEqualTo(Get<AuthorityScope>(source, "Scope"));
        await Assert.That(Get<Guid>(row, "Id")).IsEqualTo(Get<Guid>(source, "Id"));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task EmptyAggregateAndSubjectIdentitiesAreRejected(int kind)
    {
        foreach (var property in new[] { "Id", "SubjectId", "SubjectOriginTrustDomainId", "ScopeTrustDomainId" })
        {
            var row = Hydrate(CreateAggregate(kind));
            Set(row, property, Guid.Empty);
            await Reject(row);
        }
    }

    [Test]
    [Arguments(ModerationScopeKind.TrustDomain)]
    [Arguments(ModerationScopeKind.Solution)]
    [Arguments(ModerationScopeKind.Instance)]
    [Arguments(ModerationScopeKind.Tenant)]
    [Arguments(ModerationScopeKind.Community)]
    [Arguments(ModerationScopeKind.Resource)]
    [Arguments(ModerationScopeKind.Event)]
    public async Task AllJurisdictionsReconstructWithCompleteAncestry(ModerationScopeKind kind)
    {
        var scope = Scope(kind);
        var source = ModerationCase.Open(Guid.CreateVersion7(), Human(scope), scope, Guid.CreateVersion7(), Now);
        var row = Hydrate(source);

        Validate(row);

        await Assert.That(row.Scope).IsEqualTo(scope);
        var subject = row.Subject ?? throw new InvalidOperationException("The fixture requires an attributed case.");
        await Assert.That(subject.Origin).IsEqualTo(scope);
    }

    [Test]
    public async Task EventDecisionApprovalsAndSanctionSurviveScalarHydrationWithoutResourceAlias()
    {
        var scope = Scope(ModerationScopeKind.Event);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Human(scope), Human(scope), scope, scope,
            ModerationRuleReference.Create(scope.Id, "event-risk", 1),
            [ModerationCapability.Participate], Now, Now.AddDays(1), null, 2, Now);
        decision.RecordResponse(1, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), 2, Now);
        var first = Human(scope);
        var second = Human(scope);
        decision.Approve(first, scope, 3, Now);
        decision.Approve(second, scope, 4, Now);
        var restored = Hydrate(decision);
        restored.ValidatePersistedState();
        await Assert.That(restored.Subject).IsEqualTo(decision.Subject);
        await Assert.That(restored.Issuer).IsEqualTo(decision.Issuer);
        await Assert.That(restored.IssuingScope).IsEqualTo(scope);
        await Assert.That(restored.Scope).IsEqualTo(scope);
        await Assert.That(restored.Approvals).IsEquivalentTo(new[] { first, second });
        var sanction = restored.Activate(Guid.CreateVersion7(), 5, Now);
        var retained = Hydrate(sanction);
        retained.ValidatePersistedState();
        await Assert.That(retained.Scope).IsEqualTo(scope);
        await Assert.That(retained.Subject).IsEqualTo(decision.Subject);
        await Assert.That(retained.ScopeResourceId).IsNull();
        await Assert.That(retained.ScopeEventId).IsEqualTo(scope.Id);
    }

    [Test]
    [Arguments(0, "TargetOriginEventId")]
    [Arguments(1, "Approval1OriginEventId")]
    [Arguments(1, "Approval2OriginEventId")]
    [Arguments(3, "ReviewerOriginEventId")]
    public async Task OrphanEventCoordinateCannotHideInsideAbsentOptionalCarrier(int kind, string property)
    {
        var row = Hydrate(CreateAggregate(kind));
        Set(row, property, Guid.CreateVersion7());
        await Reject(row);
    }

    [Test]
    [Arguments("Scope")]
    [Arguments("SubjectOrigin")]
    public async Task EventHydrationRejectsResourceOrCommunityCoordinatesAndMissingEvent(string prefix)
    {
        var scope = Scope(ModerationScopeKind.Event);
        var source = ModerationCase.Open(Guid.CreateVersion7(), Human(scope), scope, Guid.CreateVersion7(), Now);
        foreach (var (coordinate, value) in new (string, object?)[]
        {
            ("ResourceId", scope.Id), ("CommunityId", Guid.CreateVersion7()),
            ("EventId", null), ("EventId", Guid.Empty), ("Kind", ModerationScopeKind.Resource)
        })
        {
            var row = Hydrate(source);
            Set(row, prefix + coordinate, value);
            await Reject(row);
        }
    }

    [Test]
    public async Task ResourceParentIdentityDoesNotCollapseTenantAndCommunity()
    {
        var community = Scope(ModerationScopeKind.Community);
        var resourceId = Guid.CreateVersion7();
        var direct = AuthorityScope.Resource(community.Parent!, resourceId);
        var nested = AuthorityScope.Resource(community, resourceId);
        var source = ModerationCase.Open(Guid.CreateVersion7(), Human(nested), direct, Guid.CreateVersion7(), Now);
        var row = Hydrate(source);

        Validate(row);

        await Assert.That(row.Scope).IsEqualTo(direct);
        var subject = row.Subject ?? throw new InvalidOperationException("The fixture requires an attributed case.");
        await Assert.That(subject.Origin).IsEqualTo(nested);
        await Assert.That(row.Scope.Contains(subject.Origin)).IsFalse();
    }

    [Test]
    [Arguments("Scope")]
    [Arguments("SubjectOrigin")]
    public async Task CoordinateTagsCannotHideMissingOrExtraneousAncestry(string prefix)
    {
        foreach (var property in new[] { "SolutionId", "InstanceId", "TenantId" })
        {
            var row = Hydrate(CreateCase());
            Set(row, prefix + property, null);
            await Reject(row);
        }

        foreach (var kind in new[] { (ModerationScopeKind)99, ModerationScopeKind.TrustDomain, ModerationScopeKind.Community })
        {
            var row = Hydrate(CreateCase());
            Set(row, prefix + "Kind", kind);
            await Reject(row);
        }

        var emptyCommunity = Hydrate(CreateCase());
        Set(emptyCommunity, prefix + "CommunityId", Guid.Empty);
        await Reject(emptyCommunity);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ForeignTrustDomainAndUnknownSubjectKindAreRejected(int kind)
    {
        var foreign = Hydrate(CreateAggregate(kind));
        Set(foreign, "SubjectOriginTrustDomainId", Guid.CreateVersion7());
        await Reject(foreign);
        var unknown = Hydrate(CreateAggregate(kind));
        Set(unknown, "SubjectKind", (ModerationSubjectKind)99);
        await Reject(unknown);
    }

    [Test]
    public async Task CaseStateOccurrenceAndAuditContradictionsAreRejected()
    {
        var row = CreateCase();
        Set(row, "State", (ModerationCaseState)99);
        await Reject(row);
        row = Hydrate(CreateCase());
        Set(row, "OccurrenceId", Guid.Empty);
        await Reject(row);
        row = CreateCase();
        Set(row, "UpdatedAt", Now.AddTicks(-1));
        await Reject(row);
        row = CreateCase();
        Set(row, "State", ModerationCaseState.Closed);
        await Reject(row);
        row = CreateCase();
        row.Close(row.Revision, Now.AddHours(1));
        Validate(Hydrate(row));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(512)]
    [Arguments(1024)]
    [Arguments(2048)]
    [Arguments(4096)]
    [Arguments(-1)]
    public async Task InvalidMasksCannotBecomeRestrictiveCapabilities(int mask)
    {
        foreach (var kind in new[] { 1, 2, 3 })
        {
            var row = Hydrate(CreateAggregate(kind));
            Set(row, "CapabilityMask", mask);
            await Reject(row);
        }
    }

    [Test]
    public async Task CapabilityBitsPreserveOrdinalsAndDoNotShareMutationSources()
    {
        var decision = CreateDecision();
        var row = Hydrate(decision);
        await Assert.That(Get<int>(row, "CapabilityMask")).IsEqualTo(258);
        Set(row, "CapabilityMask", 64);
        Validate(row);

        await Assert.That(row.Capabilities.SetEquals([ModerationCapability.Admission])).IsTrue();
        await Assert.That(decision.Capabilities.SetEquals([ModerationCapability.Publish, ModerationCapability.Enrollment])).IsTrue();
    }

    [Test]
    [Arguments(2, ModerationCapability.Publish)]
    [Arguments(4, ModerationCapability.Upload)]
    [Arguments(8, ModerationCapability.ReadResource)]
    [Arguments(16, ModerationCapability.Manage)]
    [Arguments(32, ModerationCapability.Participate)]
    [Arguments(64, ModerationCapability.Admission)]
    [Arguments(128, ModerationCapability.PrivateLogistics)]
    [Arguments(256, ModerationCapability.Enrollment)]
    public async Task EachRestrictiveBitReconstructsItsExistingBusinessCapability(int mask, ModerationCapability capability)
    {
        var row = Hydrate(CreateDecision());
        Set(row, "CapabilityMask", mask);
        Validate(row);

        await Assert.That(row.Capabilities.SetEquals([capability])).IsTrue();
    }

    [Test]
    public async Task BroadAndIndefiniteDecisionsRetainIndependentQuorumRequirements()
    {
        var scope = Scope(ModerationScopeKind.Solution);
        var decision = ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Human(scope), Human(scope), scope, scope, ModerationRuleReference.Create(Guid.CreateVersion7(), "danger", 2),
            [ModerationCapability.Publish], Now, null, Now.AddDays(1), 2, Now);
        Validate(Hydrate(decision));

        var noQuorum = Hydrate(decision);
        Set(noQuorum, "RequiredApprovals", 1);
        await Reject(noQuorum);
        var noReview = Hydrate(decision);
        Set(noReview, "ReviewAt", null);
        await Reject(noReview);
        var emptyRule = Hydrate(decision);
        Set(emptyRule, "RuleCode", "");
        await Reject(emptyRule);
    }

    [Test]
    public async Task DecisionDefinitionRequiresQualifiedIssuerRuleAndValidInterval()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("CaseId", Guid.Empty), ("IssuerId", Guid.Empty), ("IssuerOriginTrustDomainId", Guid.CreateVersion7()),
            ("IssuingScopeTrustDomainId", Guid.CreateVersion7()), ("RuleOwnerId", Guid.Empty),
            ("RuleCode", "invalid code"), ("RuleRevision", 0L), ("RequiredApprovals", 0),
            ("RequiredApprovals", 3), ("EndsAt", Now), ("EndsAt", null),
            ("ReviewAt", Now), ("ReviewAt", Now.AddDays(2)), ("UpdatedAt", Now.AddTicks(-1))
        })
        {
            var row = Hydrate(CreateDecision());
            Set(row, property, value);
            await Reject(row);
        }
    }

    [Test]
    public async Task DecisionFindingStateAndQuorumMustAgree()
    {
        foreach (var state in new[]
        {
            (ModerationDecisionState)99, ModerationDecisionState.AwaitingFinding,
            ModerationDecisionState.AwaitingApproval, ModerationDecisionState.Approved,
            ModerationDecisionState.Active, ModerationDecisionState.Scheduled
        })
        {
            var row = CreateDecision();
            Set(row, "State", state);
            await Reject(row);
        }

        var withFinding = CreateDecision();
        Set(withFinding, "FindingId", Guid.CreateVersion7());
        await Reject(withFinding);
        var emptyFinding = ReadyDecision();
        Set(emptyFinding, "FindingId", Guid.Empty);
        await Reject(emptyFinding);
    }

    [Test]
    public async Task ApprovalsAreIndependentOrderedImmutableSnapshotsFromScalarSlots()
    {
        var decision = ReadyDecision(2);
        var first = Human(decision.Scope);
        var second = Human(decision.Scope);
        decision.Approve(first, decision.Scope, decision.Revision, Now);
        var snapshot = decision.Approvals;
        decision.Approve(second, decision.Scope, decision.Revision, Now);
        var row = Hydrate(decision);

        Validate(row);

        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(row.Approvals.SequenceEqual(new[] { first, second })).IsTrue();
        await Assert.ThrowsAsync<NotSupportedException>(() =>
        {
            ((IList<SubjectReference>)row.Approvals)[0] = second;
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task ApprovalSlotsRejectPartialGappedConflictedDuplicateAndNonhumanRows()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("Approval1Kind", null), ("Approval1Id", null), ("Approval1OriginKind", null),
            ("Approval1OriginTrustDomainId", null), ("Approval1OriginSolutionId", null),
            ("Approval1OriginTenantId", null), ("Approval1Kind", ModerationSubjectKind.Service),
            ("Approval1OriginTrustDomainId", Guid.CreateVersion7()), ("Approval1Id", Guid.Empty)
        })
        {
            var row = Hydrate(ApprovedDecision(2));
            Set(row, property, value);
            await Reject(row);
        }

        foreach (var conflict in new[] { "IssuerId", "SubjectId", "Approval2Id" })
        {
            var row = Hydrate(ApprovedDecision(2));
            Set(row, "Approval1Id", Get<Guid?>(row, conflict));
            await Reject(row);
        }

        var gap = Hydrate(ApprovedDecision(2));
        foreach (var property in Scalars(gap).Where(property => property.Name.StartsWith("Approval1", StringComparison.Ordinal)))
        {
            Set(gap, property.Name, null);
        }

        await Reject(gap);
        var excess = Hydrate(ApprovedDecision(2));
        Set(excess, "RequiredApprovals", 1);
        await Reject(excess);
        var premature = Hydrate(ReadyDecision(2));
        Set(premature, "State", ModerationDecisionState.Approved);
        await Reject(premature);
    }

    [Test]
    public async Task EveryReachableDecisionShapeValidatesWithoutReplayingTransitions()
    {
        var decision = CreateDecision(2);
        Validate(Hydrate(decision));
        decision.RecordResponse(decision.Revision, Now);
        Validate(Hydrate(decision));
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
        Validate(Hydrate(decision));
        decision.Approve(Human(decision.Scope), decision.Scope, decision.Revision, Now);
        Validate(Hydrate(decision));
        decision.Approve(Human(decision.Scope), decision.Scope, decision.Revision, Now);
        Validate(Hydrate(decision));
        decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
        Validate(Hydrate(decision));

        foreach (var stage in Enumerable.Range(0, 5))
        {
            foreach (var reject in new[] { true, false })
            {
                var terminal = CreateDecision(2);
                if (stage >= 1) terminal.RecordResponse(terminal.Revision, Now);
                if (stage >= 2) terminal.AcceptFinding(Guid.CreateVersion7(), terminal.Revision, Now);
                if (stage >= 3) terminal.Approve(Human(terminal.Scope), terminal.Scope, terminal.Revision, Now);
                if (stage >= 4) terminal.Approve(Human(terminal.Scope), terminal.Scope, terminal.Revision, Now);
                if (reject) terminal.Reject(terminal.Revision, Now);
                else terminal.Withdraw(terminal.Revision, Now);
                Validate(Hydrate(terminal));
            }
        }

        await Assert.That(decision.State).IsEqualTo(ModerationDecisionState.Active);
    }

    [Test]
    public async Task ScheduledAndExpiredContributionsAreNotReconciledAgainstTodaysClock()
    {
        var decision = ApprovedDecision(1, Now.AddHours(1));
        var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
        var scheduled = Hydrate(sanction);
        Validate(scheduled);
        Validate(Hydrate(decision));
        await Assert.That(scheduled.State).IsEqualTo(ModerationSanctionState.Scheduled);
        await Assert.That(scheduled.IsEffectiveAt(Now.AddDays(2))).IsFalse();
        sanction.Reconcile(sanction.Revision, Now.AddDays(2));
        Validate(Hydrate(sanction));
        await Assert.That(sanction.State).IsEqualTo(ModerationSanctionState.Expired);
    }

    [Test]
    public async Task SanctionRejectsImpossibleTimeStateAndReplacementShapes()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("DecisionId", Guid.Empty), ("EndsAt", Now), ("ReviewAt", Now),
            ("State", (ModerationSanctionState)99), ("State", ModerationSanctionState.Expired),
            ("State", ModerationSanctionState.Scheduled), ("State", ModerationSanctionState.Superseded),
            ("UpdatedAt", Now.AddTicks(-1)), ("SupersedingDecisionId", Guid.CreateVersion7())
        })
        {
            var row = Hydrate(CreateSanction());
            Set(row, property, value);
            await Reject(row);
        }

        var replacement = CreateSanction();
        replacement.Supersede(Guid.CreateVersion7(), replacement.Revision, Now);
        Validate(Hydrate(replacement));
        foreach (var id in new[] { Guid.Empty, replacement.DecisionId })
        {
            var row = Hydrate(replacement);
            Set(row, "SupersedingDecisionId", id);
            await Reject(row);
        }
    }

    [Test]
    public async Task HydratedContributionsContinueLifecycleUsingOnlyTheirScalarState()
    {
        foreach (var scheduled in new[] { true, false })
        {
            foreach (var reconcileActive in new[] { true, false })
            {
                foreach (var expire in new[] { true, false })
                {
                    foreach (var supersede in new[] { true, false })
                    {
                        var decision = ApprovedDecision(1, scheduled ? Now.AddHours(1) : Now);
                        var row = Hydrate(decision.Activate(Guid.CreateVersion7(), decision.Revision, Now));
                        if (reconcileActive) row.Reconcile(row.Revision, Now.AddHours(2));
                        if (expire) row.Reconcile(row.Revision, Now.AddDays(1));
                        Validate(Hydrate(row));
                        if (supersede) row.Supersede(Guid.CreateVersion7(), row.Revision, Now.AddDays(2));
                        else row.Revoke(row.Revision, Now.AddDays(2));
                        Validate(Hydrate(row));
                        await Assert.That(row.IsEffectiveAt(Now.AddDays(2))).IsFalse();
                    }
                }
            }
        }
    }

    [Test]
    public async Task HydratedDecisionCanFinishApprovalWithoutChangingAnEarlierSnapshot()
    {
        var source = ReadyDecision(2);
        source.Approve(Human(source.Scope), source.Scope, source.Revision, Now);
        var row = Hydrate(source);
        var snapshot = row.Approvals;
        row.Approve(Human(row.Scope), row.Scope, row.Revision, Now);
        var sanction = row.Activate(Guid.CreateVersion7(), row.Revision, Now);

        Validate(row);
        Validate(sanction);

        await Assert.That(source.State).IsEqualTo(ModerationDecisionState.AwaitingApproval);
        await Assert.That(snapshot.Count).IsEqualTo(1);
        await Assert.That(sanction.Capabilities.SetEquals(source.Capabilities)).IsTrue();
    }

    [Test]
    public async Task PopulatedCoordinatesCannotContainEmptyIdentifiers()
    {
        foreach (var source in new object[] { CreateCase(), ApprovedDecision(2), CreateSanction(), CreateProtection() })
        {
            foreach (var property in Scalars(source).Where(property =>
                (property.PropertyType == typeof(Guid) || property.PropertyType == typeof(Guid?))
                && property.GetValue(source) is not null))
            {
                var row = Hydrate(source);
                Set(row, property.Name, Guid.Empty);
                await Reject(row);
            }
        }
    }

    [Test]
    public async Task ProtectiveReviewRequiresCompleteIndependentHumanAndPairedInstant()
    {
        foreach (var (property, value) in new (string, object?)[]
        {
            ("ProtectorId", Guid.Empty), ("ProtectorOriginTrustDomainId", Guid.CreateVersion7()),
            ("EndsAt", Now), ("ReviewAt", Now), ("ReviewAt", Now.AddDays(2)),
            ("Grounds", ""), ("Grounds", new string('x', 101)), ("ReviewedAt", Now),
            ("ReviewerId", Guid.CreateVersion7()), ("RevokedAt", Now.AddTicks(-1))
        })
        {
            var row = Hydrate(CreateProtection());
            Set(row, property, value);
            await Reject(row);
        }

        var reviewed = CreateProtection();
        reviewed.RecordIndependentReview(Human(reviewed.Scope), reviewed.Scope, reviewed.Revision, Now.AddMinutes(1));
        Validate(Hydrate(reviewed));
        foreach (var (property, value) in new (string, object?)[]
        {
            ("ReviewerKind", ModerationSubjectKind.Service), ("ReviewerId", reviewed.Subject.Id),
            ("ReviewerId", reviewed.Protector.Id), ("ReviewerOriginTrustDomainId", Guid.CreateVersion7()),
            ("ReviewerOriginKind", null), ("ReviewedAt", null), ("ReviewedAt", Now.AddDays(1))
        })
        {
            var row = Hydrate(reviewed);
            Set(row, property, value);
            await Reject(row);
        }

        reviewed.Revoke(reviewed.Revision, Now.AddMinutes(2));
        Validate(Hydrate(reviewed));
        await Assert.That(reviewed.IsEffectiveAt(Now.AddMinutes(3))).IsFalse();
    }

    private static object CreateAggregate(int kind) => kind switch
    {
        0 => CreateCase(),
        1 => CreateDecision(),
        2 => CreateSanction(),
        3 => CreateProtection(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static AuthorityScope Scope(ModerationScopeKind kind = ModerationScopeKind.Resource)
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        if (kind == ModerationScopeKind.TrustDomain) return trust;
        var solution = AuthorityScope.Solution(trust, Guid.CreateVersion7());
        if (kind == ModerationScopeKind.Solution) return solution;
        var instance = AuthorityScope.Instance(solution, Guid.CreateVersion7());
        if (kind == ModerationScopeKind.Instance) return instance;
        var tenant = AuthorityScope.Tenant(instance, Guid.CreateVersion7());
        if (kind == ModerationScopeKind.Tenant) return tenant;
        if (kind == ModerationScopeKind.Event) return AuthorityScope.Event(tenant, Guid.CreateVersion7());
        var community = AuthorityScope.Community(tenant, Guid.CreateVersion7());
        return kind == ModerationScopeKind.Community
            ? community : AuthorityScope.Resource(community, Guid.CreateVersion7());
    }

    private static SubjectReference Human(AuthorityScope scope) =>
        SubjectReference.Create(ModerationSubjectKind.Human, Guid.CreateVersion7(), scope);

    private static ModerationCase CreateCase()
    {
        var scope = Scope();
        return ModerationCase.Open(Guid.CreateVersion7(), Human(scope), scope, Guid.CreateVersion7(), Now);
    }

    private static ModerationDecision CreateDecision(int approvals = 1, DateTimeOffset? start = null)
    {
        var scope = Scope();
        return ModerationDecision.Propose(Guid.CreateVersion7(), Guid.CreateVersion7(), Human(scope), Human(scope),
            scope.Parent!, scope, ModerationRuleReference.Create(Guid.CreateVersion7(), "danger", 1),
            [ModerationCapability.Publish, ModerationCapability.Enrollment],
            start ?? Now, Now.AddDays(1), null, approvals, Now);
    }

    private static ModerationDecision ReadyDecision(int approvals = 1)
    {
        var decision = CreateDecision(approvals);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
        return decision;
    }

    private static ModerationDecision ApprovedDecision(int approvals = 1, DateTimeOffset? start = null)
    {
        var decision = CreateDecision(approvals, start);
        decision.RecordResponse(decision.Revision, Now);
        decision.AcceptFinding(Guid.CreateVersion7(), decision.Revision, Now);
        for (var index = 0; index < approvals; index++)
        {
            decision.Approve(Human(decision.Scope), decision.Scope, decision.Revision, Now);
        }

        return decision;
    }

    private static ModerationSanction CreateSanction()
    {
        var decision = ApprovedDecision();
        return decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
    }

    private static ModerationProtectiveMeasure CreateProtection()
    {
        var scope = Scope();
        return ModerationProtectiveMeasure.Create(Guid.CreateVersion7(), Human(scope), Human(scope),
            scope.Parent!, scope, [ModerationCapability.Admission], "danger",
            Now, Now.AddDays(1), Now.AddHours(1));
    }

    private static PropertyInfo[] Scalars(object row) => row.GetType().GetProperties()
        .Where(property => property.PropertyType.IsValueType || property.PropertyType == typeof(string))
        .ToArray();

    private static T Hydrate<T>(T source) where T : class
    {
        var type = source.GetType();
        var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new InvalidOperationException($"{type.Name} needs a private hydration constructor.");
        var row = (T)constructor.Invoke(null);
        foreach (var property in Scalars(source))
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
            throw new InvalidOperationException($"{name} needs a private setter.");
        }

        property.SetValue(row, value);
    }

    private static T Get<T>(object row, string name) =>
        (T)(row.GetType().GetProperty(name)?.GetValue(row)
            ?? throw new InvalidOperationException($"Missing value {name}."));

    private static void Validate(object row)
    {
        var method = row.GetType().GetMethod("ValidatePersistedState", Type.EmptyTypes)
            ?? throw new InvalidOperationException("Missing materialization validation seam.");
        try
        {
            method.Invoke(row, null);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static async Task Reject(object row)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
        {
            Validate(row);
            return Task.CompletedTask;
        });
    }
}
