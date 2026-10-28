using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Models.Moderation;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Components.Moderation;

/// <summary>Stages a complete catalogue recipe as a new semantic revision, never mutating history.</summary>
public partial class PolicyAuthoringProfile : ComponentBase
{
    [Parameter, EditorRequired] public PolicyAuthoringState State { get; set; } = default!;
    [Parameter, EditorRequired] public HalResourceOfModerationReferenceCatalogueView Catalogue { get; set; } = default!;
    [Parameter, EditorRequired] public ModerationPolicyBounds Bounds { get; set; } = default!;
    [Parameter] public ModerationPolicyProfileDefinition? Original { get; set; }
    [Parameter] public bool ShowAdvanced { get; set; }
    [Parameter] public EventCallback<ModerationPolicyProfileDefinition> Saved { get; set; }
    [Parameter] public EventCallback Cancelled { get; set; }

    private ModerationPolicyProfileDefinition edit = new();
    private ModerationReferenceProfile? template;
    private string grade = "";
    private string reason = "";
    private string duration = "";
    private string approvals = "";
    private string months = "";
    private string? error;
    private bool initialized;
    private bool selected;

    protected override void OnParametersSet()
    {
        if (initialized) return;
        initialized = true;
        if (Original is { } original)
        {
            edit = PolicyAuthoringState.Copy(original);
            edit.Id = Guid.CreateVersion7();
            edit.Rule = State.Reference(original.Rule.Code,
                State.NextRevision(original.Rule.Code, State.Proposed.Profiles.Select(item => item.Rule)));
            template = Catalogue.Profiles.SingleOrDefault(item => item.Id == original.ReferenceProfile
                && item.Revision == original.ReferenceRevision);
            grade = original.Grade.ToString();
            reason = PolicyAuthoringState.Key(original.Reason);
            duration = original.Timing.Duration;
            approvals = original.RequiredApprovals.ToString(CultureInfo.InvariantCulture);
            months = original.ContestationCalendarMonths.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            edit = new() { Id = Guid.CreateVersion7(), Rule = State.Reference("", 1), Recognition = ModerationReferenceRecognition.Never };
            months = Bounds.MinimumContestationCalendarMonths.ToString(CultureInfo.InvariantCulture);
        }
    }

    private IEnumerable<ModerationPolicyReasonDefinition> CompatibleReasons =>
        State.Proposed.Reasons.Where(item => State.Proposed.Classes.Any(classification =>
            PolicyAuthoringState.Same(classification.Reference, item.Class)
            && classification.Ground == template?.Ground && classification.Basis == template?.Basis));

    private void ChangeTemplate(ChangeEventArgs args)
    {
        template = Catalogue.Profiles.SingleOrDefault(item => item.Id?.ToString() == args.Value?.ToString());
        if (template is null) return;
        edit.ReferenceProfile = template.Id!.Value;
        edit.ReferenceRevision = template.Revision!.Value;
        edit.Bundle = template.Bundle!.Value;
        edit.ProtectionPurpose = template.ProtectionPurpose;
        edit.Strategies = (template.Strategies ?? []).ToList();
        edit.Effects = (template.Effects ?? []).Select(PolicyAuthoringState.Copy).ToList();
        // These choices are intentionally empty until the administrator supplies a response and coverage.
        edit.ScopeKinds = [];
        edit.SubjectKinds = [];
        edit.Recognition = ModerationReferenceRecognition.Never;
        grade = reason = duration = approvals = "";
        selected = false;
        error = null;
    }

    private void ChangeGrade(ChangeEventArgs args)
    {
        grade = args.Value?.ToString() ?? "";
        var response = template?.Responses?.SingleOrDefault(item => item.Grade.ToString() == grade);
        if (response?.Timing.Kind is not { } kind || response.Timing.Duration is not { } interval) return;
        edit.Grade = response.Grade;
        edit.Timing = new() { Kind = kind, Duration = interval };
        duration = interval;
        approvals = MinimumApprovals(response).ToString(CultureInfo.InvariantCulture);
    }

    private int MinimumApprovals(ModerationReferenceResponse response) =>
        edit.Timing.Kind == ModerationReferenceTimingKind.ReviewedIndefinite
        || edit.ScopeKinds.Any(item => item is ModerationScopeKind.Solution or ModerationScopeKind.TrustDomain)
            && response.Grade != ModerationReferenceGrade.G0
        ? 2 : response.RequiredApprovals;

