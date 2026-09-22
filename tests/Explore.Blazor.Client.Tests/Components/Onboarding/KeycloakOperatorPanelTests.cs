using AngleSharp.Dom;
using Explore.Blazor.Client.Components.Onboarding;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Onboarding;

public sealed class KeycloakOperatorPanelTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IInstanceOnboardingService _service =
        Substitute.For<IInstanceOnboardingService>();

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task ConnectionActionsRenderOnlyFromHalAffordances()
    {
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfKeycloakConnectionDto
            {
                Status = "resolved",
                Authority = "https://identity.example.test",
                Realm = "operators",
                ClientId = "event-bff",
                Available = true,
                CredentialOwnership = "deployment-managed",
                CredentialStatus = "resolved",
                RequiresCoordinatedRestart = true,
                OperatorGuidance =
                    "rotate_in_deployment_authority_restart_and_reinspect",
                _links = new Dictionary<string, HalLink>
                {
                    ["inspect"] = new()
                }
            });

        var cut = _context.Render<KeycloakOperatorPanel>(
            parameters => parameters
                .Add(component => component.Service, _service));

        cut.WaitForAssertion(() =>
        {
            Assert.That(
                    cut.FindAll(
                        "[data-testid=keycloak-inspect-action]"))
                .Count().IsEqualTo(1);
            Assert.That(
                    cut.FindAll(
                        "[data-testid=keycloak-plan-action]"))
                .IsEmpty();
        });

        await Assert.That(cut.Markup)
            .DoesNotContain("administratorPassword");
        await Assert.That(cut.Markup)
            .Contains("Keycloak operator workflow");
        await Assert.That(
                cut.Find(
                    "section[aria-labelledby=keycloak-operator-heading]"))
            .IsNotNull();
        await Assert.That(
                cut.Find(
                    "[data-testid=keycloak-admin-username]")
                    .GetAttribute("autocomplete"))
            .IsEqualTo("username");
        await Assert.That(
                cut.Find(
                    "[data-testid=keycloak-admin-password]")
                    .GetAttribute("autocomplete"))
            .IsEqualTo("current-password");
        await Assert.That(
                cut.Find(
                    "[data-testid=keycloak-inspect-action]")
                    .LocalName)
            .IsEqualTo("button");
    }

    [Test]
    public async Task HeadingUsesTranslationServiceWithFallbackElsewhere()
    {
        var translations =
            Substitute.For<ITranslationService>();
        translations.T(
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(call =>
                call.ArgAt<string>(0)
                    == "ui.keycloak.operator.heading"
                    ? "Keycloak-Betreiberablauf"
                    : call.ArgAt<string>(1));
        _context.Services.AddSingleton(translations);
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection());

        var cut = Render();
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup)
                .Contains("Keycloak-Betreiberablauf");
            Assert.That(cut.Markup)
                .Contains("User and account recovery remains Keycloak-owned");
        });
    }

    [Test]
    public async Task InspectionClearsCredentialsAndUsesInspectionHalForPlan()
    {
        const string username = "operator-admin";
        const string password = "credential-canary";
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection("inspect"));
        _service.InspectKeycloakAsync(
                Arg.Any<KeycloakInspectionCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns(Inspection("plan"));
        var cut = Render();
        cut.WaitForElement(
            "[data-testid=keycloak-inspect-action]");
        await SetCredentialsAsync(cut, username, password);

        await cut.Find(
                "[data-testid=keycloak-inspect-action]")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForElement(
            "[data-testid=keycloak-plan-action]");
        await _service.Received(1).InspectKeycloakAsync(
            Arg.Is<KeycloakInspectionCredentials>(input =>
                input.AdministratorUsername == username
                && input.AdministratorPassword == password),
            Arg.Any<CancellationToken>());
        await Assert.That(CredentialValues(cut))
            .IsEquivalentTo([string.Empty, string.Empty]);
        await Assert.That(cut.Markup).DoesNotContain(password);
    }

    [Test]
    public async Task ApplyRequiresConfirmationAndUnknownOutcomeOffersReconcile()
    {
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection("inspect"));
        _service.InspectKeycloakAsync(
                Arg.Any<KeycloakInspectionCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns(Inspection("plan"));
        _service.PlanKeycloakOperationAsync(
                Arg.Any<KeycloakOperationPlanInput>(),
                Arg.Any<CancellationToken>())
            .Returns(Operation(
                "Previewed",
                links: ["apply", "cancel"]));
        _service.ApplyKeycloakOperationAsync(
                Arg.Any<Guid>(),
                Arg.Any<KeycloakOperationCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns(Operation(
                "OutcomeUnknown",
                outcomes:
                [
                    new KeycloakStepOutcomeDto
                    {
                        StepId = "mapper:audience",
                        Outcome = "OutcomeUnknown"
                    }
                ],
                links: ["reconcile"]));
        var cut = Render();
        cut.WaitForElement(
            "[data-testid=keycloak-inspect-action]");

        await SetCredentialsAsync(cut, "admin", "inspect-secret");
        await cut.Find(
                "[data-testid=keycloak-inspect-action]")
            .ClickAsync(new MouseEventArgs());
        cut.WaitForElement(
            "[data-testid=keycloak-plan-action]");

        await SetCredentialsAsync(cut, "admin", "plan-secret");
        await cut.Find("[data-testid=keycloak-plan-action]")
            .ClickAsync(new MouseEventArgs());
        IElement apply = cut.WaitForElement(
            "[data-testid=keycloak-apply-action]");
        await Assert.That(apply.HasAttribute("disabled")).IsTrue();

        await SetCredentialsAsync(cut, "admin", "apply-secret");
        await SetFieldAsync(
            cut,
            "Type APPLY to confirm",
            "APPLY");
        apply = cut.Find(
            "[data-testid=keycloak-apply-action]");
        await Assert.That(apply.HasAttribute("disabled")).IsFalse();
        await apply.ClickAsync(new MouseEventArgs());

        cut.WaitForElement(
            "[data-testid=keycloak-reconcile-action]");
        await Assert.That(cut.FindAll(
                "[data-testid=keycloak-apply-action]"))
            .IsEmpty();
        await Assert.That(cut.FindAll(
                "[data-testid=keycloak-cancel-action]"))
            .IsEmpty();
        await Assert.That(cut.FindAll(
                "[data-testid=keycloak-outcome-unknown-guidance]"))
            .Count().IsEqualTo(1);
        await Assert.That(cut.Markup)
            .Contains("Do not apply again");
        await Assert.That(CredentialValues(cut))
            .IsEquivalentTo([string.Empty, string.Empty]);
        await Assert.That(cut.Markup)
            .DoesNotContain("apply-secret");
    }

    [Test]
    public async Task FailedInspectionClearsCredentialsAndReportsAccessibleError()
    {
        const string username = "failure-admin";
        const string password = "failure-secret-canary";
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection("inspect"));
        _service.InspectKeycloakAsync(
                Arg.Any<KeycloakInspectionCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfKeycloakInspectionDto>>(_ =>
                throw new HttpRequestException(
                    "bounded-provider-failure"));
        var cut = Render();
        cut.WaitForElement(
            "[data-testid=keycloak-inspect-action]");
        await SetCredentialsAsync(cut, username, password);

        await cut.Find(
                "[data-testid=keycloak-inspect-action]")
            .ClickAsync(new MouseEventArgs());

        IElement error = cut.WaitForElement(
            "[data-testid=keycloak-operator-error]");
        await Assert.That(error.GetAttribute("role"))
            .IsEqualTo("alert");
        await Assert.That(CredentialValues(cut))
            .IsEquivalentTo([string.Empty, string.Empty]);
        await Assert.That(cut.Markup).DoesNotContain(username);
        await Assert.That(cut.Markup).DoesNotContain(password);
        await Assert.That(cut.Markup)
            .DoesNotContain("bounded-provider-failure");
    }

    [Test]
    public async Task AmbiguousApplyFailureSuppressesReplayAndReloadsReceipt()
    {
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection("inspect"));
        _service.InspectKeycloakAsync(
                Arg.Any<KeycloakInspectionCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns(Inspection("plan"));
        HalResourceOfKeycloakOperationDto preview =
            Operation(
                "Previewed",
                links: ["apply", "cancel", "self"]);
        _service.PlanKeycloakOperationAsync(
                Arg.Any<KeycloakOperationPlanInput>(),
                Arg.Any<CancellationToken>())
            .Returns(preview);
        _service.ApplyKeycloakOperationAsync(
                Arg.Any<Guid>(),
                Arg.Any<KeycloakOperationCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<HalResourceOfKeycloakOperationDto>>(_ =>
                throw new HttpRequestException(
                    "lost-apply-response"));
        _service.GetKeycloakOperationAsync(
                preview.Id,
                Arg.Any<CancellationToken>())
            .Returns(preview);
        var cut = Render();
        cut.WaitForElement(
            "[data-testid=keycloak-inspect-action]");
        await SetCredentialsAsync(cut, "admin", "inspect-secret");
        await cut.Find(
                "[data-testid=keycloak-inspect-action]")
            .ClickAsync(new MouseEventArgs());
        await SetCredentialsAsync(cut, "admin", "plan-secret");
        await cut.Find(
                "[data-testid=keycloak-plan-action]")
            .ClickAsync(new MouseEventArgs());
        await SetCredentialsAsync(cut, "admin", "apply-secret");
        await SetFieldAsync(
            cut,
            "Type APPLY to confirm",
            "APPLY");

        await cut.Find(
                "[data-testid=keycloak-apply-action]")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.FindAll(
                    "[data-testid=keycloak-apply-action]"))
                .IsEmpty();
            Assert.That(cut.FindAll(
                    "[data-testid=keycloak-refresh-receipt]"))
                .HasCount().EqualTo(1);
            Assert.That(cut.Markup)
                .Contains("Do not apply again");
            Assert.That(cut.Markup)
                .DoesNotContain("lost-apply-response");
        });
        await _service.Received(1)
            .GetKeycloakOperationAsync(
                preview.Id,
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task PartiallyAppliedReceiptShowsNonReplayGuidance()
    {
        _service.GetKeycloakConnectionAsync(
                Arg.Any<CancellationToken>())
            .Returns(Connection("inspect"));
        _service.InspectKeycloakAsync(
                Arg.Any<KeycloakInspectionCredentials>(),
                Arg.Any<CancellationToken>())
            .Returns(Inspection("plan"));
        _service.PlanKeycloakOperationAsync(
                Arg.Any<KeycloakOperationPlanInput>(),
                Arg.Any<CancellationToken>())
            .Returns(Operation(
                "PartiallyApplied",
                outcomes:
                [
                    new KeycloakStepOutcomeDto
                    {
                        StepId = "mapper:audience",
                        Outcome = "Applied"
                    }
                ],
                links: ["self"]));
        var cut = Render();
        cut.WaitForElement(
            "[data-testid=keycloak-inspect-action]");
        await SetCredentialsAsync(cut, "admin", "inspect-secret");
        await cut.Find(
                "[data-testid=keycloak-inspect-action]")
            .ClickAsync(new MouseEventArgs());
        await SetCredentialsAsync(cut, "admin", "plan-secret");

        await cut.Find(
                "[data-testid=keycloak-plan-action]")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForElement(
            "[data-testid=keycloak-partial-outcome-guidance]");
        MudChip<string> stateChip = cut
            .FindComponents<MudChip<string>>()
            .Single(component =>
                component.Instance.ChildContent is not null)
            .Instance;
        await Assert.That(stateChip.Color)
            .IsEqualTo(Color.Warning);
        await Assert.That(cut.Markup)
            .Contains("Do not replay it");
    }

    private IRenderedComponent<KeycloakOperatorPanel> Render() =>
        _context.Render<KeycloakOperatorPanel>(
            parameters => parameters
                .Add(component => component.Service, _service));

    private static async Task SetCredentialsAsync(
        IRenderedComponent<KeycloakOperatorPanel> cut,
        string username,
        string password)
    {
        await SetFieldAsync(
            cut,
            "Administrator username",
            username);
        await SetFieldAsync(
            cut,
            "Administrator password",
            password);
    }

    private static Task SetFieldAsync(
        IRenderedComponent<KeycloakOperatorPanel> cut,
        string label,
        string value)
    {
        MudTextField<string> field = cut
            .FindComponents<MudTextField<string>>()
            .Single(component =>
                string.Equals(
                    component.Instance.Label,
                    label,
                    StringComparison.Ordinal))
            .Instance;
        return cut.InvokeAsync(() =>
            field.ValueChanged.InvokeAsync(value));
    }

    private static IReadOnlyList<string?> CredentialValues(
        IRenderedComponent<KeycloakOperatorPanel> cut) =>
        cut.FindComponents<MudTextField<string>>()
            .Where(component =>
                component.Instance.Label is
                    "Administrator username"
                    or "Administrator password")
            .Select(component => component.Instance.Value)
            .ToArray();

    private static HalResourceOfKeycloakConnectionDto Connection(
        params string[] links) =>
        new()
        {
            Status = "resolved",
            Authority = "https://identity.example.test",
            Realm = "operators",
            ClientId = "event-bff",
            Available = true,
            CredentialOwnership = "deployment-managed",
            CredentialStatus = "resolved",
            RequiresCoordinatedRestart = true,
            OperatorGuidance =
                "rotate_in_deployment_authority_restart_and_reinspect",
            _links = Links(links)
        };

    private static HalResourceOfKeycloakInspectionDto Inspection(
        params string[] links) =>
        new()
        {
            Status = "healthy",
            ReasonCode = null,
            Authority = "https://identity.example.test",
            Realm = "operators",
            ClientId = "event-bff",
            Findings = [],
            _links = Links(links)
        };

    private static HalResourceOfKeycloakOperationDto Operation(
        string state,
        IReadOnlyList<KeycloakStepOutcomeDto>? outcomes = null,
        params string[] links) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            State = state,
            Digest = "credential-free-digest",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(15),
            SettledAtUtc = null,
            IsCancellationRequested = false,
            Steps = ["mapper:audience"],
            Outcomes = outcomes?.ToArray() ?? [],
            _links = Links(links)
        };

    private static Dictionary<string, HalLink> Links(
        IEnumerable<string> relations) =>
        relations.ToDictionary(
            relation => relation,
            _ => new HalLink(),
            StringComparer.Ordinal);
}
