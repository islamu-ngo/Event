namespace Event.Application.UnitTests.Settings;

using Explore.Application.Settings;

public sealed class ReportingIntakePolicyEvaluatorTests
{
    [Test]
    public async Task ReasonCodes_UseExactStableValues()
    {
        await Assert.That(ReportingIntakePolicyReasonCodes.IntakeEnabled)
            .IsEqualTo("event_reporting_intake_enabled");
        await Assert.That(ReportingIntakePolicyReasonCodes.ProtectedByApproval)
            .IsEqualTo("event_reporting_intake_protected_by_approval");
        await Assert.That(ReportingIntakePolicyReasonCodes.ProtectedByClosedSubmissions)
            .IsEqualTo("event_reporting_intake_protected_by_closed_submissions");
        await Assert.That(ReportingIntakePolicyReasonCodes.UnsafePublicationPolicy)
            .IsEqualTo("event_reporting_intake_unsafe_publication_policy");
    }

    [Test]
    [MethodDataSource(nameof(AllPolicyStates))]
    public async Task Evaluate_AllPublicationPolicyCombinations_ReturnsExpectedSafetyAndReasonCode(
        (bool IntakeEnabled, bool RequireApproval, bool UserSubmissionEnabled,
            bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled,
            bool Allowed, string ReasonCode) testCase)
    {
        ReportingIntakePolicyEvaluation evaluation = ReportingIntakePolicyEvaluator.Evaluate(CreateState(testCase));

        await AssertEvaluationAsync(evaluation, testCase.Allowed, testCase.ReasonCode);
    }

    [Test]
    public async Task AllPublicationPolicyCombinations_ContainExactlySevenUnsafeStates()
    {
        int unsafeStateCount = 0;
        foreach (var testCase in AllPolicyStates())
        {
            if (!testCase.Allowed)
                unsafeStateCount++;
        }

        await Assert.That(unsafeStateCount).IsEqualTo(7);
    }

    [Test]
    [MethodDataSource(nameof(NonIntakePolicyStates))]
    public async Task Evaluate_TurningIntakeOn_MakesEveryPublicationPolicySafe(
        (bool RequireApproval, bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
            bool GroupSubmissionEnabled) testCase)
    {
        var disabledIntake = new ReportingIntakePolicyState(
            false,
            testCase.RequireApproval,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);
        ReportingIntakePolicyState enabledIntake = disabledIntake with { IntakeEnabled = true };

        ReportingIntakePolicyEvaluation evaluation = ReportingIntakePolicyEvaluator.Evaluate(enabledIntake);

        await AssertEvaluationAsync(evaluation, true, "event_reporting_intake_enabled");
    }

    [Test]
    [MethodDataSource(nameof(SubmissionPolicyStates))]
    public async Task Evaluate_TurningApprovalOn_MakesEveryPublicationPolicySafe(
        (bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
            bool GroupSubmissionEnabled) testCase)
    {
        var withoutApproval = new ReportingIntakePolicyState(
            false,
            false,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);
        ReportingIntakePolicyState withApproval = withoutApproval with { RequireApproval = true };

        ReportingIntakePolicyEvaluation evaluation = ReportingIntakePolicyEvaluator.Evaluate(withApproval);

        await AssertEvaluationAsync(evaluation, true, "event_reporting_intake_protected_by_approval");
    }

    [Test]
    [MethodDataSource(nameof(ClosingSubmissionPathTransitions))]
    public async Task Evaluate_DisablingAnOrdinarySubmissionPath_MovesTowardSafetyAndOnlyAllClosedIsSafe(
        (bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled,
            SubmissionPath PathToClose, bool ExpectedAllowedAfterClosing) testCase)
    {
        var beforeClosing = new ReportingIntakePolicyState(
            false,
            false,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);
        ReportingIntakePolicyState afterClosing = ClosePath(beforeClosing, testCase.PathToClose);

        ReportingIntakePolicyEvaluation beforeEvaluation = ReportingIntakePolicyEvaluator.Evaluate(beforeClosing);
        ReportingIntakePolicyEvaluation afterEvaluation = ReportingIntakePolicyEvaluator.Evaluate(afterClosing);

        await AssertEvaluationAsync(beforeEvaluation, false, "event_reporting_intake_unsafe_publication_policy");
        await AssertEvaluationAsync(
            afterEvaluation,
            testCase.ExpectedAllowedAfterClosing,
            testCase.ExpectedAllowedAfterClosing
                ? "event_reporting_intake_protected_by_closed_submissions"
                : "event_reporting_intake_unsafe_publication_policy");
    }

