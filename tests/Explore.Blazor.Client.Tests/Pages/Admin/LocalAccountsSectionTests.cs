// ABOUTME: Exercises Local account UI through its real scoped service and generated HTTP clients.
// ABOUTME: Guards HAL authority, exact reset intent, safe recovery, and ephemeral credential handover.

using System.Net;
using System.Security.Cryptography;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Services.Accessibility;
using Explore.Blazor.Client.Tests.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class LocalAccountsSectionTests
{
    [Test]
    public async Task ReadyAccountsAndAdministratorClaimsCannotManufactureMissingActions()
    {
        using var fixture = new Fixture();
        fixture.Transport.ActionLinks = false;
        var cut = fixture.Render();

        await Assert.That(cut.FindAll($"[data-local-subject-id='{fixture.Transport.SubjectA:D}']").Count).IsEqualTo(1);

        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Contains("Create local account", StringComparison.Ordinal)
            || button.TextContent.Contains("Issue temporary credential", StringComparison.Ordinal)
            || button.TextContent.Trim() == "Reconcile")).IsFalse();
        await Assert.That(fixture.Transport.Requests.Any(request => request.Method == HttpMethod.Post)).IsFalse();
    }

    [Test]
    public async Task InvalidCreationHasLabelledConnectedErrorsAndDoesNotSendAnIssuance()
    {
        using var fixture = new Fixture();
        var cut = fixture.Render();
        await Button(cut, "Create local account").ClickAsync(new MouseEventArgs());
        var email = cut.Find("#local-account-email");
        await Assert.That(cut.FindAll("label").Any(label => label.GetAttribute("for") == email.Id
            && label.TextContent.Contains("Email", StringComparison.Ordinal))).IsTrue();

        await SubmitAsync(cut, "Submit local account");

        await Assert.That(cut.Find("#local-account-email").GetAttribute("aria-invalid")).IsEqualTo("true");
        string descriptionIds = cut.Find("#local-account-email").GetAttribute("aria-describedby") ?? string.Empty;
        await Assert.That(descriptionIds.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(id => cut.FindAll($"[id='{id}']").Any(element => !string.IsNullOrWhiteSpace(element.TextContent)))).IsTrue();
        await Assert.That(fixture.Transport.Requests.Any(request => request.Method == HttpMethod.Post)).IsFalse();
    }

    [Test]
    public async Task SuccessfulCreationDisplaysEncodedOneTimeCredentialThenDismissesIt()
    {
        using var fixture = new Fixture();
        string password = PasswordCanary();
        fixture.Transport.Override = (request, _) => Task.FromResult(fixture.Transport.Issue(
            request.Body!.Value.GetProperty("operationId").GetGuid(), fixture.Transport.SubjectA, password, HttpStatusCode.Created));
        var cut = fixture.Render();
        await OpenCreateAsync(cut);

        await SubmitAsync(cut, "Submit local account");

        await Assert.That(ContainsCredential(cut, password)).IsTrue();
        LocalIdentityUiTransport.Request submitted = fixture.Transport.Requests.Single(request => request.Method == HttpMethod.Post);
        await Assert.That(submitted.Uri.AbsolutePath).IsEqualTo(LocalIdentityUiTransport.IdentitiesPath);
        await Assert.That(submitted.Body!.Value.GetProperty("operationId").GetGuid() != Guid.Empty).IsTrue();
        await Assert.That(submitted.Body.Value.TryGetProperty("initialPassword", out _)).IsFalse();
        await Assert.That(cut.FindAll("script[data-credential-canary]").Count).IsEqualTo(0);
        await AssertCredentialNotBroadcastAsync(fixture, cut, password);

        await Button(cut, "Dismiss credential").ClickAsync(new MouseEventArgs());

        await Assert.That(ContainsCredential(cut, password)).IsFalse();
        await OpenCreateAsync(cut);
        await Assert.That(ContainsCredential(cut, password)).IsFalse();
    }

    [Test]
    public async Task LateResetForCancelledAccountCannotReplaceNewAccountsHandover()
    {
        using var fixture = new Fixture();
        string passwordA = PasswordCanary();
        string passwordB = PasswordCanary();
        var pendingA = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedA = new TaskCompletionSource<LocalIdentityUiTransport.Request>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Uri.AbsolutePath != fixture.Transport.ResetPath(fixture.Transport.SubjectA))
                return Task.FromResult(fixture.Transport.Issue(request.Body!.Value.GetProperty("operationId").GetGuid(), fixture.Transport.SubjectB, passwordB));
            startedA.SetResult(request);
            return pendingA.Task;
        };
        var cut = fixture.Render();
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "Account A supervised reset");
        Task requestA = SubmitAsync(cut, "Submit temporary credential");
        try
        {
            LocalIdentityUiTransport.Request submittedA = await startedA.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Button(cut, "Cancel reset").ClickAsync(new MouseEventArgs());
            await SelectResetAsync(cut, fixture.Transport.SubjectB, "Account B supervised reset");

            await SubmitAsync(cut, "Submit temporary credential");

            await Assert.That(ContainsCredential(cut, passwordB)).IsTrue();
            await Assert.That(ContainsOperation(cut, submittedA.Body!.Value.GetProperty("operationId").GetGuid())).IsTrue();
            await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsTrue();
            LocalIdentityUiTransport.Request submittedB = fixture.Transport.Requests.Last(request => request.Method == HttpMethod.Post);
            await Assert.That(submittedA.Body!.Value.GetProperty("expectedCurrentOperationId").GetGuid()).IsEqualTo(fixture.Transport.OperationA);
            await Assert.That(submittedA.Body.Value.GetProperty("expectedCurrentOperationConcurrencyStamp").GetGuid()).IsEqualTo(fixture.Transport.StampA);
            await Assert.That(submittedB.Body!.Value.GetProperty("expectedCurrentOperationId").GetGuid()).IsEqualTo(fixture.Transport.OperationB);
            await Assert.That(submittedB.Body.Value.GetProperty("expectedCurrentOperationConcurrencyStamp").GetGuid()).IsEqualTo(fixture.Transport.StampB);
            await Assert.That(submittedB.Body.Value.GetProperty("reason").GetString()).IsEqualTo("Account B supervised reset");
            await Assert.That(submittedA.Body.Value.GetProperty("operationId").GetGuid())
                .IsNotEqualTo(submittedB.Body.Value.GetProperty("operationId").GetGuid());
            using HttpResponseMessage lateResponse = fixture.Transport.Issue(
                submittedA.Body.Value.GetProperty("operationId").GetGuid(), fixture.Transport.SubjectA, passwordA);
            pendingA.SetResult(lateResponse);
            await requestA;

            await Assert.That(ContainsCredential(cut, passwordA)).IsFalse();
            await Assert.That(ContainsCredential(cut, passwordB)).IsTrue();
            await AssertCredentialNotBroadcastAsync(fixture, cut, passwordA);
            await AssertCredentialNotBroadcastAsync(fixture, cut, passwordB);
        }
        finally
        {
            pendingA.TrySetCanceled();
        }
    }

    [Test]
    public async Task DisposedSectionsLateIssuanceCannotPopulateNewSectionInSameScope()
    {
        using var fixture = new Fixture();
        string password = PasswordCanary();
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<LocalIdentityUiTransport.Request>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Override = async (request, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            started.SetResult(request);
            return await pending.Task;
        };
        var host = fixture.Context.Render<MudStack>(parameters => parameters.AddChildContent<LocalAccountsSection>());
        var original = host.FindComponent<LocalAccountsSection>();
        await OpenCreateAsync(original);
        Task submission = SubmitAsync(original, "Submit local account");
        try
        {
            var submitted = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Guid operationId = submitted.Body!.Value.GetProperty("operationId").GetGuid();

            host.Render(parameters => parameters.Add(component => component.ChildContent, (RenderFragment?)null));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var replacement = fixture.Render();
            using HttpResponseMessage lateResponse = fixture.Transport.Issue(
                operationId, fixture.Transport.SubjectA, password, HttpStatusCode.Created);
            pending.SetResult(lateResponse);
            await submission;

            await Assert.That(host.FindComponents<LocalAccountsSection>().Count).IsEqualTo(0);
            await Assert.That(ContainsCredential(replacement, password)).IsFalse();
            await AssertCredentialNotBroadcastAsync(fixture, replacement, password);
        }
        finally
        {
            pending.TrySetCanceled();
        }
    }

    [Test]
    public async Task AmbiguousCommittedCreationRecoversExactOperationWithoutIssuingAgain()
    {
        using var fixture = new Fixture();
        Guid committedOperationId = Guid.Empty;
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Post && request.Uri.AbsolutePath == LocalIdentityUiTransport.IdentitiesPath)
            {
                committedOperationId = request.Body!.Value.GetProperty("operationId").GetGuid();
                throw new HttpRequestException("Response acknowledgement was lost.");
            }
            bool reconcile = request.Method == HttpMethod.Post;
            return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(committedOperationId,
                fixture.Transport.SubjectA, reconcile ? LocalCredentialState.ChangeRequired : LocalCredentialState.ProvisioningPending,
                reconcile: !reconcile)));
        };
        var cut = fixture.Render();
        await OpenCreateAsync(cut);
        await SubmitAsync(cut, "Submit local account");
        await Assert.That(cut.Find("#local-operation-id").GetAttribute("value")).IsEqualTo(committedOperationId.ToString("D"));

        await SubmitAsync(cut, "Check operation");
        await Button(cut, "Reconcile").ClickAsync(new MouseEventArgs());

        await Assert.That(fixture.Transport.Requests.Where(request => request.Method == HttpMethod.Post
            && request.Uri.AbsolutePath == LocalIdentityUiTransport.IdentitiesPath).Count()).IsEqualTo(1);
        await Assert.That(fixture.Transport.Requests.Any(request => request.Method == HttpMethod.Get
            && request.Uri.AbsolutePath == LocalIdentityUiTransport.StatusPath(committedOperationId))).IsTrue();
        await Assert.That(fixture.Transport.Requests.Any(request => request.Method == HttpMethod.Post
            && request.Uri.AbsolutePath == LocalIdentityUiTransport.StatusPath(committedOperationId) + "/reconcile")).IsTrue();
        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Dismiss credential")).IsFalse();
    }

    [Test]
    public async Task ServerConflictUsesLocalizedSafeErrorWithoutEchoingProviderDetails()
    {
        using var fixture = new Fixture();
        string privateDetail = PasswordCanary();
        fixture.Context.Services.GetRequiredService<ITranslationService>().T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => "Localized " + (call.ArgAt<string?>(1) ?? call.ArgAt<string>(0)));
        fixture.Transport.Override = (_, _) => Task.FromResult(LocalIdentityUiTransport.Json(new
        {
            status = 409, title = "Conflict", detail = privateDetail
        }, HttpStatusCode.Conflict));
        var cut = fixture.Render();
        await OpenCreateAsync(cut);

        await SubmitAsync(cut, "Submit local account");

        await Assert.That(cut.Find("[role='alert']").TextContent.Contains("Localized ", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cut.Markup.Contains(privateDetail, StringComparison.Ordinal)).IsFalse();
        await AssertCredentialNotBroadcastAsync(fixture, cut, privateDetail);
    }

    [Test]
    [Arguments(HttpStatusCode.Forbidden)]
    [Arguments(HttpStatusCode.Conflict)]
    public async Task PostCommitFailureKeepsOriginalCreationRecoverable(HttpStatusCode status)
    {
        using var fixture = new Fixture();
        Guid committed = Guid.Empty;
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                committed = request.Body!.Value.GetProperty("operationId").GetGuid();
                return Task.FromResult(LocalIdentityUiTransport.Json(new { status = (int)status }, status));
            }
            return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(committed,
                fixture.Transport.SubjectA, LocalCredentialState.ChangeRequired, reconcile: false)));
        };
        var cut = fixture.Render();
        await OpenCreateAsync(cut);
        await SubmitAsync(cut, "Submit local account");

        await Assert.That(ContainsOperation(cut, committed)).IsTrue();
        await Assert.That(Button(cut, "Create local account").HasAttribute("disabled")).IsTrue();
        await SubmitAsync(cut, "Check operation");
        await Assert.That(Button(cut, "Create local account").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("[data-local-account-create]").Count).IsEqualTo(0);
    }

    [Test]
    public async Task UnrelatedLookupCannotResolveOriginalReset()
    {
        using var fixture = new Fixture();
        Guid unresolved = Guid.Empty;
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                unresolved = request.Body!.Value.GetProperty("operationId").GetGuid();
                throw new HttpRequestException("Acknowledgement lost.");
            }
            Guid id = Guid.Parse(request.Uri.Segments[^1]);
            return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(id,
                id == unresolved ? fixture.Transport.SubjectA : fixture.Transport.SubjectB,
                LocalCredentialState.ChangeRequired, reconcile: false)));
        };
        var cut = fixture.Render();
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "Lost reset acknowledgement");
        await SubmitAsync(cut, "Submit temporary credential");
        await cut.Find("#local-operation-id").ChangeAsync(new ChangeEventArgs { Value = fixture.Transport.OperationB.ToString("D") });
        await SubmitAsync(cut, "Check operation");

        await Assert.That(ContainsOperation(cut, unresolved)).IsTrue();
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsTrue();
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectB).HasAttribute("disabled")).IsFalse();
        await cut.Find("#local-operation-id").ChangeAsync(new ChangeEventArgs { Value = unresolved.ToString("D") });
        await SubmitAsync(cut, "Check operation");
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsFalse();
    }

    [Test]
    [Arguments("issuance")]
    [Arguments("status")]
    [Arguments("reconciliation")]
    public async Task SuccessfulOutcomeRefreshesCurrentPageAndNextResetMetadata(string outcome)
    {
        using var fixture = new Fixture();
        fixture.Transport.PageCount = 2;
        var cut = fixture.Render();
        await Button(cut, "Next page").ClickAsync(new MouseEventArgs());
        Guid currentOperation = Guid.CreateVersion7();
        Guid currentStamp = Guid.CreateVersion7();
        string password = PasswordCanary();
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Uri.AbsolutePath == fixture.Transport.ResetPath(fixture.Transport.SubjectA))
            {
                Guid previous = request.Body!.Value.GetProperty("expectedCurrentOperationId").GetGuid();
                Guid stamp = request.Body.Value.GetProperty("expectedCurrentOperationConcurrencyStamp").GetGuid();
                if (previous != fixture.Transport.OperationA || stamp != fixture.Transport.StampA)
                    return Task.FromResult(LocalIdentityUiTransport.Json(new { status = 409 }, HttpStatusCode.Conflict));
                Guid id = request.Body.Value.GetProperty("operationId").GetGuid();
                fixture.Transport.OperationA = id;
                fixture.Transport.StampA = currentStamp;
                fixture.Transport.StateA = LocalCredentialState.ChangeRequired;
                return Task.FromResult(fixture.Transport.Issue(id, fixture.Transport.SubjectA, password));
            }
            bool pending = outcome == "reconciliation" && request.Method == HttpMethod.Get;
            if (!pending)
            {
                fixture.Transport.OperationA = currentOperation;
                fixture.Transport.StampA = currentStamp;
                fixture.Transport.StateA = LocalCredentialState.ChangeRequired;
            }
            return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(currentOperation,
                fixture.Transport.SubjectA, pending ? LocalCredentialState.ProvisioningPending : LocalCredentialState.ChangeRequired,
                reconcile: pending)));
        };
        if (outcome == "issuance")
        {
            await SelectResetAsync(cut, fixture.Transport.SubjectA, "First reset");
            await SubmitAsync(cut, "Submit temporary credential");
            await Assert.That(ContainsCredential(cut, password)).IsTrue();
        }
        else
        {
            await cut.Find("#local-operation-id").ChangeAsync(new ChangeEventArgs { Value = currentOperation.ToString("D") });
            await SubmitAsync(cut, "Check operation");
            if (outcome == "reconciliation")
                await Button(cut, "Reconcile").ClickAsync(new MouseEventArgs());
        }

        await Assert.That(cut.Find($"[data-local-subject-id='{fixture.Transport.SubjectA:D}']").TextContent
            .Contains("Password replacement required", StringComparison.Ordinal)).IsTrue();
        await Assert.That(Button(cut, "Previous page").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Next page")).IsFalse();
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "Deliberate second reset");
        await SubmitAsync(cut, "Submit temporary credential");
        await Assert.That(ContainsCredential(cut, password)).IsTrue();
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(HttpStatusCode.ServiceUnavailable)]
    [Arguments(HttpStatusCode.Forbidden)]
    public async Task FailedPostResetRefreshDisablesStaleActionsUntilAuthoritativeRecovery(HttpStatusCode failure)
    {
        using var fixture = new Fixture();
        fixture.Transport.PageCount = 2;
        string password = PasswordCanary();
        var cut = fixture.Render();
        await Button(cut, "Next page").ClickAsync(new MouseEventArgs());
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(fixture.Transport.OperationA,
                    fixture.Transport.SubjectA, LocalCredentialState.ChangeRequired, reconcile: false)));
            var body = request.Body!.Value;
            if (body.GetProperty("expectedCurrentOperationId").GetGuid() != fixture.Transport.OperationA
                || body.GetProperty("expectedCurrentOperationConcurrencyStamp").GetGuid() != fixture.Transport.StampA)
                return Task.FromResult(LocalIdentityUiTransport.Json(new { status = 409 }, HttpStatusCode.Conflict));
            fixture.Transport.OperationA = body.GetProperty("operationId").GetGuid();
            fixture.Transport.StampA = Guid.CreateVersion7();
            fixture.Transport.StateA = LocalCredentialState.ChangeRequired;
            return Task.FromResult(fixture.Transport.Issue(fixture.Transport.OperationA, fixture.Transport.SubjectA, password));
        };
        fixture.Transport.ListOverride = (_, _) => Task.FromResult(LocalIdentityUiTransport.Json(new { status = (int)failure }, failure));
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "First supervised reset");
        await SubmitAsync(cut, "Submit temporary credential");

        await Assert.That(ContainsCredential(cut, password)).IsTrue();
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(1);
        await Assert.That(Button(cut, "Previous page").HasAttribute("disabled")).IsFalse();
        await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Next page")).IsFalse();
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsTrue();
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectB).HasAttribute("disabled")).IsTrue();

        // A known operation alone does not make the old list metadata safe to reuse.
        await SubmitAsync(cut, "Check operation");
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsTrue();
        fixture.Transport.ListOverride = null;
        await SubmitAsync(cut, "Check operation");
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled")).IsFalse();
        await Assert.That(ResetButton(cut, fixture.Transport.SubjectB).HasAttribute("disabled")).IsFalse();
        await Assert.That(Button(cut, "Previous page").HasAttribute("disabled")).IsFalse();
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "Reset using refreshed predecessor");
        await SubmitAsync(cut, "Submit temporary credential");
        await Assert.That(ContainsCredential(cut, password)).IsTrue();
        await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DismissalInvalidatesDelayedRefreshWithoutMakingStaleRowsActionable(bool recoverBeforeLateResponse)
    {
        using var fixture = new Fixture();
        fixture.Transport.PageCount = 2;
        string password = PasswordCanary();
        var cut = fixture.Render();
        await Button(cut, "Next page").ClickAsync(new MouseEventArgs());
        var started = new TaskCompletionSource<LocalIdentityUiTransport.Request>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Transport.Override = (request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                fixture.Transport.OperationA = request.Body!.Value.GetProperty("operationId").GetGuid();
                fixture.Transport.StampA = Guid.CreateVersion7();
                fixture.Transport.StateA = LocalCredentialState.ChangeRequired;
                return Task.FromResult(fixture.Transport.Issue(fixture.Transport.OperationA, fixture.Transport.SubjectA, password));
            }
            return Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(fixture.Transport.OperationA,
                fixture.Transport.SubjectA, LocalCredentialState.ChangeRequired, reconcile: false)));
        };
        fixture.Transport.ListOverride = async (request, cancellationToken) =>
        {
            using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
            started.SetResult(request);
            return await pending.Task;
        };
        await SelectResetAsync(cut, fixture.Transport.SubjectA, "Reset before dismissed refresh");
        Task submission = SubmitAsync(cut, "Submit temporary credential");
        try
        {
            var listRequest = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cut.InvokeAsync(async () =>
            {
                await Assert.That(ContainsCredential(cut, password)).IsTrue();
                await Button(cut, "Dismiss credential").ClickAsync(new MouseEventArgs());
            });
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.That(ContainsCredential(cut, password)).IsFalse();
            fixture.Transport.ListOverride = null;
            if (recoverBeforeLateResponse)
                await SubmitAsync(cut, "Check operation");

            // This obsolete result would remove both reset affordances and change pagination.
            fixture.Transport.ActionLinks = false;
            fixture.Transport.PageCount = 3;
            using HttpResponseMessage obsolete = fixture.Transport.DefaultResponse(listRequest);
            pending.SetResult(obsolete);
            await submission.WaitAsync(TimeSpan.FromSeconds(5));

            await Assert.That(ContainsCredential(cut, password)).IsFalse();
            await Assert.That(cut.FindAll("[role='alert']").Count).IsEqualTo(0);
            await Assert.That(Button(cut, "Previous page").HasAttribute("disabled")).IsFalse();
            await Assert.That(cut.FindAll("button").Any(button => button.TextContent.Trim() == "Next page")).IsFalse();
            await Assert.That(ResetButton(cut, fixture.Transport.SubjectA).HasAttribute("disabled"))
                .IsEqualTo(!recoverBeforeLateResponse);
        }
        finally
        {
            pending.TrySetCanceled();
        }
    }

    [Test]
    public async Task SuccessfulCreationRefreshesNewAccountWithoutClearingHandover()
    {
        using var fixture = new Fixture();
        fixture.Transport.IncludeSubjectB = false;
        string password = PasswordCanary();
        fixture.Transport.Override = (request, _) =>
        {
            fixture.Transport.IncludeSubjectB = true;
            return Task.FromResult(fixture.Transport.Issue(request.Body!.Value.GetProperty("operationId").GetGuid(),
                fixture.Transport.SubjectB, password, HttpStatusCode.Created));
        };
        var cut = fixture.Render();
        await Assert.That(cut.FindAll($"[data-local-subject-id='{fixture.Transport.SubjectB:D}']").Count).IsEqualTo(0);
        await OpenCreateAsync(cut);
        await SubmitAsync(cut, "Submit local account");

        await Assert.That(cut.FindAll($"[data-local-subject-id='{fixture.Transport.SubjectB:D}']").Count).IsEqualTo(1);
        await Assert.That(ContainsCredential(cut, password)).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellingFormFocusesSurvivingHeading(bool reset)
    {
        using var fixture = new Fixture();
        var cut = fixture.Render();
        if (reset) await SelectResetAsync(cut, fixture.Transport.SubjectA, "Cancelled reset");
        else await OpenCreateAsync(cut);
        await Button(cut, reset ? "Cancel reset" : "Cancel account creation").ClickAsync(new MouseEventArgs());

        string focusedId = (string)fixture.Context.JSInterop.Invocations.Last(invocation => invocation.Identifier == "setFocusById").Arguments[0]!;
        await Assert.That(cut.FindAll($"[id='{focusedId}']").Count).IsEqualTo(1);
        await Assert.That(cut.Find($"[id='{focusedId}']").TagName).IsEqualTo("H2");
        await Assert.That(cut.FindAll("[data-local-account-create],[data-local-credential-reset]").Count).IsEqualTo(0);
    }

    [Test]
    public async Task InvalidOperationHasUniqueConnectedErrorAndClearsOnValidLookup()
    {
        using var fixture = new Fixture();
        fixture.Transport.Override = (_, _) => Task.FromResult(LocalIdentityUiTransport.Json(fixture.Transport.Status(
            fixture.Transport.OperationA, fixture.Transport.SubjectA, LocalCredentialState.ChangeRequired, reconcile: false)));
        var cut = fixture.Render();
        await cut.Find("#local-operation-id").ChangeAsync(new ChangeEventArgs { Value = "invalid" });
        await SubmitAsync(cut, "Check operation");

        var input = cut.Find("#local-operation-id");
        await Assert.That(input.GetAttribute("aria-invalid")).IsEqualTo("true");
        string[] descriptions = input.GetAttribute("aria-describedby")!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        await Assert.That(descriptions.Any(id => id != "local-operation-help" && cut.FindAll($"[id='{id}']").Count == 1
            && !string.IsNullOrWhiteSpace(cut.Find($"[id='{id}']").TextContent))).IsTrue();
        await cut.Find("#local-operation-id").ChangeAsync(new ChangeEventArgs { Value = fixture.Transport.OperationA.ToString("D") });
        await SubmitAsync(cut, "Check operation");
        await Assert.That(cut.Find("#local-operation-id").GetAttribute("aria-invalid")).IsNotEqualTo("true");
        await Assert.That(ContainsOperation(cut, fixture.Transport.OperationA)).IsTrue();
    }

    private static async Task SubmitAsync(IRenderedComponent<LocalAccountsSection> cut, string label)
    {
        AngleSharp.Dom.IElement button = Button(cut, label);
        await Assert.That(button.GetAttribute("type")).IsEqualTo("submit");
        AngleSharp.Dom.IElement form = button.Closest("form")
            ?? throw new InvalidOperationException("A credential submit control must belong to a native form.");
        await form.SubmitAsync();
    }

    private static async Task OpenCreateAsync(IRenderedComponent<LocalAccountsSection> cut)
    {
        await Button(cut, "Create local account").ClickAsync(new MouseEventArgs());
        await cut.Find("#local-account-email").ChangeAsync(new ChangeEventArgs { Value = $"created-{Guid.CreateVersion7():N}@example.test" });
        await cut.Find("#local-account-first-name").ChangeAsync(new ChangeEventArgs { Value = "New" });
        await cut.Find("#local-account-last-name").ChangeAsync(new ChangeEventArgs { Value = "Account" });
    }
    private static async Task SelectResetAsync(IRenderedComponent<LocalAccountsSection> cut, Guid subjectId, string reason)
    {
        await ResetButton(cut, subjectId).ClickAsync(new MouseEventArgs());
        await cut.Find("#local-reset-reason").ChangeAsync(new ChangeEventArgs { Value = reason });
    }
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<LocalAccountsSection> cut, string label) =>
        cut.FindAll("button").Single(button => button.TextContent.Trim() == label
            || button.TextContent.Trim() == "Localized " + label || button.GetAttribute("aria-label") == label);
    private static AngleSharp.Dom.IElement ResetButton(IRenderedComponent<LocalAccountsSection> cut, Guid subjectId) =>
        cut.Find($"[data-local-subject-id='{subjectId:D}']").QuerySelectorAll("button")
            .Single(button => button.TextContent.Trim() == "Issue temporary credential");
    private static bool ContainsOperation(IRenderedComponent<LocalAccountsSection> cut, Guid operationId) =>
        cut.FindAll("input,code,span").Any(element => element.GetAttribute("value") == operationId.ToString("D")
            || element.TextContent == operationId.ToString("D"));
    private static bool ContainsCredential(IRenderedComponent<LocalAccountsSection> cut, string password) =>
        cut.FindAll("input,textarea,output,code").Any(element => element.GetAttribute("value") == password || element.TextContent == password);
    private static string PasswordCanary() => $"Aa1!<script data-credential-canary>{Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}</script>";
    private static async Task AssertCredentialNotBroadcastAsync(Fixture fixture, IRenderedComponent<LocalAccountsSection> cut, string password)
    {
        await Assert.That(cut.FindAll("[role='alert'],[role='status'],[aria-live]")
            .Any(element => element.TextContent.Contains(password, StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixture.Announcer.Messages.Any(message => message.Contains(password, StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixture.Transport.Requests.Any(request => request.Uri.OriginalString.Contains(password, StringComparison.Ordinal))).IsFalse();
        await Assert.That(fixture.Context.JSInterop.Invocations.Any(invocation => invocation.Arguments
            .Any(argument => argument?.ToString()?.Contains(password, StringComparison.Ordinal) == true))).IsFalse();
    }

    private sealed class Fixture : IDisposable
    {
        internal BlazorTestContext Context { get; } = new();
        internal LocalIdentityUiTransport Transport { get; } = new();
        internal RecordingAnnouncer Announcer { get; } = new();
        private readonly HttpClient _http;
        internal Fixture()
        {
            Context.SetAuthenticatedUser(Guid.CreateVersion7(), "Instance administrator", "admin@example.test");
            _http = Transport.CreateHttpClient();
            Context.Services.AddSingleton<IControlPlaneOverviewService>(Transport.CreateOverview(_http));
            Context.Services.AddScoped(_ => Transport.CreateService(_http));
            Context.Services.AddSingleton<IAccessibilityAnnouncerService>(Announcer);
            Context.Services.AddScoped<IAccessibilityFocusService, AccessibilityFocusService>();
            Context.JSInterop.SetupModule("/js/accessibility.js").SetupVoid("setFocusById", _ => true).SetVoidResult();
            Context.Render<MudPopoverProvider>();
        }
        internal IRenderedComponent<LocalAccountsSection> Render() => Context.Render<LocalAccountsSection>();
        public void Dispose() { Context.Dispose(); _http.Dispose(); Transport.Dispose(); }
    }
    private sealed class RecordingAnnouncer : IAccessibilityAnnouncerService
    {
        internal List<string> Messages { get; } = [];
        public Task AnnouncePoliteAsync(string message) { Messages.Add(message); return Task.CompletedTask; }
        public Task AnnounceAssertiveAsync(string message) { Messages.Add(message); return Task.CompletedTask; }
    }
}
