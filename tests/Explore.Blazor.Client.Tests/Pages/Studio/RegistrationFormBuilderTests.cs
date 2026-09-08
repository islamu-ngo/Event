using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio.RegistrationForms;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class RegistrationFormBuilderTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IRegistrationFormAuthoringService _service;
    private readonly IAccessibilityAnnouncerService _announcer;
    private readonly IAccessibilityFocusService _focus;
    private readonly IDialogService _dialog;

    public RegistrationFormBuilderTests()
    {
        _service = _ctx.AddMockService<IRegistrationFormAuthoringService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
        _focus = _ctx.AddMockService<IAccessibilityFocusService>();
        _dialog = Substitute.For<IDialogService>();
        _ctx.Services.AddSingleton(_dialog);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task EmptyWorkflowRendersEmptyStateOnlyWhenCreateFormIsAdvertised()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();
        var createLink = new HalLink
        {
            Href = $"/api/events/{eventId}/registration-workflows/{workflowId}/forms",
            Method = "POST"
        };
        var workflow = new HalResourceOfRegistrationWorkflowDto
        {
            Id = workflowId,
            EventId = eventId,
            Purpose = "registration",
            Forms = [],
            ConcurrencyStamp = concurrencyStamp,
            _links = new Dictionary<string, HalLink> { ["create-form"] = createLink }
        };
        var summary = new RegistrationFormVersionSummaryDto
        {
            Id = Guid.CreateVersion7(),
            Version = 1,
            StatusCode = "DRAFT",
            StatusName = "Draft",
            LanguageTag = "en"
        };
        var embedded = new RegistrationFormDto
        {
            Id = formId,
            EventId = eventId,
            Name = "Attendee details",
            Namespace = "event",
            Key = "attendee-details",
            Versions = [summary]
        };
        var refreshedWorkflow = new HalResourceOfRegistrationWorkflowDto
        {
            Id = workflowId,
            EventId = eventId,
            Purpose = "registration",
            Forms = [embedded],
            ConcurrencyStamp = concurrencyStamp
        };
        var createdForm = new HalResourceOfRegistrationFormDto
        {
            Id = formId,
            EventId = eventId,
            Name = embedded.Name,
            Namespace = embedded.Namespace,
            Key = embedded.Key,
            Versions = [summary]
        };
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, formId, "DRAFT");
        version.Id = summary.Id;
        _service.GetWorkflowAsync(eventId, Arg.Any<CancellationToken>()).Returns(workflow, refreshedWorkflow);
        _service.CreateFormAsync(eventId, workflowId, concurrencyStamp, Arg.Any<RegistrationFormInput>(), createLink, Arg.Any<CancellationToken>()).Returns(formId);
        _service.GetFormAsync(eventId, formId, Arg.Any<CancellationToken>()).Returns(createdForm);
        _service.GetVersionAsync(eventId, formId, summary.Id, Arg.Any<CancellationToken>()).Returns(version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='registration-form-empty']");
        cut.Find("input").Change("Attendee details");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Create form", StringComparison.Ordinal)).Click();

        cut.WaitForElement("[data-testid='registration-form-version']");
        await Assert.That(cut.Find("[data-version-status]").GetAttribute("data-version-status")).IsEqualTo("DRAFT");
        await _service.Received(1).CreateFormAsync(
            eventId,
            workflowId,
            concurrencyStamp,
            Arg.Is<RegistrationFormInput>(input => input.Name == "Attendee details" && input.Key == "attendee-details"),
            createLink,
            Arg.Any<CancellationToken>());
        await _service.Received(1).GetFormAsync(eventId, formId, Arg.Any<CancellationToken>());
        await _service.Received(1).GetVersionAsync(eventId, formId, summary.Id, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EmptyWorkflowCanInstantiateAdvertisedTemplate()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid workflowId = Guid.CreateVersion7();
        Guid workflowStamp = Guid.CreateVersion7();
        Guid templateId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        var instantiate = new HalLink { Href = $"/api/registration-form-templates/{templateId}/instantiate", Method = "POST" };
        var workflow = new HalResourceOfRegistrationWorkflowDto
        {
            Id = workflowId,
            EventId = eventId,
            Purpose = "registration",
            Forms = [],
            ConcurrencyStamp = workflowStamp
        };
        var refreshed = new HalResourceOfRegistrationWorkflowDto
        {
            Id = workflowId,
            EventId = eventId,
            Purpose = "registration",
            Forms = [new RegistrationFormDto { Id = formId, EventId = eventId, Name = "Template form", Namespace = "event", Key = "template-form", Versions = [new RegistrationFormVersionSummaryDto { Id = versionId, Version = 1, StatusCode = "DRAFT", StatusName = "Draft", LanguageTag = "en" }] }],
            ConcurrencyStamp = workflowStamp
        };
        _service.GetWorkflowAsync(eventId, Arg.Any<CancellationToken>()).Returns(workflow, refreshed);
        _service.GetTemplatesAsync(Arg.Any<CancellationToken>()).Returns(TemplateCollection(templateId, "Template form", instantiate));
        _service.InstantiateTemplateAsync(templateId, Arg.Any<InstantiateRegistrationFormTemplateInputDto>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(formId);
        _service.GetFormAsync(eventId, formId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfRegistrationFormDto { Id = formId, EventId = eventId, Name = "Template form", Namespace = "event", Key = "template-form", Versions = [new RegistrationFormVersionSummaryDto { Id = versionId, Version = 1, StatusCode = "DRAFT", StatusName = "Draft", LanguageTag = "en" }] });
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, formId, "DRAFT");
        version.Id = versionId;
        _service.GetVersionAsync(eventId, formId, versionId, Arg.Any<CancellationToken>()).Returns(version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='registration-form-template-picker']");
        cut.Find("[data-testid='instantiate-registration-form-template']").Click();

        cut.WaitForElement("[data-testid='registration-form-version']");
        await _service.Received(1).InstantiateTemplateAsync(templateId,
            Arg.Is<InstantiateRegistrationFormTemplateInputDto>(input =>
                input.EventId == eventId && input.WorkflowId == workflowId && input.ExpectedWorkflowConcurrencyStamp == workflowStamp && input.Key == "template-form"),
            Arg.Is<HalLink>(link => link.Href == instantiate.Href && link.Method == instantiate.Method),
            Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Registration form created from template.");
    }

    [Test]
    public async Task LoadFailureRendersSafeErrorWithoutExceptionDetails()
    {
        Guid eventId = Guid.CreateVersion7();
        _service.GetWorkflowAsync(eventId, Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("secret backend detail"));

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='registration-form-error']");
        await Assert.That(cut.Markup).Contains("Refresh and try again");
        await Assert.That(cut.Markup).DoesNotContain("secret backend detail");
    }

    [Test]
    public async Task PublishedVersionIsReadOnlyAndOnlyAdvertisedNewVersionActionIsShown()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("PUBLISHED");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "PUBLISHED");
        version._links = Links();
        ClearNestedLinks(version);
        form._links = Links(("create-version", $"/api/events/{eventId}/registration-forms/{form.Id}/versions", "POST"));
        Configure(eventId, workflow, form, version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='clone-registration-form-version']");
        await Assert.That(cut.FindAll("[aria-label^='Move section']")).IsEmpty();
        await Assert.That(cut.FindAll("[aria-label^='Move field']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid^='edit-registration-form']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid^='delete-registration-form']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid^='add-registration-form']")).IsEmpty();
        await Assert.That(cut.Find("[data-version-status]").GetAttribute("data-version-status")).IsEqualTo("PUBLISHED");
    }

    [Test]
    public async Task MutationLinksAloneControlAffordancesRegardlessOfDisplayStatus()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("PUBLISHED");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "PUBLISHED");
        version._links = Links(
            ("add-section", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections", "POST"),
            ("reorder-sections", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/reorder", "PUT"),
            ("publish", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/publish", "POST"));
        Configure(eventId, workflow, form, version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='add-registration-form-section']");
        await Assert.That(cut.FindAll("[aria-label^='Move section']").Count).IsEqualTo(4);
        await Assert.That(cut.FindAll("[data-testid='publish-registration-form-version']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task AdvertisedNewVersionActionCreatesAndOpensEditableDraft()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("PUBLISHED");
        HalResourceOfRegistrationFormVersionDto published = VersionGraph(eventId, form.Id, "PUBLISHED");
        HalResourceOfRegistrationFormVersionDto draft = VersionGraph(eventId, form.Id, "DRAFT");
        form._links = Links(("create-version", $"/api/events/{eventId}/registration-forms/{form.Id}/versions", "POST"));
        Configure(eventId, workflow, form, published);
        _service.CreateVersionAsync(eventId, form.Id, form.ConcurrencyStamp,
            Arg.Any<RegistrationFormVersionInput>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(draft.Id);
        _service.GetVersionAsync(eventId, form.Id, draft.Id, Arg.Any<CancellationToken>()).Returns(draft);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement("[data-testid='clone-registration-form-version']").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find("[data-version-status]").GetAttribute("data-version-status")).IsEqualTo("DRAFT"));
        await _service.Received(1).CreateVersionAsync(eventId, form.Id, form.ConcurrencyStamp,
            Arg.Is<RegistrationFormVersionInput>(input => input.CloneFromVersionId == published.Id),
            Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("New draft version created.");
    }

    [Test]
    public async Task DraftKeyboardReorderDisablesBoundariesPersistsAndAnnounces()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT");
        Configure(eventId, workflow, form, version);
        HalResourceOfRegistrationFormVersionDto authoritative = VersionGraph(eventId, form.Id, "DRAFT");
        RegistrationFormSectionDto[] reversed = version.Sections.OrderByDescending(item => item.Ordinal).ToArray();
        authoritative.Sections = reversed;
        _service.ReorderSectionsAsync(eventId, form.Id, version.Id, version.ConcurrencyStamp,
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(authoritative);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement("[aria-label='Move section Contact down']");

        await Assert.That(cut.Find("[aria-label='Move section Contact up']").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find("[aria-label='Move section Preferences down']").HasAttribute("disabled")).IsTrue();
        cut.Find("[aria-label='Move section Contact down']").Click();

        Guid[] initial = version.Sections.OrderBy(item => item.Ordinal).Select(item => item.Id).ToArray();
        await _service.Received(1).ReorderSectionsAsync(
            eventId, form.Id, version.Id, version.ConcurrencyStamp,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { initial[1], initial[0] })),
            Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
        await _announcer.Received(1).AnnouncePoliteAsync("Section moved down.");
        await _focus.Received(1).FocusByIdAsync($"section-{initial[0]:N}-move-down", true);
    }

    [Test]
    public async Task FieldReorderDisablesBoundariesAndUsesAuthoritativeReturnedState()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        Configure(eventId, workflow, form, version);
        RegistrationFormSectionDto section = version.Sections.OrderBy(item => item.Ordinal).First();
        RegistrationFormFieldDto[] fields = section.Fields.OrderBy(item => item.Ordinal).ToArray();
        HalResourceOfRegistrationFormVersionDto authoritative = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        authoritative.Sections = [authoritative.Sections.First() with { Fields = [fields[1], fields[0]] }, ..authoritative.Sections.Skip(1)];
        _service.ReorderFieldsAsync(eventId, form.Id, version.Id, section.Id, version.ConcurrencyStamp,
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(authoritative);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement($"[aria-label='Move field {fields[0].Label} down']");
        await Assert.That(cut.Find($"[aria-label='Move field {fields[0].Label} up']").HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Find($"[aria-label='Move field {fields[1].Label} down']").HasAttribute("disabled")).IsTrue();
        cut.Find($"[aria-label='Move field {fields[0].Label} down']").Click();

        await _service.Received(1).ReorderFieldsAsync(eventId, form.Id, version.Id, section.Id, version.ConcurrencyStamp,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { fields[1].Id, fields[0].Id })),
            Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-field-id]").First().GetAttribute("data-field-id")).IsEqualTo(fields[1].Id.ToString()));
        await _focus.Received(1).FocusByIdAsync($"field-{fields[0].Id:N}-move-down", true);
    }

    [Test]
    public async Task PointerDragReordersSectionsThroughAtomicCommand()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT");
        Configure(eventId, workflow, form, version);
        RegistrationFormSectionDto[] sections = version.Sections.OrderBy(item => item.Ordinal).ToArray();
        HalResourceOfRegistrationFormVersionDto authoritative = VersionGraph(eventId, form.Id, "DRAFT");
        authoritative.Sections = [sections[1], sections[0]];
        _service.ReorderSectionsAsync(eventId, form.Id, version.Id, version.ConcurrencyStamp,
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(authoritative);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement($"[data-section-id='{sections[0].Id}']");
        var source = cut.Find($"[data-section-id='{sections[0].Id}']");
        var target = cut.Find($"[data-section-id='{sections[1].Id}']");

        await Assert.That(source.GetAttribute("draggable")).IsEqualTo("true");
        source.TriggerEvent("ondragstart", new DragEventArgs());
        target.TriggerEvent("ondrop", new DragEventArgs());

        await _service.Received(1).ReorderSectionsAsync(eventId, form.Id, version.Id, version.ConcurrencyStamp,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { sections[1].Id, sections[0].Id })),
            Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-section-id]").First().GetAttribute("data-section-id")).IsEqualTo(sections[1].Id.ToString()));
        await _announcer.Received(1).AnnouncePoliteAsync("Section moved to position 2.");
    }

    [Test]
    public async Task PointerDragReordersFieldsThroughAtomicCommand()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        Configure(eventId, workflow, form, version);
        RegistrationFormSectionDto section = version.Sections.OrderBy(item => item.Ordinal).First();
        RegistrationFormFieldDto[] fields = section.Fields.OrderBy(item => item.Ordinal).ToArray();
        HalResourceOfRegistrationFormVersionDto authoritative = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        authoritative.Sections = [authoritative.Sections.First() with { Fields = [fields[1], fields[0]] }, ..authoritative.Sections.Skip(1)];
        _service.ReorderFieldsAsync(eventId, form.Id, version.Id, section.Id, version.ConcurrencyStamp,
            Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(authoritative);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement($"[data-field-id='{fields[0].Id}']");
        var source = cut.Find($"[data-field-id='{fields[0].Id}']");
        var target = cut.Find($"[data-field-id='{fields[1].Id}']");

        await Assert.That(source.GetAttribute("draggable")).IsEqualTo("true");
        source.TriggerEvent("ondragstart", new DragEventArgs());
        target.TriggerEvent("ondrop", new DragEventArgs());

        await _service.Received(1).ReorderFieldsAsync(eventId, form.Id, version.Id, section.Id, version.ConcurrencyStamp,
            Arg.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { fields[1].Id, fields[0].Id })),
            Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[data-field-id]").First().GetAttribute("data-field-id")).IsEqualTo(fields[1].Id.ToString()));
        await _announcer.Received(1).AnnouncePoliteAsync("Field moved to position 2.");
    }

    [Test]
    public async Task BuilderAndEditorsResolveUserCopyThroughTranslationService()
    {
        ITranslationService translation = _ctx.Services.GetRequiredService<ITranslationService>();
        translation.T("studio.registration_forms.sections.title", "Sections and fields").Returns("Translated builder");
        translation.T("studio.registration_forms.field.definition", "Field definition").Returns("Translated field editor");
        translation.T("studio.registration_forms.condition.clause", "Condition clause {0}").Returns("Translated condition {0}");
        translation.T("studio.registration_forms.rule.definition", "Rule definition").Returns("Translated rule editor");
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        version._links = Links(("add-rule", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/rules", "POST"));
        Configure(eventId, workflow, form, version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement("[data-testid='edit-registration-form-field']").Click();
        cut.Find("[data-testid='add-registration-form-rule']").Click();

        cut.WaitForElement("[data-testid='registration-form-rule-editor']");
        await Assert.That(cut.Markup).Contains("Translated builder");
        await Assert.That(cut.Markup).Contains("Translated field editor");
        await Assert.That(cut.Markup).Contains("Translated condition 1");
        await Assert.That(cut.Markup).Contains("Translated rule editor");
    }

    [Test]
    public async Task ConsentEditorRequiresExactConsentTextAndSendsItThroughAuthoringInput()
    {
        const string consentText = "I agree to receive event updates by email.";
        var model = new RegistrationFormFieldEditModel
        {
            Ordinal = 1,
            Namespace = "person",
            Key = "updates-consent",
            Label = "Receive event updates",
            FieldTypeId = 17,
            RetentionPolicyId = 1,
            OrganizerVisibilityId = 2,
            RequiresExplicitConsent = true,
            ConsentPurposeCode = "EVENT_UPDATES",
            ConsentTextVersion = "2026-08",
            ConsentText = consentText
        };

        var cut = _ctx.RenderMudComponent<RegistrationFormFieldEditor>(parameters => parameters
            .Add(component => component.Model, model));

        var consentTextArea = cut.Find("textarea");
        await Assert.That(consentTextArea.GetAttribute("maxlength")).IsEqualTo("4000");
        await Assert.That(consentTextArea.HasAttribute("required")).IsTrue();
        await Assert.That(cut.Find("[data-testid='save-registration-form-field']").HasAttribute("disabled")).IsFalse();
        await Assert.That(model.ToCreateInput().ConsentText).IsEqualTo(consentText);

        model.ConsentText = " ";
        await Assert.That(model.IsValid).IsFalse();
    }

    [Test]
    public async Task FieldEditorSendsAnalyticsGovernanceOnlyForAggregatableFields()
    {
        var model = new RegistrationFormFieldEditModel
        {
            Ordinal = 1,
            Namespace = "person",
            Key = "age",
            Label = "Age",
            FieldTypeId = 3,
            RetentionPolicyId = 1,
            OrganizerVisibilityId = 2,
            IsAnalyticsRelevant = true,
            IsOperationallyFilterable = true
        };

        var cut = _ctx.RenderMudComponent<RegistrationFormFieldEditor>(parameters => parameters
            .Add(component => component.Model, model));

        await Assert.That(cut.Markup).Contains("Analytics relevant");
        var input = model.ToCreateInput();
        await Assert.That(input.AdditionalProperties["isAnalyticsRelevant"]).IsEqualTo(true);
        await Assert.That(input.AdditionalProperties["isOperationallyFilterable"]).IsEqualTo(true);

        model.SetFieldType(9);

        await Assert.That(model.IsAnalyticsRelevant).IsFalse();
        await Assert.That(model.IsOperationallyFilterable).IsFalse();
    }

    [Test]
    public async Task ExactNestedCrudRelationsRenderCompleteAuthoringSurface()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT", includeFields: true);
        RegistrationFormSectionDto section = version.Sections.First();
        RegistrationFormFieldDto field = section.Fields.First();
        RegistrationFormFieldOptionDto option = Option("General", 1, eventId, form.Id, version.Id, section.Id, field.Id);
        field = field with { Options = [option] };
        section = section with { Fields = [field, ..section.Fields.Skip(1)] };
        RegistrationFormRuleDto rule = Rule(eventId, form.Id, version.Id, field);
        version.Sections = [section, ..version.Sections.Skip(1)];
        version.Rules = [rule];
        version._links = Links(
            ("add-section", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections", "POST"),
            ("add-rule", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/rules", "POST"));
        SetLinks(section.AdditionalProperties,
            ("edit", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}", "PATCH"),
            ("delete", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}", "DELETE"),
            ("add-field", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}/fields", "POST"));
        SetLinks(field.AdditionalProperties,
            ("edit", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}/fields/{field.Id}", "PATCH"),
            ("delete", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}/fields/{field.Id}", "DELETE"),
            ("add-option", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/sections/{section.Id}/fields/{field.Id}/options", "POST"));
        Configure(eventId, workflow, form, version);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='edit-registration-form-section']");
        await Assert.That(cut.FindAll("[data-testid='delete-registration-form-section']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='add-registration-form-field']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='edit-registration-form-field']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='delete-registration-form-field']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='add-registration-form-option']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='edit-registration-form-option']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='retire-registration-form-option']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='add-registration-form-rule']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='edit-registration-form-rule']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='delete-registration-form-rule']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("fieldset").Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(cut.FindAll("legend").Count).IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task EventChangeCancelsPendingLoadAndIgnoresStaleResult()
    {
        Guid firstEventId = Guid.CreateVersion7();
        Guid secondEventId = Guid.CreateVersion7();
        var pending = new TaskCompletionSource<HalResourceOfRegistrationWorkflowDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken firstToken = default;
        _service.GetWorkflowAsync(firstEventId, Arg.Any<CancellationToken>()).Returns(call =>
        {
            firstToken = call.ArgAt<CancellationToken>(1);
            return pending.Task;
        });
        _service.GetWorkflowAsync(secondEventId, Arg.Any<CancellationToken>()).Returns(new HalResourceOfRegistrationWorkflowDto
        {
            Id = Guid.CreateVersion7(),
            EventId = secondEventId,
            Purpose = "registration",
            Forms = []
        });

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, firstEventId));
        cut.WaitForState(() => firstToken.CanBeCanceled);
        cut.Render(parameters => parameters.Add(component => component.EventId, secondEventId));
        cut.WaitForElement("[data-testid='registration-form-empty']");

        await Assert.That(firstToken.IsCancellationRequested).IsTrue();
        pending.SetResult(new HalResourceOfRegistrationWorkflowDto
        {
            Id = Guid.CreateVersion7(),
            EventId = firstEventId,
            Purpose = "registration",
            Forms =
            [new RegistrationFormDto { Id = Guid.CreateVersion7(), EventId = firstEventId, Name = "Stale", Namespace = "event", Key = "stale" }]
        });
        await cut.InvokeAsync(() => Task.CompletedTask);
        await Assert.That(cut.FindAll("option").Any(item => item.TextContent.Contains("Stale", StringComparison.Ordinal))).IsFalse();
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task PublishCancellationRestoresFocusAndDispatchesNoMutation()
    {
        (Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form) = FormGraph("DRAFT");
        HalResourceOfRegistrationFormVersionDto version = VersionGraph(eventId, form.Id, "DRAFT");
        version._links = Links(("publish", $"/api/events/{eventId}/registration-forms/{form.Id}/versions/{version.Id}/publish", "POST"));
        Configure(eventId, workflow, form, version);
        _dialog.ShowMessageBoxAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<DialogOptions>())
            .Returns(false);

        var cut = _ctx.RenderMudComponent<RegistrationFormBuilder>(parameters => parameters.Add(component => component.EventId, eventId));
        cut.WaitForElement("[data-testid='publish-registration-form-version']").Click();

        await _focus.Received(1).SaveFocusAsync();
        await _focus.Received(1).RestoreFocusAsync();
        await _service.DidNotReceive().PublishAsync(eventId, form.Id, version.Id, Arg.Any<Guid>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MutationTargetValidationRejectsStaleResourceAndWrongMethod()
    {
        var link = new HalLink { Href = "/api/events/00000000-0000-0000-0000-000000000001/registration-forms/other/versions", Method = "GET" };
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            RegistrationFormHal.Require(link, "POST", "/api/events/current/registration-forms/current/versions"));
        await Assert.That(error.Message).Contains("advertise");
    }

    private void Configure(Guid eventId, HalResourceOfRegistrationWorkflowDto workflow, HalResourceOfRegistrationFormDto form, HalResourceOfRegistrationFormVersionDto version)
    {
        RegistrationFormVersionSummaryDto summary = form.Versions.First() with { Id = version.Id };
        form.Versions = [summary];
        workflow.Forms = [workflow.Forms.First() with { Versions = [summary] }];
        _service.GetWorkflowAsync(eventId, Arg.Any<CancellationToken>()).Returns(workflow);
        _service.GetFormAsync(eventId, form.Id, Arg.Any<CancellationToken>()).Returns(form);
        _service.GetVersionAsync(eventId, form.Id, version.Id, Arg.Any<CancellationToken>()).Returns(version);
        _service.ReorderSectionsAsync(eventId, form.Id, version.Id, Arg.Any<Guid>(), Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<HalLink>(), Arg.Any<CancellationToken>()).Returns(version);
    }

    private static (Guid, HalResourceOfRegistrationWorkflowDto, HalResourceOfRegistrationFormDto) FormGraph(string status)
    {
        Guid eventId = Guid.CreateVersion7();
        Guid formId = Guid.CreateVersion7();
        Guid versionId = Guid.CreateVersion7();
        var summary = new RegistrationFormVersionSummaryDto { Id = versionId, Version = 1, StatusCode = status, StatusName = status, LanguageTag = "en" };
        var embedded = new RegistrationFormDto { Id = formId, EventId = eventId, Name = "Registration", Namespace = "event", Key = "registration", Versions = [summary] };
        var workflow = new HalResourceOfRegistrationWorkflowDto { Id = Guid.CreateVersion7(), EventId = eventId, Purpose = "registration", Forms = [embedded] };
        var form = new HalResourceOfRegistrationFormDto { Id = formId, EventId = eventId, Name = embedded.Name, Namespace = embedded.Namespace, Key = embedded.Key, Versions = [summary] };
        return (eventId, workflow, form);
    }

    private static HalResourceOfRegistrationFormVersionDto VersionGraph(Guid eventId, Guid formId, string status, bool includeFields = false)
    {
        Guid versionId = Guid.CreateVersion7();
        var first = Section("Contact", 0, eventId, formId, versionId);
        var second = Section("Preferences", 1, eventId, formId, versionId);
        if (includeFields)
        {
            first = first with { Fields = [Field("Name", 1, eventId, formId, versionId, first.Id), Field("Email", 2, eventId, formId, versionId, first.Id)] };
        }
        return new HalResourceOfRegistrationFormVersionDto
        {
            Id = versionId,
            EventId = eventId,
            RegistrationFormId = formId,
            Version = 1,
            StatusCode = status,
            StatusName = status,
            LanguageTag = "en",
            Sections = [first, second],
            _links = Links(("reorder-sections", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/reorder", "PUT"))
        };
    }

    private static RegistrationFormSectionDto Section(string title, int ordinal, Guid eventId, Guid formId, Guid versionId)
    {
        var section = new RegistrationFormSectionDto { Id = Guid.CreateVersion7(), Title = title, Ordinal = ordinal + 1 };
        section.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(Links(
            ("edit", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{section.Id}", "PATCH"),
            ("reorder-fields", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{section.Id}/fields/reorder", "PUT")));
        return section;
    }

    private static RegistrationFormFieldDto Field(string label, int ordinal, Guid eventId, Guid formId, Guid versionId, Guid sectionId)
    {
        var field = new RegistrationFormFieldDto
        {
            Id = Guid.CreateVersion7(),
            Ordinal = ordinal,
            Namespace = "event",
            Key = label.ToLowerInvariant(),
            Label = label,
            FieldTypeId = 14,
            FieldTypeCode = "SINGLE_CHOICE",
            FieldTypeName = "Single choice",
            RetentionPolicyId = 1,
            OrganizerVisibilityId = 2,
            OrganizerVisibilityCode = "AUTHORIZED_ORGANIZERS",
            OrganizerVisibilityName = "Authorized organizers",
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        SetLinks(field.AdditionalProperties,
            ("edit", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}", "PATCH"),
            ("delete", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}", "DELETE"),
            ("add-option", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}/options", "POST"));
        return field;
    }

    private static RegistrationFormFieldOptionDto Option(string label, int ordinal, Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId)
    {
        var option = new RegistrationFormFieldOptionDto { Id = Guid.CreateVersion7(), Ordinal = ordinal, Key = label.ToLowerInvariant(), Label = label, ConcurrencyStamp = Guid.CreateVersion7() };
        SetLinks(option.AdditionalProperties,
            ("edit", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{fieldId}/options/{option.Id}", "PATCH"),
            ("retire", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{fieldId}/options/{option.Id}", "DELETE"));
        return option;
    }

    private static RegistrationFormRuleDto Rule(Guid eventId, Guid formId, Guid versionId, RegistrationFormFieldDto field)
    {
        var rule = new RegistrationFormRuleDto
        {
            Id = Guid.CreateVersion7(),
            Ordinal = 1,
            TargetNamespace = field.Namespace,
            TargetKey = field.Key,
            Effect = 1,
            Condition = new Condition { Operator = "exists", FieldNamespace = field.Namespace, FieldKey = field.Key },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        SetLinks(rule.AdditionalProperties,
            ("edit", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules/{rule.Id}", "PATCH"),
            ("delete", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules/{rule.Id}", "DELETE"));
        return rule;
    }

    private static void SetLinks(IDictionary<string, object> properties, params (string Rel, string Href, string Method)[] links) =>
        properties["_links"] = JsonSerializer.SerializeToElement(Links(links));

    private static void ClearNestedLinks(HalResourceOfRegistrationFormVersionDto version)
    {
        foreach (RegistrationFormSectionDto section in version.Sections)
        {
            section.AdditionalProperties.Clear();
            foreach (RegistrationFormFieldDto field in section.Fields)
            {
                field.AdditionalProperties.Clear();
                foreach (RegistrationFormFieldOptionDto option in field.Options)
                {
                    option.AdditionalProperties.Clear();
                }
            }
        }

        foreach (RegistrationFormRuleDto rule in version.Rules)
        {
            rule.AdditionalProperties.Clear();
        }
    }

    private static Dictionary<string, HalLink> Links(params (string Rel, string Href, string Method)[] links) =>
        links.ToDictionary(item => item.Rel, item => new HalLink { Href = item.Href, Method = item.Method });

    private static HalCollectionResourceOfRegistrationFormTemplateDto TemplateCollection(Guid id, string name, HalLink instantiate) => new()
    {
        _embedded = new HalCollectionEmbeddedOfRegistrationFormTemplateDto
        {
            Items = [new HalResourceOfRegistrationFormTemplateDto
            {
                Id = id,
                Name = name,
                Description = "Template",
                Category = "General",
                _links = new Dictionary<string, HalLink> { ["instantiate"] = instantiate }
            }]
        }
    };
}
