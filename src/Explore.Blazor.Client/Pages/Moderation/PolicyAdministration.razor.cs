using System.Globalization;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Models.Moderation;
using Explore.Blazor.Client.Services.Moderation;
using Microsoft.AspNetCore.Components;

namespace Explore.Blazor.Client.Pages.Moderation;

/// <summary>
/// Guided full-graph editing with append-only semantic revisions and HAL-owned write affordances.
/// Display mode is presentation only. No activation operation or authority inference exists here.
/// </summary>
public partial class PolicyAdministration : ComponentBase, IDisposable
{
    [Inject] private ModerationPolicyAdministrationService Administration { get; set; } = default!;
    [Inject] private IAccessibilityFocusService Focus { get; set; } = default!;

    private ModerationPolicyAdministrationScope scope = ModerationPolicyAdministrationScope.Tenant;
    private string displayMode = "Simple";
    private ModerationPolicyAdministrationView? baseline;
    private ModerationPolicyAdministrationView? observed;
    private PolicyAuthoringState? state;
    private ModerationPolicyAdministrationOperation? operation;
    private readonly List<ModerationPolicyAdministrationOperation> rejectedOperations = [];
    private ModerationPolicyAdministrationSubmissionResult? result;
    private ModerationPolicyAdministrationFailure failure;
    private ModerationPolicyAdministrationFailure submissionFailure;
    private CancellationTokenSource? cancellation;
    private bool busy;
    private bool disposed;
    private bool attempted;
    private bool confirmReplacement;
    private string deadline = "";
    private string status = "";
    private string? nextFocus;
    private bool classOpen;
    private bool reasonOpen;
    private bool profileOpen;
    private ModerationPolicyClassDefinition? originalClass;
    private ModerationPolicyReasonDefinition? originalReason;
    private ModerationPolicyProfileDefinition? originalProfile;
    private string classCode = "";
    private string classMeaning = "";
    private string classTemplate = "";
    private string reasonCode = "";
    private string reasonMeaning = "";
    private string reasonClass = "";
    private string? editError;
    private bool Advanced => displayMode == "Advanced";
    private bool EditingLocked => busy || operation is not null;
    private bool CanChangeScope => !busy && !HasPendingEdit && state?.Changed != true && operation is null;
    private bool HasPendingEdit => classOpen || reasonOpen || profileOpen;
    private bool CanResumeAuthoring => !busy && operation is not null && submissionFailure is
        ModerationPolicyAdministrationFailure.InvalidIntent or ModerationPolicyAdministrationFailure.SignInRequired
        or ModerationPolicyAdministrationFailure.Forbidden or ModerationPolicyAdministrationFailure.Conflict
        or ModerationPolicyAdministrationFailure.RateLimited or ModerationPolicyAdministrationFailure.ActionUnavailable;