    [Test]
    [MethodDataSource(nameof(OpeningSubmissionPathTransitions))]
    public async Task Evaluate_EnablingAnyOrdinarySubmissionPathWithoutIntakeOrApproval_IsUnsafe(
        (bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled,
            SubmissionPath PathToOpen) testCase)
    {
        var beforeOpening = new ReportingIntakePolicyState(
            false,
            false,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);
        ReportingIntakePolicyState afterOpening = OpenPath(beforeOpening, testCase.PathToOpen);

        ReportingIntakePolicyEvaluation beforeEvaluation = ReportingIntakePolicyEvaluator.Evaluate(beforeOpening);
        ReportingIntakePolicyEvaluation afterEvaluation = ReportingIntakePolicyEvaluator.Evaluate(afterOpening);

        await Assert.That(beforeEvaluation.Allowed).IsEqualTo(
            !testCase.UserSubmissionEnabled
            && !testCase.OrganizationSubmissionEnabled
            && !testCase.GroupSubmissionEnabled);
        await AssertEvaluationAsync(afterEvaluation, false, "event_reporting_intake_unsafe_publication_policy");
    }

    [Test]
    [MethodDataSource(nameof(OpenSubmissionPolicyStates))]
    public async Task Evaluate_TurningApprovalOffWithAnOpenSubmissionPath_IsUnsafe(
        (bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled) testCase)
    {
        var withApproval = new ReportingIntakePolicyState(
            false,
            true,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);
        ReportingIntakePolicyState withoutApproval = withApproval with { RequireApproval = false };

        ReportingIntakePolicyEvaluation safeEvaluation = ReportingIntakePolicyEvaluator.Evaluate(withApproval);
        ReportingIntakePolicyEvaluation unsafeEvaluation = ReportingIntakePolicyEvaluator.Evaluate(withoutApproval);

        await AssertEvaluationAsync(safeEvaluation, true, "event_reporting_intake_protected_by_approval");
        await AssertEvaluationAsync(unsafeEvaluation, false, "event_reporting_intake_unsafe_publication_policy");
    }

    public static IEnumerable<(bool IntakeEnabled, bool RequireApproval, bool UserSubmissionEnabled,
        bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled, bool Allowed, string ReasonCode)> AllPolicyStates()
    {
        yield return (false, false, false, false, false, true, "event_reporting_intake_protected_by_closed_submissions");
        yield return (false, false, false, false, true, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, false, true, false, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, false, true, true, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, true, false, false, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, true, false, true, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, true, true, false, false, "event_reporting_intake_unsafe_publication_policy");
        yield return (false, false, true, true, true, false, "event_reporting_intake_unsafe_publication_policy");

        yield return (false, true, false, false, false, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, false, false, true, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, false, true, false, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, false, true, true, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, true, false, false, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, true, false, true, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, true, true, false, true, "event_reporting_intake_protected_by_approval");
        yield return (false, true, true, true, true, true, "event_reporting_intake_protected_by_approval");

        yield return (true, false, false, false, false, true, "event_reporting_intake_enabled");
        yield return (true, false, false, false, true, true, "event_reporting_intake_enabled");
        yield return (true, false, false, true, false, true, "event_reporting_intake_enabled");
        yield return (true, false, false, true, true, true, "event_reporting_intake_enabled");
        yield return (true, false, true, false, false, true, "event_reporting_intake_enabled");
        yield return (true, false, true, false, true, true, "event_reporting_intake_enabled");
        yield return (true, false, true, true, false, true, "event_reporting_intake_enabled");
        yield return (true, false, true, true, true, true, "event_reporting_intake_enabled");

        yield return (true, true, false, false, false, true, "event_reporting_intake_enabled");
        yield return (true, true, false, false, true, true, "event_reporting_intake_enabled");
        yield return (true, true, false, true, false, true, "event_reporting_intake_enabled");
        yield return (true, true, false, true, true, true, "event_reporting_intake_enabled");
        yield return (true, true, true, false, false, true, "event_reporting_intake_enabled");
        yield return (true, true, true, false, true, true, "event_reporting_intake_enabled");
        yield return (true, true, true, true, false, true, "event_reporting_intake_enabled");
        yield return (true, true, true, true, true, true, "event_reporting_intake_enabled");
    }

    public static IEnumerable<(bool RequireApproval, bool UserSubmissionEnabled,
        bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled)> NonIntakePolicyStates()
    {
        foreach (bool requireApproval in new[] { false, true })
            foreach (bool userSubmissionEnabled in new[] { false, true })
                foreach (bool organizationSubmissionEnabled in new[] { false, true })
                    foreach (bool groupSubmissionEnabled in new[] { false, true })
                        yield return (requireApproval, userSubmissionEnabled, organizationSubmissionEnabled, groupSubmissionEnabled);
    }

