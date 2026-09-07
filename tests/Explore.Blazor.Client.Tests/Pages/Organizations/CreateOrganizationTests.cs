using System.Reflection;
using Explore.Blazor.Client.Pages.Organizations;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Organizations;

public sealed class CreateOrganizationTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IOrganizationService _organizationService;

    public CreateOrganizationTests()
    {
        _ctx = new BlazorTestContext();
        _organizationService = Substitute.For<IOrganizationService>();

        _ctx.Services.AddSingleton(_organizationService);
        _ctx.Services.AddSingleton(Substitute.For<IImageStorageService>());
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<ILogger<CreateOrganization>>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Submit_WithInvalidEmail_ShowsClientValidationAndDoesNotCallService()
    {
        var cut = RenderWithValidRequiredFields();
        SetOrganizationField(cut, dto => dto.Email = "not-an-email");

        await InvokeHandleSubmitAsync(cut);
        cut.Render();

        await _organizationService.DidNotReceiveWithAnyArgs().CreateOrganizationAsync(default!);
        await Assert.That(cut.Markup).Contains("Enter a valid contact email.");
    }

    [Test]
    public async Task Submit_WithValidationProblemDetails_MapsServerErrorsIntoSummary()
    {
        _organizationService.CreateOrganizationAsync(Arg.Any<CreateOrganizationDto>())
            .ThrowsAsync(new ApiException<ValidationProblemDetails>(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ValidationProblemDetails
                {
                    Errors = new Dictionary<string, ICollection<string>>
                    {
                        ["email"] = new[] { "Use an organization-owned email address." }
                    }
                },
                null));
        var cut = RenderWithValidRequiredFields();

        await InvokeHandleSubmitAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Please fix the validation errors below.");
        await Assert.That(cut.Markup).Contains("Use an organization-owned email address.");
    }

    [Test]
    public async Task Submit_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "provider rejected <script>alert(1)</script> secret";
        _organizationService.CreateOrganizationAsync(Arg.Any<CreateOrganizationDto>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));
        var cut = RenderWithValidRequiredFields();

        await InvokeHandleSubmitAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Organization could not be submitted. Please try again.");
        await Assert.That(cut.Markup).DoesNotContain(rawProviderMessage);
        await Assert.That(cut.Markup).DoesNotContain("<script>");
    }

    private IRenderedComponent<CreateOrganization> RenderWithValidRequiredFields()
    {
        var cut = _ctx.RenderMudComponent<CreateOrganization>();
        SetOrganizationField(cut, dto =>
        {
            dto.FullName = "Community Foundation";
            dto.Email = "hello@example.org";
            dto.Address = "1 Main Street";
            dto.Postcode = 12345;
            dto.City = "Brussels";
            dto.Country = "Belgium";
            dto.WebsiteUrl = "https://example.org";
        });
        SetField(cut.Instance, "acceptTerms", true);
        SetField(cut.Instance, "confirmInformation", true);
        cut.Render();
        return cut;
    }

    private static void SetOrganizationField(IRenderedComponent<CreateOrganization> cut, Action<CreateOrganizationDto> update)
    {
        var organization = (CreateOrganizationDto)GetField(cut.Instance, "organization");
        update(organization);
    }

    private static async Task InvokeHandleSubmitAsync(IRenderedComponent<CreateOrganization> cut)
    {
        var method = typeof(CreateOrganization)
            .GetMethod("HandleSubmit", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("HandleSubmit method was not found.");

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, null)!);
    }

    private static object GetField(object instance, string name) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;

    private static void SetField(object instance, string name, object value) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, value);
}
