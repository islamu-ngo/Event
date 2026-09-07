using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Pages.Organizations;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Organization;

public class MyOrganizationsTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IOrganizationService _organizationService;
    private readonly IUserService _userService;

    public MyOrganizationsTests()
    {
        _ctx = new BlazorTestContext();
        _organizationService = Substitute.For<IOrganizationService>();
        _userService = Substitute.For<IUserService>();

        _ctx.Services.AddSingleton(_organizationService);
        _ctx.Services.AddSingleton(_userService);
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<ILogger<MyOrganizations>>());

        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User", "test@example.com");
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task MyOrganizations_ShowsLoadingState_WhileInitialLoadIsPending()
    {
        // Arrange
        var pending = new TaskCompletionSource<ICollection<OrganizationListDto>>();
        _organizationService.GetMyOrganizationsAsync().Returns(pending.Task);

        // Act
        var cut = _ctx.RenderMudComponent<MyOrganizations>();

        // Assert
        await Assert.That(cut.Markup).Contains("Loading your organizations...");

        // Cleanup
        pending.TrySetResult(new List<OrganizationListDto>());
    }

    [Test]
    public async Task MyOrganizations_ShowsEmptyState_WhenNoOrganizationsExist()
    {
        // Arrange
        _organizationService.GetMyOrganizationsAsync().Returns(new List<OrganizationListDto>());
        _userService.SyncUserAsync().Returns((BaseCommandResponseOfGuid?)null);

        // Act
        var cut = _ctx.RenderMudComponent<MyOrganizations>();
        cut.WaitForState(() => cut.Markup.Contains("No organizations yet", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("No organizations yet");
        await Assert.That(cut.Markup).Contains("No organization creation affordance is currently available.");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/organizations/create\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/organization/create\"");
    }

    [Test]
    public async Task MyOrganizations_ShowsErrorState_WhenOrganizationFetchThrows()
    {
        // Arrange
        _organizationService.GetMyOrganizationsAsync().ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var cut = _ctx.RenderMudComponent<MyOrganizations>();
        cut.WaitForState(() => cut.Markup.Contains("Unable to load your organizations", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Unable to load your organizations. Please try again.");
        await Assert.That(cut.Markup).Contains("Retry");
    }

    [Test]
    public async Task MyOrganizations_ShowsOrganizationCards_WhenOrganizationsExist()
    {
        // Arrange
        var organizations = new List<OrganizationListDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                FullName = "Community Hub",
                Email = "hub@example.com",
                City = "Brussels",
                Country = "Belgium",
                CurrentUserRoleId = RoleHelper.OrgAdmin,
                ApprovalStatusId = 2
            }
        };

        _organizationService.GetMyOrganizationsAsync().Returns(organizations);

        // Act
        var cut = _ctx.RenderMudComponent<MyOrganizations>();
        cut.WaitForState(() => cut.Markup.Contains("Community Hub", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        // Assert
        await Assert.That(cut.Markup).Contains("Community Hub");
        await Assert.That(cut.Markup).Contains("1 organization(s)");
    }
}
