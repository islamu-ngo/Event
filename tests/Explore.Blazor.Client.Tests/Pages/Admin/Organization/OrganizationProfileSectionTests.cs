using System.Reflection;
using Explore.Blazor.Client.Pages.Admin.Organization.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin.Organization;

public sealed class OrganizationProfileSectionTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IOrganizationService _organizationService;

    public OrganizationProfileSectionTests()
    {
        _ctx = new BlazorTestContext();
        _organizationService = Substitute.For<IOrganizationService>();

        _ctx.Services.AddSingleton(_organizationService);
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Save_WithInvalidEmail_ShowsClientValidationAndDoesNotCallService()
    {
        var cut = RenderLoadedProfile();
        SetModelProperty(cut.Instance, "Email", "not-an-email");

        await InvokeSaveAsync(cut);
        cut.Render();

        await _organizationService.DidNotReceiveWithAnyArgs().UpdateOrganizationAsync(default, default, default!);
        await Assert.That(cut.Markup).Contains("Enter a valid contact email.");
    }

    [Test]
    public async Task Save_WithValidationProblemDetails_MapsServerErrorsIntoSummary()
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
        var cut = RenderLoadedProfile();

        await InvokeSaveAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Please fix the validation errors below.");
        await Assert.That(cut.Markup).Contains("Use an organization-owned email address.");
    }

    [Test]
    public async Task Save_WithUnexpectedException_DoesNotEchoRawExceptionMessage()
    {
        const string rawProviderMessage = "provider rejected <script>alert(1)</script> secret";
        _organizationService.UpdateOrganizationAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<UpdateOrganizationDto>())
            .ThrowsAsync(new InvalidOperationException(rawProviderMessage));
        var cut = RenderLoadedProfile();

        await InvokeSaveAsync(cut);
        cut.Render();

        await Assert.That(cut.Markup).Contains("Organization profile could not be updated. Please try again.");
        await Assert.That(cut.Markup).DoesNotContain(rawProviderMessage);
        await Assert.That(cut.Markup).DoesNotContain("<script>");
    }

    private IRenderedComponent<OrganizationProfileSection> RenderLoadedProfile()
    {
        _organizationService.GetOrganizationByIdAsync(Arg.Any<Guid>())
            .Returns(CreateOrganization());

        var cut = _ctx.RenderMudComponent<OrganizationProfileSection>(
            parameters => parameters.Add(component => component.OrganizationId, Guid.NewGuid()));
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
        return cut;
    }

    private static OrganizationDto CreateOrganization() => new()
    {
        Id = Guid.NewGuid(),
        FullName = "Test Organization",
        Email = "test@example.com",
        WebsiteUrl = "https://example.org",
        Address = "1 Main Street",
        Postcode = "12345",
        City = "Brussels",
        Country = "Belgium",
        ConcurrencyStamp = Guid.NewGuid()
    };

    private static async Task InvokeSaveAsync(IRenderedComponent<OrganizationProfileSection> cut)
    {
        var method = typeof(OrganizationProfileSection)
            .GetMethod("SaveAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SaveAsync method was not found.");

        await cut.InvokeAsync(() => (Task)method.Invoke(cut.Instance, null)!);
    }

    private static void SetModelProperty(OrganizationProfileSection component, string propertyName, object? value)
    {
        var model = GetField(component, "_model");
        model.GetType()
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(model, value);
    }

    private static object GetField(object instance, string name) =>
        instance
            .GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(instance)!;
}