    public static IEnumerable<(bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
        bool GroupSubmissionEnabled, SubmissionPath PathToClose, bool ExpectedAllowedAfterClosing)> ClosingSubmissionPathTransitions()
    {
        foreach (var state in OpenSubmissionPolicyStates())
        {
            if (state.UserSubmissionEnabled)
                yield return (true, state.OrganizationSubmissionEnabled, state.GroupSubmissionEnabled,
                    SubmissionPath.User, !state.OrganizationSubmissionEnabled && !state.GroupSubmissionEnabled);
            if (state.OrganizationSubmissionEnabled)
                yield return (state.UserSubmissionEnabled, true, state.GroupSubmissionEnabled,
                    SubmissionPath.Organization, !state.UserSubmissionEnabled && !state.GroupSubmissionEnabled);
            if (state.GroupSubmissionEnabled)
                yield return (state.UserSubmissionEnabled, state.OrganizationSubmissionEnabled, true,
                    SubmissionPath.Group, !state.UserSubmissionEnabled && !state.OrganizationSubmissionEnabled);
        }
    }

    public static IEnumerable<(bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
        bool GroupSubmissionEnabled, SubmissionPath PathToOpen)> OpeningSubmissionPathTransitions()
    {
        foreach (bool userSubmissionEnabled in new[] { false, true })
            foreach (bool organizationSubmissionEnabled in new[] { false, true })
                foreach (bool groupSubmissionEnabled in new[] { false, true })
                {
                    if (!userSubmissionEnabled)
                        yield return (false, organizationSubmissionEnabled, groupSubmissionEnabled, SubmissionPath.User);
                    if (!organizationSubmissionEnabled)
                        yield return (userSubmissionEnabled, false, groupSubmissionEnabled, SubmissionPath.Organization);
                    if (!groupSubmissionEnabled)
                        yield return (userSubmissionEnabled, organizationSubmissionEnabled, false, SubmissionPath.Group);
                }
    }

    public static IEnumerable<(bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
        bool GroupSubmissionEnabled)> SubmissionPolicyStates()
    {
        yield return (false, false, false);
        foreach (var state in OpenSubmissionPolicyStates())
            yield return state;
    }

    public static IEnumerable<(bool UserSubmissionEnabled, bool OrganizationSubmissionEnabled,
        bool GroupSubmissionEnabled)> OpenSubmissionPolicyStates()
    {
        yield return (false, false, true);
        yield return (false, true, false);
        yield return (false, true, true);
        yield return (true, false, false);
        yield return (true, false, true);
        yield return (true, true, false);
        yield return (true, true, true);
    }

    private static ReportingIntakePolicyState CreateState(
        (bool IntakeEnabled, bool RequireApproval, bool UserSubmissionEnabled,
            bool OrganizationSubmissionEnabled, bool GroupSubmissionEnabled,
            bool Allowed, string ReasonCode) testCase) => new(
            testCase.IntakeEnabled,
            testCase.RequireApproval,
            testCase.UserSubmissionEnabled,
            testCase.OrganizationSubmissionEnabled,
            testCase.GroupSubmissionEnabled);

    private static ReportingIntakePolicyState ClosePath(ReportingIntakePolicyState state, SubmissionPath path) => path switch
    {
        SubmissionPath.User => state with { UserSubmissionEnabled = false },
        SubmissionPath.Organization => state with { OrganizationSubmissionEnabled = false },
        SubmissionPath.Group => state with { GroupSubmissionEnabled = false },
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
    };

    private static ReportingIntakePolicyState OpenPath(ReportingIntakePolicyState state, SubmissionPath path) => path switch
    {
        SubmissionPath.User => state with { UserSubmissionEnabled = true },
        SubmissionPath.Organization => state with { OrganizationSubmissionEnabled = true },
        SubmissionPath.Group => state with { GroupSubmissionEnabled = true },
        _ => throw new ArgumentOutOfRangeException(nameof(path), path, null)
    };

    private static async Task AssertEvaluationAsync(
        ReportingIntakePolicyEvaluation evaluation,
        bool expectedAllowed,
        string expectedReasonCode)
    {
        await Assert.That(evaluation.Allowed).IsEqualTo(expectedAllowed);
        await Assert.That(evaluation.ReasonCode).IsEqualTo(expectedReasonCode);
        await Assert.That(evaluation.Message).IsNotEmpty();
    }

    public enum SubmissionPath
    {
        User,
        Organization,
        Group
    }
}
