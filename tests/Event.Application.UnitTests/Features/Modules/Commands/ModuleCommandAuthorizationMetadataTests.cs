using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Features.Modules.Requests.Commands;

namespace Event.Application.UnitTests.Features.Modules.Commands;

public sealed class ModuleCommandAuthorizationMetadataTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Test]
    [Arguments(typeof(EnableTenantModuleCommand))]
    [Arguments(typeof(DisableTenantModuleCommand))]
    public async Task TenantModuleWriteCommandsRequireTenantUpdatePermission(Type commandType)
    {
        var attribute = commandType.GetCustomAttribute<AuthorizeResourceAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.Resource).IsEqualTo(ResourceKinds.Tenant);
        await Assert.That(attribute.Action).IsEqualTo(AuthorizationActions.Update);
        await Assert.That(typeof(ISecureRequest).IsAssignableFrom(commandType)).IsTrue();
    }

    [Test]
    public async Task EnableCommandExposesTenantAuthorizationContext()
    {
        ISecureRequest command = new EnableTenantModuleCommand
        {
            TenantId = TenantId,
            ModuleKey = "Mod_Tech"
        };

        // The module key and the enable/disable intent are payload, not authority: both commands ask the
        // same question, "may this caller administer this tenant".
        await Assert.That(command.ResourceId).IsEqualTo(TenantId.ToString("D"));
        await Assert.That(command.AuthorizationFacts)
            .IsEqualTo(new TenantScopedAuthorizationFacts(TenantId));
    }

    [Test]
    public async Task DisableCommandExposesTenantAuthorizationContext()
    {
        ISecureRequest command = new DisableTenantModuleCommand
        {
            TenantId = TenantId,
            ModuleKey = "Mod_Islamic"
        };

        await Assert.That(command.ResourceId).IsEqualTo(TenantId.ToString("D"));
        await Assert.That(command.AuthorizationFacts)
            .IsEqualTo(new TenantScopedAuthorizationFacts(TenantId));
    }
}
