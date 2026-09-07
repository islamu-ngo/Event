using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Features.Footer.Requests.Commands;

namespace Event.Application.UnitTests.Features.Footer.Commands;

public class FooterCommandAuthorizationMetadataTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid LinkId = Guid.NewGuid();

    [Test]
    [Arguments(typeof(CreateFooterLinkGroupCommand))]
    [Arguments(typeof(UpdateFooterLinkGroupCommand))]
    [Arguments(typeof(DeleteFooterLinkGroupCommand))]
    [Arguments(typeof(ReorderFooterLinkGroupsCommand))]
    [Arguments(typeof(CreateFooterLinkCommand))]
    [Arguments(typeof(UpdateFooterLinkCommand))]
    [Arguments(typeof(DeleteFooterLinkCommand))]
    [Arguments(typeof(PatchTenantFooterSettingsCommand))]
    public async Task TenantScopedFooterWriteCommandsRequireTenantUpdatePermission(Type commandType)
    {
        var attribute = commandType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(attribute.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(commandType)).IsTrue();
    }

    public static IEnumerable<(ISecureRequest Command, string? ExpectedContextKey)> TenantScopedFooterCommands()
    {
        yield return (new CreateFooterLinkGroupCommand { UserId = UserId, TenantId = TenantId, Title = "Main" }, null);
        yield return (new UpdateFooterLinkGroupCommand
        {
            TenantId = TenantId,
            GroupId = GroupId,
            Update = new()
            {
                Title = new() { Value = "Main" },
                IsActive = new() { Value = true }
            }
        }, "groupId");
        yield return (new DeleteFooterLinkGroupCommand { UserId = UserId, TenantId = TenantId, GroupId = GroupId }, "groupId");
        yield return (new ReorderFooterLinkGroupsCommand { UserId = UserId, TenantId = TenantId, OrderedGroupIds = [GroupId] }, null);
        yield return (new CreateFooterLinkCommand { UserId = UserId, TenantId = TenantId, GroupId = GroupId, Label = "Home", Url = "https://example.test", OpenInNewTab = false }, "groupId");
        yield return (new UpdateFooterLinkCommand
        {
            TenantId = TenantId,
            LinkId = LinkId,
            Update = new()
            {
                Label = new() { Value = "Home" },
                Url = new() { Value = "https://example.test" },
                OpenInNewTab = new() { Value = false },
                IsActive = new() { Value = true }
            }
        }, "linkId");
        yield return (new DeleteFooterLinkCommand { UserId = UserId, TenantId = TenantId, LinkId = LinkId }, "linkId");
        yield return (new PatchTenantFooterSettingsCommand
        {
            UserId = UserId,
            TenantId = TenantId,
            Patch = new()
            {
                General = new()
                {
                    Enabled = Explore.Application.Models.Common.OptionalUpdate<bool>.Set(true)
                }
            }
        }, "settingGroup");
    }

    [Test]
    [MethodDataSource(nameof(TenantScopedFooterCommands))]
    public async Task TenantScopedFooterWriteCommandsExposeTenantAuthorizationContext(
        (ISecureRequest Command, string? ExpectedContextKey) testCase)
    {
        var (command, _) = testCase;

        // Footer links and groups are tenant-administered; the specific link identifies the row being
        // edited, not a separate authority zone.
        await Assert.That(command.ResourceId).IsEqualTo(TenantId.ToString("D"));
        await Assert.That(command.AuthorizationFacts)
            .IsEqualTo(new TenantScopedAuthorizationFacts(TenantId));
    }
}