    private static void Toggle<T>(ICollection<T> values, T value, ChangeEventArgs args)
    {
        if (args.Value is true) { if (!values.Contains(value)) values.Add(value); }
        else values.Remove(value);
    }

    private bool NoDuration => edit.Timing.Kind is ModerationReferenceTimingKind.NoEnforcementInterval
        or ModerationReferenceTimingKind.FixedEventEnd or ModerationReferenceTimingKind.CurrentControlUntilQualifiedResolution;

    private async Task SaveAsync()
    {
        error = null;
        var response = template?.Responses?.SingleOrDefault(item => item.Grade.ToString() == grade);
        var chosenReason = CompatibleReasons.SingleOrDefault(item => PolicyAuthoringState.Key(item.Reference) == reason);
        var code = edit.Rule.Code;
        if (template is null || response is null || chosenReason is null
            || string.IsNullOrWhiteSpace(code) || code.Length > 100
            || code.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-')
            || Original is null && State.Proposed.Profiles.Any(item => item.Rule.Code == code)
            || edit.ScopeKinds.Count == 0 || edit.SubjectKinds.Count == 0
            || !int.TryParse(approvals, NumberStyles.None, CultureInfo.InvariantCulture, out var approvalCount)
            || approvalCount < MinimumApprovals(response) || approvalCount > 2
            || !int.TryParse(months, NumberStyles.None, CultureInfo.InvariantCulture, out var monthCount)
            || monthCount is < 0 or > 120
            || !TimeSpan.TryParseExact(duration, "c", CultureInfo.InvariantCulture, out var interval)
            || !TimeSpan.TryParseExact(response.Timing.Duration, "c", CultureInfo.InvariantCulture, out var ceiling)
            || (NoDuration ? interval != TimeSpan.Zero : interval <= TimeSpan.Zero || interval > ceiling))
        {
            error = "Choose a complete reference response, compatible reason, coverage and valid bounded procedure.";
            return;
        }
        if (State.Proposed.Profiles.Count >= 256)
        {
            error = "The native 256-definition limit is reached. Retained history cannot be deleted here.";
            return;
        }
        if (selected && (approvalCount < Bounds.MinimumApprovals || approvalCount > Bounds.MaximumApprovals
            || monthCount < Bounds.MinimumContestationCalendarMonths
            || !edit.ScopeKinds.All(Bounds.ScopeKinds.Contains) || !edit.SubjectKinds.All(Bounds.SubjectKinds.Contains)
            || !Bounds.TimingKinds.Contains(edit.Timing.Kind)
            || !WithinDisplayedDurationBound(interval)))
        {
            error = "The selected declaration exceeds the displayed authority ceiling or procedural floor. Narrow coverage or retain it unselected; native validation remains authoritative.";
            return;
        }
        edit.Rule = State.Reference(code, State.NextRevision(code, State.Proposed.Profiles.Select(item => item.Rule)));
        edit.Reason = PolicyAuthoringState.Copy(chosenReason.Reference);
        edit.RequiredApprovals = approvalCount;
        edit.ContestationCalendarMonths = monthCount;
        edit.Timing.Duration = interval.ToString("c", CultureInfo.InvariantCulture);
        if (selected) State.Select(edit.Rule, true);
        await Saved.InvokeAsync(PolicyAuthoringState.Copy(edit));
    }

    private bool WithinDisplayedDurationBound(TimeSpan interval)
    {
        var review = edit.Timing.Kind is ModerationReferenceTimingKind.ReviewedIndefinite
            or ModerationReferenceTimingKind.ObjectReviewWithinDay;
        if (!TimeSpan.TryParseExact(review ? Bounds.MaximumReviewDelay : Bounds.MaximumFiniteDuration,
            "c", CultureInfo.InvariantCulture, out var ceiling) || interval > ceiling) return false;
        return edit.Timing.Kind is not (ModerationReferenceTimingKind.ProtectiveDayWithReview
            or ModerationReferenceTimingKind.RecoveryOrDayReviewBoundary or ModerationReferenceTimingKind.EarlierOfDayOrEventEnd)
            || TimeSpan.TryParseExact(Bounds.MaximumReviewDelay, "c", CultureInfo.InvariantCulture, out var reviewCeiling)
                && interval <= reviewCeiling;
    }
}