    protected override Task OnInitializedAsync() => ReadAsync(false);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (nextFocus is not { } target || disposed) return;
        nextFocus = null;
        await Focus.FocusByIdAsync(target, true);
    }

    private async Task ChangeScopeAsync(ChangeEventArgs args)
    {
        if (!CanChangeScope || !Enum.TryParse<ModerationPolicyAdministrationScope>(args.Value?.ToString(), out var selected))
            return;
        scope = selected;
        baseline = observed = null;
        state = null;
        await ReadAsync(false);
    }

    private void BeginWork(string message)
    {
        busy = true;
        failure = ModerationPolicyAdministrationFailure.None;
        status = message;
        cancellation = new();
    }

    private void EndWork()
    {
        busy = false;
        cancellation?.Dispose();
        cancellation = null;
    }

    private async Task ReadAsync(bool refresh)
    {
        if (busy || disposed) return;
        BeginWork("Reading current policy and reference recipes.");
        try
        {
            var read = await Administration.ReadAsync(scope, cancellation!.Token);
            if (disposed) return;
            failure = read.Failure;
            if (read.View is null) { status = "Read failed. Local work is retained."; return; }
            if (refresh && baseline is not null)
            {
                observed = read.View;
                status = "Latest native observation acquired separately. Your displayed baseline and local work were not replaced.";
            }
            else
            {
                baseline = read.View;
                observed = null;
                state = read.View.IsUninitialized ? null : new(read.View.Current);
                status = read.View.IsUninitialized
                    ? "The installed slot is uninitialized. Its prospective identity is not reserved."
                    : "Retained policy loaded. Selected definitions are declarations, not current deployed support.";
            }
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            if (!disposed) status = "Read cancelled. Local work is retained.";
        }
        finally { EndWork(); }
    }

    private void CreatePolicy()
    {
        if (baseline?.IsUninitialized != true || EditingLocked) return;
        state = new(baseline.Current);
        state.MarkChanged();
        status = "New local proposal created without definitions, mode selection or support declarations. No native reservation or adoption occurred.";
    }

    private void ReplaceFromObservation()
    {
        if (!confirmReplacement || observed is null || busy) return;
        baseline = observed;
        observed = null;
        state = baseline.IsUninitialized ? null : new(baseline.Current);
        if (operation is not null) rejectedOperations.Add(operation);
        operation = null;
        result = null;
        attempted = false;
        confirmReplacement = false;
        classOpen = reasonOpen = profileOpen = false;
        failure = ModerationPolicyAdministrationFailure.None;
        submissionFailure = ModerationPolicyAdministrationFailure.None;
        status = "Started again from the deliberately selected latest observation.";
        nextFocus = "policy-scope";
    }

    private void ResumeAuthoring()
    {
        if (!CanResumeAuthoring || operation is null) return;
        rejectedOperations.Add(operation);
        operation = null;
        result = null;
        attempted = false;
        failure = ModerationPolicyAdministrationFailure.None;
        submissionFailure = ModerationPolicyAdministrationFailure.None;
        status = "Rejected operation identity retained. Editing resumed with all local definitions intact; another submission will be a new intent.";
        nextFocus = "add-class";
    }

    private void ToggleMode(ModerationNativeMode mode, ChangeEventArgs args)
    {
        if (state is null || EditingLocked) return;
        if (args.Value is true) { if (!state.Proposed.Modes.Contains(mode)) state.Proposed.Modes.Add(mode); }
        else state.Proposed.Modes.Remove(mode);
        state.MarkChanged();
    }

    private void AlignInventory()
    {
        if (state is null || baseline is null || EditingLocked) return;
        state.AlignInventory(baseline.Current);
        status = "Proposal catalogue and inventory coordinates explicitly aligned to the displayed installed observation. Retained definitions were not migrated.";
    }

    private void EditClass(ModerationPolicyClassDefinition? original)
    {
        originalClass = original;
        classCode = original?.Reference.Code ?? "";
        classMeaning = original?.Meaning ?? "";
        classTemplate = "";
        classOpen = true;
        reasonOpen = profileOpen = false;
        editError = null;
        nextFocus = "class-meaning";
    }

    private void SaveClass()
    {
        if (state is null || EditingLocked) return;
        var reference = baseline?.ReferenceCatalogue.Profiles.SingleOrDefault(item => item.Id?.ToString() == classTemplate);
        if (!ValidCode(classCode) || !ValidMeaning(classMeaning) || state.Proposed.Classes.Count >= 256
            || originalClass is null && (reference?.Ground is null || reference.Basis is null
                || state.Proposed.Classes.Any(item => item.Reference.Code == classCode)))
        {
            editError = "Supply a unique rule code, a trimmed meaning of 1 to 4000 characters and an installed ground/basis recipe.";
            return;
        }
        state.Proposed.Classes.Add(new()
        {
            Reference = state.Reference(classCode, state.NextRevision(classCode, state.Proposed.Classes.Select(item => item.Reference))),
            Meaning = classMeaning, Ground = originalClass?.Ground ?? reference!.Ground!.Value,
            Basis = originalClass?.Basis ?? reference!.Basis!.Value
        });
        state.MarkChanged();
        classOpen = false;
        nextFocus = "add-class";
        status = "Class revision appended; all original meanings are retained.";
    }

    private void EditReason(ModerationPolicyReasonDefinition? original)
    {
        originalReason = original;
        reasonCode = original?.Reference.Code ?? "";
        reasonMeaning = original?.Meaning ?? "";
        reasonClass = original is null ? "" : PolicyAuthoringState.Key(original.Class);
        reasonOpen = true;
        classOpen = profileOpen = false;
        editError = null;
        nextFocus = "reason-meaning";
    }

    private IEnumerable<ModerationPolicyClassDefinition> ReasonClasses =>
        state?.Proposed.Classes.Where(item => originalReason is null
            || item.Reference.OwnerId == originalReason.Class.OwnerId && item.Reference.Code == originalReason.Class.Code) ?? [];

    private void SaveReason()
    {
        if (state is null || EditingLocked) return;
        var classification = ReasonClasses.SingleOrDefault(item => PolicyAuthoringState.Key(item.Reference) == reasonClass);
        if (!ValidCode(reasonCode) || !ValidMeaning(reasonMeaning) || classification is null
            || state.Proposed.Reasons.Count >= 256
            || originalReason is null && state.Proposed.Reasons.Any(item => item.Reference.Code == reasonCode))
        {
            editError = "Supply a unique rule code, a trimmed meaning of 1 to 4000 characters and a compatible class revision.";
            return;
        }
        state.Proposed.Reasons.Add(new()
        {
            Reference = state.Reference(reasonCode, state.NextRevision(reasonCode, state.Proposed.Reasons.Select(item => item.Reference))),
            Meaning = reasonMeaning, Class = PolicyAuthoringState.Copy(classification.Reference)
        });
        state.MarkChanged();
        reasonOpen = false;
        nextFocus = "add-reason";
        status = "Reason revision appended; all original class links and meanings are retained.";
    }

    private void EditProfile(ModerationPolicyProfileDefinition? original)
    {
        originalProfile = original;
        profileOpen = true;
        classOpen = reasonOpen = false;
        nextFocus = "profile-code";
    }

    private void SaveProfile(ModerationPolicyProfileDefinition profile)
    {
        if (state is null || EditingLocked) return;
        state.Proposed.Profiles.Add(profile);
        state.MarkChanged();
        profileOpen = false;
        nextFocus = "add-profile";
        status = "Complete profile revision appended. Existing definitions and declarations stay unchanged unless explicitly selected.";
    }

    private void CancelEdit()
    {
        nextFocus = classOpen ? "add-class" : reasonOpen ? "add-reason" : "add-profile";
        classOpen = reasonOpen = profileOpen = false;
        editError = null;
    }

    private static bool ValidCode(string code) => !string.IsNullOrEmpty(code) && code.Length <= 100
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool ValidMeaning(string text) => !string.IsNullOrWhiteSpace(text)
        && text.Length <= 4000 && text == text.Trim()
        && !text.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t');

    private async Task PublishAsync(bool preparation)
    {
        if (EditingLocked || state is null || baseline is null || HasPendingEdit
            || (preparation ? !baseline.CanPreparePublication : !baseline.CanPublishDraft)) return;
        if (state.Proposed.Classes.Count == 0 || state.Proposed.Reasons.Count == 0
            || state.Proposed.Profiles.Count == 0 || state.Proposed.Modes.Count == 0
            || preparation && state.Proposed.SupportedProfiles.Count == 0)
        {
            failure = ModerationPolicyAdministrationFailure.InvalidIntent;
            status = "Complete the classes, reasons, profiles and native mode declaration. Preparation also requires a selected profile.";
            return;
        }
        DateTimeOffset validUntil = default;
        if (preparation && (!DateTimeOffset.TryParse(deadline, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out validUntil)
            || !deadline.EndsWith('Z') && !HasExplicitOffset(deadline)))
        {
            failure = ModerationPolicyAdministrationFailure.InvalidIntent;
            status = "Supply a candidate deadline with an explicit UTC Z or numeric offset. Native preparation validates its lifetime.";
            return;
        }
        BeginWork("Freezing intent and reacquiring current native write coordinates.");
        attempted = false;
        result = null;
        submissionFailure = ModerationPolicyAdministrationFailure.None;
        var displayed = baseline.Current;
        var proposed = PolicyAuthoringState.Copy(state.Proposed);
        proposed.PublicationId = Guid.CreateVersion7();
        proposed.RequestedState = preparation ? ModerationPolicyRequestedState.Active : ModerationPolicyRequestedState.Draft;
        try
        {
            var prepared = preparation
                ? await Administration.CreatePreparationAsync(scope, proposed, Guid.CreateVersion7(), validUntil, cancellation!.Token)
                : await Administration.CreateDraftAsync(scope, proposed, Guid.CreateVersion7(), cancellation!.Token);
            if (disposed) return;
            failure = prepared.Failure;
            if (prepared.Operation is not { } frozen)
            {
                status = "Operation preparation failed. Local work is retained and no POST was sent.";
                return;
            }
            // CreateAsync reacquires coordinates. It must not silently rebase this displayed proposal.
            if (frozen.PolicyId != displayed.PolicyId || frozen.ExpectedRevision != displayed.ExpectedRevision)
            {
                failure = ModerationPolicyAdministrationFailure.Conflict;
                status = "Current policy identity or revision changed. No POST was sent. Read the latest observation and deliberately reconcile your retained work.";
                return;
            }
            operation = frozen;
            await SubmitFrozenAsync();
        }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            if (!disposed) CancelledOperation();
        }
        finally { EndWork(); }
    }

    private static bool HasExplicitOffset(string value) => value.Length >= 6
        && value[^3] == ':' && value[^6] is '+' or '-';

    private async Task SubmitFrozenAsync()
    {
        if (operation is null) return;
        attempted = true;
        status = "Submitting one exact native operation. Do not infer adoption from a candidate.";
        StateHasChanged();
        result = await Administration.SubmitAsync(operation, cancellation!.Token);
        if (disposed) return;
        failure = result.Failure;
        submissionFailure = result.Failure;
        status = result.Candidate is not null
            ? "Candidate prepared for measurement only. Nothing was adopted or activated."
            : result.Publication is not null
                ? "Draft publication observed at the publication instant. No active readiness or enduring grant is asserted."
                : "Submission did not return an accepted receipt. All edits and the exact operation are retained; there is no automatic retry.";
    }

    private async Task RetryAsync()
    {
        if (busy || disposed || operation is null || !attempted || result?.Failure == ModerationPolicyAdministrationFailure.None)
            return;
        BeginWork("Deliberately recovering the exact original operation receipt.");
        try { await SubmitFrozenAsync(); }
        catch (OperationCanceledException) when (cancellation?.IsCancellationRequested == true)
        {
            if (!disposed) CancelledOperation();
        }
        finally { EndWork(); }
    }

    private void CancelledOperation()
    {
        failure = attempted ? ModerationPolicyAdministrationFailure.Uncertain : ModerationPolicyAdministrationFailure.Unavailable;
        if (attempted) submissionFailure = failure;
        status = attempted
            ? "Cancelled after POST may have committed. Recover only the exact retained operation deliberately."
            : "Cancelled before POST. Local work is retained.";
    }

    private static string Explain(ModerationPolicyAdministrationFailure value) => value switch
    {
        ModerationPolicyAdministrationFailure.SignInRequired => "Sign-in is required. Retain your work and restore the session.",
        ModerationPolicyAdministrationFailure.Forbidden => "Current native authority denied this action.",
        ModerationPolicyAdministrationFailure.Conflict => "Current policy or authority coordinates conflict with this intent.",
        ModerationPolicyAdministrationFailure.InvalidIntent => "The proposed intent is invalid; native validation remains authoritative.",
        ModerationPolicyAdministrationFailure.RateLimited => "The request was rate limited. No automatic retry occurs.",
        ModerationPolicyAdministrationFailure.Unavailable => "The native service is unavailable.",
        ModerationPolicyAdministrationFailure.InvalidRepresentation => "The response is incomplete or inconsistent; do not infer success.",
        ModerationPolicyAdministrationFailure.ActionUnavailable => "The current HAL representation does not authorize this transport action.",
        ModerationPolicyAdministrationFailure.Uncertain => "The response was lost or cancelled after submission. Commit status is uncertain.",
        _ => ""
    };

    public void Dispose()
    {
        disposed = true;
        cancellation?.Cancel();
    }
}
