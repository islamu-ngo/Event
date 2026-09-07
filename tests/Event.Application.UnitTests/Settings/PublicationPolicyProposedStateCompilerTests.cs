namespace Event.Application.UnitTests.Settings;

using System.Collections.Immutable;
using Explore.Application.Settings;
using Explore.Domain.Settings.Definitions;

public sealed class PublicationPolicyProposedStateCompilerTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TenantC = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Test]
    public async Task GuardedKeys_AllContainsExactlyTheFiveCanonicalKeysInPolicyStateOrder()
    {
        IReadOnlyList<string> keys = PublicationPolicySettingKeys.All;

        await Assert.That(keys.Count).IsEqualTo(5);
        await Assert.That(keys[0]).IsEqualTo(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key);
        await Assert.That(keys[1]).IsEqualTo(EventSettingDefinitions.RequireApproval.Key);
        await Assert.That(keys[2]).IsEqualTo(EventSettingDefinitions.UserSubmissionEnabled.Key);
        await Assert.That(keys[3]).IsEqualTo(EventSettingDefinitions.OrganizationSubmissionEnabled.Key);
        await Assert.That(keys[4]).IsEqualTo(EventSettingDefinitions.GroupSubmissionEnabled.Key);
    }

    [Test]
    public async Task MutationKind_ContainsExactlySetAndRemove()
    {
        string[] names = Enum.GetNames<PublicationPolicyMutationKind>();

        await Assert.That(names.SequenceEqual(["Set", "Remove"])).IsTrue();
    }

    [Test]
    public async Task CompileTenant_MissingRowsUsesRegistryDefaults()
    {
        PublicationPolicyCompilationResult result = CompileTenant();

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state).IsEqualTo(new ReportingIntakePolicyState(
            IntakeEnabled: true,
            RequireApproval: false,
            UserSubmissionEnabled: true,
            OrganizationSubmissionEnabled: true,
            GroupSubmissionEnabled: true));
    }

    [Test]
    public async Task CompileTenant_UnlockedSystemValuesOverrideDefaults()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues:
            [
                System(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false),
                System(EventSettingDefinitions.RequireApproval.Key, true)
            ]);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state.IntakeEnabled).IsFalse();
        await Assert.That(state.RequireApproval).IsTrue();
    }

    [Test]
    public async Task CompileTenant_TenantOverrideWinsOverUnlockedSystemValue()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues: [System(EventSettingDefinitions.UserSubmissionEnabled.Key, false)],
            tenantValues: [Tenant(TenantA, EventSettingDefinitions.UserSubmissionEnabled.Key, true)]);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state.UserSubmissionEnabled).IsTrue();
    }

    [Test]
    public async Task CompileTenant_LockedSystemValueSuppressesStoredTenantOverride()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues: [System(EventSettingDefinitions.UserSubmissionEnabled.Key, false, isLocked: true)],
            tenantValues: [Tenant(TenantA, EventSettingDefinitions.UserSubmissionEnabled.Key, true)]);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state.UserSubmissionEnabled).IsFalse();
    }

    [Test]
    public async Task CompileTenant_SetMutationOverlaysCurrentTenantValue()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues: [System(EventSettingDefinitions.RequireApproval.Key, false)],
            tenantValues: [Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, false)],
            mutations: [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state.RequireApproval).IsTrue();
    }

    [Test]
    public async Task CompileTenant_RemoveMutationRevealsUnlockedParentValue()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues: [System(EventSettingDefinitions.GroupSubmissionEnabled.Key, true)],
            tenantValues: [Tenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key, false)],
            mutations: [RemoveTenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key)]);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        await Assert.That(state.GroupSubmissionEnabled).IsTrue();
    }

    [Test]
    public async Task CompileTenant_CompleteMultiKeyOverlayIsCompiledBeforeSafetyEvaluation()
    {
        ImmutableArray<PublicationPolicySettingMutation> mutations =
        [
            SetTenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.UserSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.OrganizationSubmissionEnabled.Key, false),
            SetTenant(TenantA, EventSettingDefinitions.GroupSubmissionEnabled.Key, false)
        ];

        PublicationPolicyCompilationResult result = CompileTenant(mutations: mutations);

        ReportingIntakePolicyState state = await AssertTenantSuccessAsync(result, TenantA);
        ReportingIntakePolicyEvaluation evaluation = ReportingIntakePolicyEvaluator.Evaluate(state);
        await Assert.That(state).IsEqualTo(new ReportingIntakePolicyState(false, false, false, false, false));
        await Assert.That(evaluation.Allowed).IsTrue();
        await Assert.That(evaluation.ReasonCode)
            .IsEqualTo(ReportingIntakePolicyReasonCodes.ProtectedByClosedSubmissions);
    }

    [Test]
    public async Task CompileTenant_DuplicateMutationForOneKeyFailsClosed()
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            mutations:
            [
                SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true),
                RemoveTenant(TenantA, EventSettingDefinitions.RequireApproval.Key)
            ]);

        await AssertInvalidAsync(result);
    }

    [Test]
    [Arguments("unknown.publication.key")]
    [Arguments("events.max_sessions_per_event")]
    public async Task CompileTenant_UnknownOrNonGuardedMutationKeyFailsClosed(string key)
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            mutations: [SetTenant(TenantA, key, true)]);

        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task CompileTenant_MissingTenantIdentityOnInputFailsClosed()
    {
        var input = new PublicationPolicyTenantCompilationInput(
            TenantId: null,
            SystemValues: [],
            TenantValues: [],
            Mutations: []);

        PublicationPolicyCompilationResult result =
            PublicationPolicyProposedStateCompiler.CompileTenant(input);

        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task CompileTenant_RejectsSystemShapedSetAndRemoveMutations()
    {
        PublicationPolicySettingMutation[] systemShapedMutations =
        [
            SetSystem(EventSettingDefinitions.RequireApproval.Key, true, isLocked: false),
            RemoveSystem(EventSettingDefinitions.RequireApproval.Key)
        ];

        foreach (PublicationPolicySettingMutation mutation in systemShapedMutations)
        {
            await AssertInvalidAsync(CompileTenant(mutations: [mutation]));
        }
    }

    [Test]
    public async Task CompileTenant_MismatchedSetAndRemoveMutationTenantIdentityFailClosed()
    {
        PublicationPolicySettingMutation[] mismatchedMutations =
        [
            SetTenant(TenantB, EventSettingDefinitions.RequireApproval.Key, true),
            RemoveTenant(TenantB, EventSettingDefinitions.RequireApproval.Key)
        ];

        foreach (PublicationPolicySettingMutation mutation in mismatchedMutations)
        {
            await AssertInvalidAsync(CompileTenant(mutations: [mutation]));
        }
    }

    [Test]
    public async Task CompileInstance_RejectsEveryMutationWithTenantIdentity()
    {
        PublicationPolicySettingMutation[] tenantShapedMutations =
        [
            SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true),
            RemoveTenant(TenantA, EventSettingDefinitions.RequireApproval.Key)
        ];

        foreach (PublicationPolicySettingMutation mutation in tenantShapedMutations)
        {
            var input = new PublicationPolicyInstanceCompilationInput(
                SystemValues: [],
                TenantValues: [],
                Mutations: [mutation]);

            await AssertInvalidAsync(PublicationPolicyProposedStateCompiler.CompileInstance(input));
        }
    }

    [Test]
    public async Task MutationShapes_ValidTenantAndSystemSetAndRemoveRowsCompile()
    {
        PublicationPolicyCompilationResult tenantSet = CompileTenant(
            mutations: [SetTenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)]);
        PublicationPolicyCompilationResult tenantRemove = CompileTenant(
            tenantValues: [Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)],
            mutations: [RemoveTenant(TenantA, EventSettingDefinitions.RequireApproval.Key)]);
        PublicationPolicyCompilationResult systemSet = PublicationPolicyProposedStateCompiler.CompileInstance(
            new PublicationPolicyInstanceCompilationInput(
                SystemValues: [],
                TenantValues: [],
                Mutations: [SetSystem(EventSettingDefinitions.RequireApproval.Key, true, isLocked: false)]));
        PublicationPolicyCompilationResult systemRemove = PublicationPolicyProposedStateCompiler.CompileInstance(
            new PublicationPolicyInstanceCompilationInput(
                SystemValues: [System(EventSettingDefinitions.RequireApproval.Key, true)],
                TenantValues: [],
                Mutations: [RemoveSystem(EventSettingDefinitions.RequireApproval.Key)]));

        await AssertTenantSuccessAsync(tenantSet, TenantA);
        await AssertTenantSuccessAsync(tenantRemove, TenantA);
        await Assert.That(systemSet.Success).IsTrue();
        await Assert.That(systemRemove.Success).IsTrue();
    }

    [Test]
    public async Task MutationShapes_InvalidSetRowsFailClosed()
    {
        PublicationPolicySettingMutation[] invalidTenantSets =
        [
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: null, TenantA, IsLocked: null),
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: "invalid", TenantA, IsLocked: null),
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: "true", TenantA, IsLocked: false)
        ];
        PublicationPolicySettingMutation[] invalidSystemSets =
        [
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: null, TenantId: null, IsLocked: false),
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: "invalid", TenantId: null, IsLocked: false),
            Mutation(PublicationPolicyMutationKind.Set, JsonValue: "true", TenantId: null, IsLocked: null)
        ];

        foreach (PublicationPolicySettingMutation mutation in invalidTenantSets)
        {
            await AssertInvalidAsync(CompileTenant(mutations: [mutation]));
        }

        foreach (PublicationPolicySettingMutation mutation in invalidSystemSets)
        {
            var input = new PublicationPolicyInstanceCompilationInput([], [], [mutation]);
            await AssertInvalidAsync(PublicationPolicyProposedStateCompiler.CompileInstance(input));
        }
    }

    [Test]
    public async Task MutationShapes_RemoveRowsCarryingValueOrLockFailClosed()
    {
        PublicationPolicySettingMutation[] invalidTenantRemoves =
        [
            Mutation(PublicationPolicyMutationKind.Remove, JsonValue: "false", TenantA, IsLocked: null),
            Mutation(PublicationPolicyMutationKind.Remove, JsonValue: null, TenantA, IsLocked: false)
        ];
        PublicationPolicySettingMutation[] invalidSystemRemoves =
        [
            Mutation(PublicationPolicyMutationKind.Remove, JsonValue: "false", TenantId: null, IsLocked: null),
            Mutation(PublicationPolicyMutationKind.Remove, JsonValue: null, TenantId: null, IsLocked: true)
        ];

        foreach (PublicationPolicySettingMutation mutation in invalidTenantRemoves)
        {
            await AssertInvalidAsync(CompileTenant(mutations: [mutation]));
        }

        foreach (PublicationPolicySettingMutation mutation in invalidSystemRemoves)
        {
            var input = new PublicationPolicyInstanceCompilationInput([], [], [mutation]);
            await AssertInvalidAsync(PublicationPolicyProposedStateCompiler.CompileInstance(input));
        }
    }

    [Test]
    [Arguments("TRUE")]
    [Arguments("\"true\"")]
    [Arguments("1")]
    [Arguments(null)]
    public async Task CompileTenant_MalformedBooleanMutationFailsClosed(string? jsonValue)
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            mutations:
            [
                new PublicationPolicySettingMutation(
                    EventSettingDefinitions.RequireApproval.Key,
                    PublicationPolicyMutationKind.Set,
                    jsonValue,
                    TenantA,
                    IsLocked: null)
            ]);

        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task CompileTenant_MalformedBooleanSnapshotFailsClosed()
    {
        PublicationPolicyCompilationResult systemResult = CompileTenant(
            systemValues:
            [
                new PublicationPolicySystemValueSnapshot(
                    EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
                    JsonValue: "not-a-boolean",
                    IsLocked: false)
            ]);
        PublicationPolicyCompilationResult tenantResult = CompileTenant(
            tenantValues:
            [
                new PublicationPolicyTenantValueSnapshot(
                    TenantA,
                    EventSettingDefinitions.RequireApproval.Key,
                    JsonValue: "null")
            ]);

        await AssertInvalidAsync(systemResult);
        await AssertInvalidAsync(tenantResult);
    }

    [Test]
    public async Task CompileTenant_DuplicateSystemAndTenantSnapshotKeysFailClosed()
    {
        PublicationPolicyCompilationResult duplicateSystem = CompileTenant(
            systemValues:
            [
                System(EventSettingDefinitions.RequireApproval.Key, false),
                System(EventSettingDefinitions.RequireApproval.Key, true)
            ]);
        PublicationPolicyCompilationResult duplicateTenant = CompileTenant(
            tenantValues:
            [
                Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, false),
                Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)
            ]);

        await AssertInvalidAsync(duplicateSystem);
        await AssertInvalidAsync(duplicateTenant);
    }

    [Test]
    [Arguments("unknown.publication.key")]
    [Arguments("events.max_sessions_per_event")]
    public async Task CompileTenant_UnknownOrNonGuardedSystemSnapshotKeyFailsClosed(string key)
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            systemValues: [new PublicationPolicySystemValueSnapshot(key, "true", IsLocked: false)]);

        await AssertInvalidAsync(result);
    }

    [Test]
    [Arguments("unknown.publication.key")]
    [Arguments("events.max_sessions_per_event")]
    public async Task CompileTenant_UnknownOrNonGuardedTenantSnapshotKeyFailsClosed(string key)
    {
        PublicationPolicyCompilationResult result = CompileTenant(
            tenantValues: [new PublicationPolicyTenantValueSnapshot(TenantA, key, "true")]);

        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task CompileTenant_MissingOrMismatchedTenantSnapshotIdentityFailsClosed()
    {
        PublicationPolicyCompilationResult missingIdentity = CompileTenant(
            tenantValues:
            [
                new PublicationPolicyTenantValueSnapshot(
                    TenantId: null,
                    EventSettingDefinitions.RequireApproval.Key,
                    JsonValue: "true")
            ]);
        PublicationPolicyCompilationResult mismatchedIdentity = CompileTenant(
            tenantValues: [Tenant(TenantB, EventSettingDefinitions.RequireApproval.Key, true)]);

        await AssertInvalidAsync(missingIdentity);
        await AssertInvalidAsync(mismatchedIdentity);
    }

    [Test]
    [MethodDataSource(nameof(InvalidInstanceSnapshotInputs))]
    public async Task CompileInstance_InvalidSnapshotsFailClosed(
        (string Category, PublicationPolicyInstanceCompilationInput Input) testCase)
    {
        PublicationPolicyCompilationResult result =
            PublicationPolicyProposedStateCompiler.CompileInstance(testCase.Input);

        await AssertInvalidAsync(result);
    }

    [Test]
    public async Task CompileInstance_AppliesSystemProposalToBaseAndEveryTenantGroupWhilePreservingOverrides()
    {
        var input = new PublicationPolicyInstanceCompilationInput(
            SystemValues:
            [
                System(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false),
                System(EventSettingDefinitions.RequireApproval.Key, false)
            ],
            TenantValues:
            [
                Tenant(TenantB, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true),
                Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, false)
            ],
            Mutations:
            [
                SetSystem(EventSettingDefinitions.RequireApproval.Key, true, isLocked: false)
            ]);

        PublicationPolicyCompilationResult result = PublicationPolicyProposedStateCompiler.CompileInstance(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.BaseTenantState).IsEqualTo(
            new ReportingIntakePolicyState(false, true, true, true, true));
        await Assert.That(result.TenantStates.Count).IsEqualTo(2);
        await Assert.That(result.TenantStates[0].TenantId).IsEqualTo(TenantA);
        await Assert.That(result.TenantStates[0].State.RequireApproval).IsFalse();
        await Assert.That(result.TenantStates[1].TenantId).IsEqualTo(TenantB);
        await Assert.That(result.TenantStates[1].State.IntakeEnabled).IsTrue();

        ReportingIntakePolicyEvaluation baseEvaluation =
            ReportingIntakePolicyEvaluator.Evaluate(result.BaseTenantState!.Value);
        ReportingIntakePolicyEvaluation tenantAEvaluation =
            ReportingIntakePolicyEvaluator.Evaluate(result.TenantStates[0].State);
        ReportingIntakePolicyEvaluation tenantBEvaluation =
            ReportingIntakePolicyEvaluator.Evaluate(result.TenantStates[1].State);
        await Assert.That(baseEvaluation.Allowed).IsTrue();
        await Assert.That(tenantAEvaluation.Allowed).IsFalse();
        await Assert.That(tenantAEvaluation.ReasonCode)
            .IsEqualTo(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy);
        await Assert.That(tenantBEvaluation.Allowed).IsTrue();
    }

    [Test]
    public async Task CompileInstance_ProposedLockedSystemValueSuppressesEveryTenantOverride()
    {
        var input = new PublicationPolicyInstanceCompilationInput(
            SystemValues: [],
            TenantValues:
            [
                Tenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true),
                Tenant(TenantB, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true)
            ],
            Mutations:
            [
                SetSystem(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false, isLocked: true)
            ]);

        PublicationPolicyCompilationResult result = PublicationPolicyProposedStateCompiler.CompileInstance(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.BaseTenantState!.Value.IntakeEnabled).IsFalse();
        await Assert.That(result.TenantStates.Count).IsEqualTo(2);
        await Assert.That(result.TenantStates[0].State.IntakeEnabled).IsFalse();
        await Assert.That(result.TenantStates[1].State.IntakeEnabled).IsFalse();
    }

    [Test]
    public async Task CompileInstance_SystemSetReplacesLockedWithUnlockedAndRevealsStoredTenantOverride()
    {
        var input = new PublicationPolicyInstanceCompilationInput(
            SystemValues: [System(EventSettingDefinitions.RequireApproval.Key, true, isLocked: true)],
            TenantValues: [Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, false)],
            Mutations: [SetSystem(EventSettingDefinitions.RequireApproval.Key, true, isLocked: false)]);

        PublicationPolicyCompilationResult result = PublicationPolicyProposedStateCompiler.CompileInstance(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.BaseTenantState!.Value.RequireApproval).IsTrue();
        await Assert.That(result.TenantStates[0].State.RequireApproval).IsFalse();
    }

    [Test]
    public async Task CompileInstance_RemoveLockedSystemRowRevealsDefaultAndStoredTenantOverride()
    {
        var input = new PublicationPolicyInstanceCompilationInput(
            SystemValues: [System(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false, isLocked: true)],
            TenantValues: [Tenant(TenantA, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, false)],
            Mutations: [RemoveSystem(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key)]);

        PublicationPolicyCompilationResult result = PublicationPolicyProposedStateCompiler.CompileInstance(input);

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.BaseTenantState!.Value.IntakeEnabled).IsTrue();
        await Assert.That(result.TenantStates[0].State.IntakeEnabled).IsFalse();
    }

    [Test]
    public async Task CompileInstance_OutputsTenantsDeterministicallyAndDoesNotMutateInputSnapshots()
    {
        ImmutableArray<PublicationPolicySystemValueSnapshot> systems =
        [
            System(EventSettingDefinitions.GroupSubmissionEnabled.Key, false),
            System(EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, true)
        ];
        ImmutableArray<PublicationPolicyTenantValueSnapshot> tenants =
        [
            Tenant(TenantC, EventSettingDefinitions.UserSubmissionEnabled.Key, false),
            Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true),
            Tenant(TenantB, EventSettingDefinitions.OrganizationSubmissionEnabled.Key, false)
        ];
        ImmutableArray<PublicationPolicySettingMutation> mutations =
        [
            SetSystem(EventSettingDefinitions.GroupSubmissionEnabled.Key, true, isLocked: false),
            SetSystem(EventSettingDefinitions.RequireApproval.Key, false, isLocked: false)
        ];
        (string Key, string? JsonValue, bool IsLocked)[] systemsBefore = systems
            .Select(row => (row.Key, row.JsonValue, row.IsLocked))
            .ToArray();
        (Guid? TenantId, string Key, string? JsonValue)[] tenantsBefore = tenants
            .Select(row => (row.TenantId, row.Key, row.JsonValue))
            .ToArray();
        (string Key, PublicationPolicyMutationKind Kind, string? JsonValue, Guid? TenantId, bool? IsLocked)[] mutationsBefore = mutations
            .Select(row => (row.Key, row.Kind, row.JsonValue, row.TenantId, row.IsLocked))
            .ToArray();

        PublicationPolicyCompilationResult result = PublicationPolicyProposedStateCompiler.CompileInstance(
            new PublicationPolicyInstanceCompilationInput(systems, tenants, mutations));

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.TenantStates.Select(compiled => compiled.TenantId)
            .SequenceEqual([TenantA, TenantB, TenantC])).IsTrue();
        await Assert.That(systems.Select(row => (row.Key, row.JsonValue, row.IsLocked))
            .SequenceEqual(systemsBefore)).IsTrue();
        await Assert.That(tenants.Select(row => (row.TenantId, row.Key, row.JsonValue))
            .SequenceEqual(tenantsBefore)).IsTrue();
        await Assert.That(mutations.Select(row => (row.Key, row.Kind, row.JsonValue, row.TenantId, row.IsLocked))
            .SequenceEqual(mutationsBefore)).IsTrue();
    }

    public static IEnumerable<(string Category, PublicationPolicyInstanceCompilationInput Input)>
        InvalidInstanceSnapshotInputs()
    {
        yield return (
            "malformed-system-boolean",
            new PublicationPolicyInstanceCompilationInput(
                [new PublicationPolicySystemValueSnapshot(
                    EventReportingIntakeSettingDefinitions.IntakeEnabled.Key,
                    JsonValue: "not-a-boolean",
                    IsLocked: false)],
                [],
                []));
        yield return (
            "malformed-tenant-boolean",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [new PublicationPolicyTenantValueSnapshot(
                    TenantA,
                    EventSettingDefinitions.RequireApproval.Key,
                    JsonValue: "null")],
                []));
        yield return (
            "duplicate-system-key",
            new PublicationPolicyInstanceCompilationInput(
                [
                    System(EventSettingDefinitions.RequireApproval.Key, false),
                    System(EventSettingDefinitions.RequireApproval.Key, true)
                ],
                [],
                []));
        yield return (
            "duplicate-tenant-key-within-one-tenant",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [
                    Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, false),
                    Tenant(TenantA, EventSettingDefinitions.RequireApproval.Key, true)
                ],
                []));
        yield return (
            "unknown-system-key",
            new PublicationPolicyInstanceCompilationInput(
                [new PublicationPolicySystemValueSnapshot("unknown.publication.key", "true", IsLocked: false)],
                [],
                []));
        yield return (
            "non-guarded-system-key",
            new PublicationPolicyInstanceCompilationInput(
                [new PublicationPolicySystemValueSnapshot("events.max_sessions_per_event", "true", IsLocked: false)],
                [],
                []));
        yield return (
            "unknown-tenant-key",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [new PublicationPolicyTenantValueSnapshot(TenantA, "unknown.publication.key", "true")],
                []));
        yield return (
            "non-guarded-tenant-key",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [new PublicationPolicyTenantValueSnapshot(TenantA, "events.max_sessions_per_event", "true")],
                []));
        yield return (
            "null-tenant-identity",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [new PublicationPolicyTenantValueSnapshot(
                    TenantId: null,
                    EventSettingDefinitions.RequireApproval.Key,
                    JsonValue: "true")],
                []));
        yield return (
            "empty-tenant-identity",
            new PublicationPolicyInstanceCompilationInput(
                [],
                [new PublicationPolicyTenantValueSnapshot(
                    Guid.Empty,
                    EventSettingDefinitions.RequireApproval.Key,
                    JsonValue: "true")],
                []));
    }

    private static PublicationPolicyCompilationResult CompileTenant(
        ImmutableArray<PublicationPolicySystemValueSnapshot> systemValues = default,
        ImmutableArray<PublicationPolicyTenantValueSnapshot> tenantValues = default,
        ImmutableArray<PublicationPolicySettingMutation> mutations = default)
    {
        var input = new PublicationPolicyTenantCompilationInput(
            TenantA,
            systemValues.IsDefault ? [] : systemValues,
            tenantValues.IsDefault ? [] : tenantValues,
            mutations.IsDefault ? [] : mutations);
        return PublicationPolicyProposedStateCompiler.CompileTenant(input);
    }

    private static PublicationPolicySystemValueSnapshot System(string key, bool value, bool isLocked = false) =>
        new(key, value ? "true" : "false", isLocked);

    private static PublicationPolicyTenantValueSnapshot Tenant(Guid tenantId, string key, bool value) =>
        new(tenantId, key, value ? "true" : "false");

    private static PublicationPolicySettingMutation Mutation(
        PublicationPolicyMutationKind kind,
        string? JsonValue,
        Guid? TenantId,
        bool? IsLocked) =>
        new(EventSettingDefinitions.RequireApproval.Key, kind, JsonValue, TenantId, IsLocked);

    private static PublicationPolicySettingMutation SetTenant(Guid tenantId, string key, bool value) =>
        new(key, PublicationPolicyMutationKind.Set, value ? "true" : "false", tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation RemoveTenant(Guid tenantId, string key) =>
        new(key, PublicationPolicyMutationKind.Remove, JsonValue: null, tenantId, IsLocked: null);

    private static PublicationPolicySettingMutation SetSystem(string key, bool value, bool isLocked) =>
        new(key, PublicationPolicyMutationKind.Set, value ? "true" : "false", TenantId: null, isLocked);

    private static PublicationPolicySettingMutation RemoveSystem(string key) =>
        new(key, PublicationPolicyMutationKind.Remove, JsonValue: null, TenantId: null, IsLocked: null);

    private static async Task<ReportingIntakePolicyState> AssertTenantSuccessAsync(
        PublicationPolicyCompilationResult result,
        Guid expectedTenantId)
    {
        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
        await Assert.That(result.BaseTenantState).IsNull();
        await Assert.That(result.TenantStates.Count).IsEqualTo(1);
        await Assert.That(result.TenantStates[0].TenantId).IsEqualTo(expectedTenantId);
        return result.TenantStates[0].State;
    }

    private static async Task AssertInvalidAsync(PublicationPolicyCompilationResult result)
    {
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("event_reporting_intake_policy_invalid");
        await Assert.That(result.BaseTenantState).IsNull();
        await Assert.That(result.TenantStates.Count).IsEqualTo(0);
    }
}
