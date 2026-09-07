using Bunit;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Registration.FormRenderer;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.AspNetCore.Components.Web;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class RegistrationFormRendererTests : IDisposable
{
    private readonly BlazorTestContext _context = new();
    private readonly IAccessibilityAnnouncerService _announcer;

    public RegistrationFormRendererTests()
    {
        _announcer = _context.AddMockService<IAccessibilityAnnouncerService>();
        _context.AddMockService<IAccessibilityFocusService>();
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task RendersAllSeventeenPortableFieldTypesWithNativeSemantics()
    {
        RegistrationFieldView[] fields = PortableFields();
        var consentText = "I agree that ISLAMU may use this answer to arrange accessibility support.";

        var cut = Render(fields);

        string[] expected =
        [
            "SHORT_TEXT", "LONG_TEXT", "INTEGER", "DECIMAL", "BOOLEAN", "DATE", "TIME", "INSTANT", "EMAIL",
            "PHONE", "URL", "COUNTRY_CODE", "LANGUAGE_TAG", "SINGLE_CHOICE", "MULTIPLE_CHOICE", "RATING", "CONSENT"
        ];
        foreach (string code in expected)
            await Assert.That(cut.FindAll($"[data-field-type='{code}']").Count).IsEqualTo(1);

        await Assert.That(cut.Find("[data-field-type='EMAIL'] input").GetAttribute("type")).IsEqualTo("email");
        await Assert.That(cut.Find("[data-field-type='PHONE'] input").GetAttribute("type")).IsEqualTo("tel");
        await Assert.That(cut.Find("[data-field-type='URL'] input").GetAttribute("type")).IsEqualTo("url");
        await Assert.That(cut.Find("[data-field-type='CONSENT']").TextContent).Contains(consentText);
    }

    [Test]
    public async Task ConditionToggleShowsTargetAndClearsItWhenHiddenAgain()
    {
        RegistrationFieldView source = Field("BOOLEAN", 1);
        RegistrationFieldView target = Field("SHORT_TEXT", 2);
        var rule = new RegistrationRuleView(1, target.Namespace, target.Key, 1,
            new Condition
            {
                Operator = "equals",
                FieldNamespace = source.Namespace,
                FieldKey = source.Key,
                Value = new Value { Type = "boolean", BooleanValue = true }
            });
        RegistrationFormAnswerState state = new();
        var cut = Render([source, target], [rule], state);

        await Assert.That(cut.FindAll($"[data-field-key='{target.Namespace}.{target.Key}']")).IsEmpty();
        await cut.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = true });
        await Assert.That(cut.FindAll($"[data-field-key='{target.Namespace}.{target.Key}']").Count).IsEqualTo(1);
        await cut.Find($"[data-field-key='{target.Namespace}.{target.Key}'] input").InputAsync(new ChangeEventArgs { Value = "temporary" });
        await cut.Find("input[type='checkbox']").ChangeAsync(new ChangeEventArgs { Value = false });
        await Assert.That(state.Get(target.Id)).IsNull();
    }

    [Test]
    public async Task SkipAffordanceComesOnlyFromHalAndCompletesWithoutErrorStyling()
    {
        int skipped = 0;
        var links = new Dictionary<string, HalLink> { ["skip"] = new() { Href = "/skip", Method = "POST" } };
        var cut = Render([Field("SHORT_TEXT", 1)], links: links, onSkip: () => { skipped++; return Task.CompletedTask; });

        cut.FindAll("button").Single(button => button.TextContent.Contains("Skip and continue", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.That(skipped).IsEqualTo(1));
        await Assert.That(cut.FindAll("[role='alert']")).IsEmpty();
        await Assert.That(cut.Markup).Contains("Optional");
    }

    [Test]
    public async Task RendersServerSubjectProgressAndCurrentPersonContext()
    {
        var progress = new RegistrationRequirementProgressView(3, 1, 0, 2, false);
        var cut = _context.RenderMudComponent<RegistrationFormRenderer>(parameters => parameters
            .Add(component => component.Sections,
                [new RegistrationSectionView(Guid.CreateVersion7(), 1, "Your details", [Field("SHORT_TEXT", 1)])])
            .Add(component => component.SubjectLabel, "Person 2 of 3")
            .Add(component => component.RequirementProgress, progress));

        await Assert.That(cut.Find(".registration-form__subject").TextContent).IsEqualTo("Person 2 of 3");
        await Assert.That(cut.Find("#registration-requirement-progress").GetAttribute("value")).IsEqualTo("1");
        await Assert.That(cut.Find("label[for='registration-requirement-progress']").TextContent).Contains("1 of 3 people completed");
    }

    [Test]
    public async Task ServerIssueMapsBySafeFieldIdentityAndKeyboardSubmitAnnouncesStates()
    {
        RegistrationFieldView field = Field("SHORT_TEXT", 1);
        RegistrationFormAnswerState state = new();
        state.SetIssues([new(field.Id, ["answer.invalid_format"])]);
        int submissions = 0;
        var links = new Dictionary<string, HalLink> { ["submit"] = new() { Href = "/submit", Method = "POST" } };
        var cut = Render([field], state: state, links: links, onSubmit: _ => { submissions++; return Task.CompletedTask; });

        await Assert.That(cut.Find("input").GetAttribute("aria-invalid")).IsEqualTo("true");
        await cut.Find("form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.That(submissions).IsEqualTo(1));
        await _announcer.Received().AnnouncePoliteAsync("Processing registration details.");
        await _announcer.Received().AnnouncePoliteAsync("Registration details saved.");
    }

    private IRenderedComponent<RegistrationFormRenderer> Render(
        IReadOnlyList<RegistrationFieldView> fields,
        IReadOnlyList<RegistrationRuleView>? rules = null,
        RegistrationFormAnswerState? state = null,
        IReadOnlyDictionary<string, HalLink>? links = null,
        Func<RegistrationFormSubmission, Task>? onSubmit = null,
        Func<Task>? onSkip = null) =>
        _context.RenderMudComponent<RegistrationFormRenderer>(parameters => parameters
            .Add(component => component.Sections,
                [new RegistrationSectionView(Guid.CreateVersion7(), 1, "Your details", fields)])
            .Add(component => component.Rules, rules ?? [])
            .Add(component => component.State, state ?? new RegistrationFormAnswerState())
            .Add(component => component.Links, links ?? new Dictionary<string, HalLink>())
            .Add(component => component.OnSubmit, onSubmit ?? (_ => Task.CompletedTask))
            .Add(component => component.OnSkip, onSkip ?? (() => Task.CompletedTask)));

    private static RegistrationFieldView[] PortableFields() =>
    [
        Field("SHORT_TEXT", 1), Field("LONG_TEXT", 2), Field("INTEGER", 3), Field("DECIMAL", 4), Field("BOOLEAN", 5),
        Field("DATE", 6), Field("TIME", 7), Field("INSTANT", 8), Field("EMAIL", 9), Field("PHONE", 10), Field("URL", 11),
        Field("COUNTRY_CODE", 12), Field("LANGUAGE_TAG", 13), Field("SINGLE_CHOICE", 14), Field("MULTIPLE_CHOICE", 15),
        Field("RATING", 16), Field("CONSENT", 17)
    ];

    private static RegistrationFieldView Field(string code, int ordinal) => new(
        Guid.CreateVersion7(), ordinal, "attendee", $"field_{ordinal}", code.Replace('_', ' '), code,
        ordinal % 2 == 0, code == "MULTIPLE_CHOICE", null, null, null, null, null, null, null, null,
        code == "CONSENT" ? "v1" : null,
        code == "CONSENT" ? "I agree that ISLAMU may use this answer to arrange accessibility support." : null,
        code is "SINGLE_CHOICE" or "MULTIPLE_CHOICE"
            ? [new RegistrationFieldOptionView(Guid.CreateVersion7(), 1, "yes", "Yes", false)]
            : []);
}
