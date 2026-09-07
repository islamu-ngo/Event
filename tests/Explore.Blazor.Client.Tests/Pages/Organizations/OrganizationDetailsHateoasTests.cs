using System.Reflection;
using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Pages.Organizations;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Organizations;

public class OrganizationDetailsHateoasTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IOrganizationService _organizationService;
    private readonly IEventService _eventService;
    private readonly IOrganizationMemberService _organizationMemberService;

    public OrganizationDetailsHateoasTests()
    {
        _ctx = new BlazorTestContext();
        _organizationService = Substitute.For<IOrganizationService>();
        _eventService = Substitute.For<IEventService>();
        _organizationMemberService = Substitute.For<IOrganizationMemberService>();

        _ctx.Services.AddSingleton(_organizationService);
        _ctx.Services.AddSingleton(_eventService);
        _ctx.Services.AddSingleton(_organizationMemberService);
        _ctx.Services.AddScoped<RouterStateService>();
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<ILogger<OrganizationDetails>>());

        _eventService.GetPublicEventsByActorAsync(Arg.Any<Guid>()).Returns(new List<EventListDto>());
        _eventService.GetPublicEventsByOrganizationAsync(Arg.Any<Guid>()).Returns(new List<EventListDto>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task WhenApiReturnsEditLink_ShowsOrganizationActionAffordances()
    {
        _organizationService.GetOrganizationByIdAsync(Arg.Any<Guid>())
            .Returns(CreateOrganization(withEditLink: true));

        var cut = _ctx.RenderMudComponent<OrganizationDetails>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Create Event");
        await Assert.That(cut.Markup).Contains("Members");
        await Assert.That(cut.Markup).Contains("Edit");
    }

    [Test]
    public async Task WhenApiDoesNotReturnEditLink_HidesOrganizationActionAffordances()
    {
        _organizationService.GetOrganizationByIdAsync(Arg.Any<Guid>())
            .Returns(CreateOrganization(withEditLink: false));

        var cut = _ctx.RenderMudComponent<OrganizationDetails>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup.Contains("Create Event", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Members", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Edit", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task DoesNotCallOrganizationMembersService_OnPageLoad()
    {
        _organizationService.GetOrganizationByIdAsync(Arg.Any<Guid>())
            .Returns(CreateOrganization(withEditLink: true));

        var cut = _ctx.RenderMudComponent<OrganizationDetails>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await _organizationMemberService.DidNotReceiveWithAnyArgs().GetMembersAsync(default);
    }

    [Test]
    public async Task EditSubmit_WithInvalidEmail_ShowsClientValidationAndDoesNotCallService()
    {
        var cut = RenderInEditMode();
        SetEditModelProperty(cut, "Email", "not-an-email");

        await InvokeSaveChangesAsync(cut);
        cut.Render();

        await _organizationService.DidNotReceiveWithAnyArgs().UpdateOrganizationAsync(default, default, default!);
        await Assert.That(cut.Markup).Contains("Enter a valid contact email.");
    }

    [Test]
    public async Task EditSubmit_WithValidationProblemDetails_MapsServerErrorsIntoSummary()
    {
        _organizationService.UpdateOrganizationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateOrganizationDto>())
            .ThrowsAsync(new ApiException<ValidationProblemDetails>(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ValidationProblemDetails
                {
                    Errors = new Dictionary<string, ICollection<string>>
                    {
                        ["Email.Value"] = new[] { "Use an organization-owned email address." }
                    }
                },
                null));
        var cut = RenderInEditMode();

        await InvokeSaveChangesAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Please fix the validation errors below.");
        await Assert.That(cut.Markup).Contains("Use an organization-owned email address.");
    }

    [Test]
    public async Task EditSubmit_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "database rejected <script>alert(1)</script> secret";
        _organizationService.UpdateOrganizationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateOrganizationDto>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));
        var cut = RenderInEditMode();

        await InvokeSaveChangesAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Organization could not be updated. Please try again.");
        await Assert.That(cut.Markup).DoesNotContain(rawProviderMessage);
        await Assert.That(cut.Markup).DoesNotContain("<script>");
    }

    private static OrganizationDto CreateOrganization(bool withEditLink)
    {
        var dto = new OrganizationDto
        {
            Id = Guid.NewGuid(),
            FullName = "Test Organization",
            Email = "test@example.com",
            WebsiteUrl = "https://example.org",
            Address = "1 Main Street",
            Postcode = "12345",
            City = "Brussels",
            Country = "Belgium",
            ApprovalStatusId = 2,
            ApprovalStatusFullName = "Approved",
            ConcurrencyStamp = Guid.NewGuid(),
            AdditionalProperties = new Dictionary<string, object>()
        };

        if (withEditLink)
        {
            using var doc = JsonDocument.Parse(
                "{\"self\":{\"href\":\"/api/organization/1\"},\"edit\":{\"href\":\"/api/organization/1\",\"method\":\"PUT\"}}");
            dto.AdditionalProperties["_links"] = doc.RootElement.Clone();
        }

        return dto;
    }

    private IRenderedComponent<OrganizationDetails> RenderInEditMode()
    {
        _organizationService.GetOrganizationByIdAsync(Arg.Any<Guid>())
            .Returns(CreateOrganization(withEditLink: true));

        var cut = _ctx.RenderMudComponent<OrganizationDetails>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
        InvokePrivate(cut.Instance, "ToggleEditMode");
        cut.Render();

        return cut;
    }

    private static async Task InvokeSaveChangesAsync(IRenderedComponent<OrganizationDetails> cut)
    {
        var method = typeof(OrganizationDetails)
            .GetMethod("SaveChanges", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SaveChanges method was not found.");

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, null)!);
    }

    private static void SetEditModelProperty(IRenderedComponent<OrganizationDetails> cut, string propertyName, object? value)
    {
        var model = GetField(cut.Instance, "editModel");
        model.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(model, value);
    }

    private static void InvokePrivate(object instance, string methodName)
    {
        instance
            .GetType()
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(instance, null);
    }

    private static object GetField(object instance, string name) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;
}
