namespace Explore.Blazor.Client.Tests.Services.Shell;

using Explore.Blazor.Client.Services.Shell;

public sealed class WorkspaceRouteClassifierTests
{
    private readonly WorkspaceRouteClassifier _classifier = new(new WorkspaceRegistry());

    [Test]
    [Arguments("/", "events")]
    [Arguments("/events", "events")]
    [Arguments("/home", "events")]
    [Arguments("/errors/404", "events")]
    [Arguments("/errors/403", "events")]
    [Arguments("/errors/500", "events")]
    [Arguments("/startup", "events")]
    [Arguments("/setup", "events")]
    [Arguments("/onboarding/auth-provider", "events")]
    [Arguments("/onboarding/authz-provider", "events")]
    [Arguments("/onboarding/instance", "events")]
    [Arguments("/onboarding/tenant", "events")]
    [Arguments("/login", "events")]
    [Arguments("/logout", "events")]
    [Arguments("/my/profile", "events")]
    [Arguments("/my/reviews", "events")]
    [Arguments("/my/reports", "events")]
    [Arguments("/settings", "settings")]
    [Arguments("/settings/personal", "settings")]
    [Arguments("/settings/personal/appearance", "settings")]
    [Arguments("https://example.test/settings/personal/appearance", "settings")]
    [Arguments("/settings/instance", "settings")]
    [Arguments("/settings/admin", "settings")]
    [Arguments("/settings/organization/123", "settings")]
    [Arguments("/settings/group/123", "settings")]
    [Arguments("/ai", "ai")]
    [Arguments("/ai/chats/01912345-6789-7abc-8def-0123456789ab", "ai")]
    [Arguments("https://example.test/ai/chats/01912345-6789-7abc-8def-0123456789ab?source=dock", "ai")]
    [Arguments("/my/organizations", "events")]
    [Arguments("/users/123/organizations", "events")]
    [Arguments("/organizations/create", "events")]
    [Arguments("/organization/success", "events")]
    [Arguments("/organization/123/members", "events")]
    [Arguments("/organization/profile/123", "events")]
    [Arguments("/organization/reviews/123", "events")]
    [Arguments("/organization/123", "events")]
    [Arguments("/group/profile/123", "events")]
    [Arguments("/events/create", "events")]
    [Arguments("/events/123/edit", "events")]
    [Arguments("/events/123/moderation/reports", "events")]
    [Arguments("/events/123/sessions/create", "events")]
    [Arguments("/events/123/sessions/456/edit", "events")]
    [Arguments("/events/123/sessions/456", "events")]
    [Arguments("/event-created/123", "events")]
    [Arguments("/organizations/123/events/create", "events")]
    [Arguments("/events/summer-festival-ABCD", "events")]
    [Arguments("/about", "events")]
    [Arguments("/contact", "events")]
    [Arguments("/community-guidelines", "events")]
    [Arguments("/privacy", "events")]
    [Arguments("/terms", "events")]
    [Arguments("/admin/organization/123", "events")]
    [Arguments("/admin/instance", "events")]
    [Arguments("/admin/instance/tenants", "events")]
    [Arguments("/admin/instance/tenants/123/configuration", "events")]
    [Arguments("/admin/instance/plans", "events")]
    [Arguments("/admin/instance/plans/community", "events")]
    [Arguments("/admin/instance/domains", "events")]
    [Arguments("/admin/instance/operations", "events")]
    public async Task ClassifyMapsCurrentRouteFamilies(string route, string expectedWorkspace)
    {
        var workspace = _classifier.Classify(route);

        await Assert.That(workspace.Value).IsEqualTo(expectedWorkspace);
    }

    [Test]
    public async Task ClassifyRequiresRouteSegmentBoundary()
    {
        var workspace = _classifier.Classify("/settings-preview");

        await Assert.That(workspace).IsEqualTo(WorkspaceKey.Events);
    }
}
