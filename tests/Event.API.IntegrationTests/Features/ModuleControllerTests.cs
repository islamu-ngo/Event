using System.Reflection;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModuleControllerTests
{
    [Test]
    [Arguments(nameof(ModuleController.EnableModule))]
    [Arguments(nameof(ModuleController.DisableModule))]
    public async Task ModuleMutationEndpointsUseWriteRateLimitPolicy(string actionName)
    {
        var action = typeof(ModuleController).GetMethod(actionName);

        await Assert.That(action).IsNotNull();
        await Assert.That(action!.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName)
            .IsEqualTo(RateLimitingExtensions.WritePolicy);
    }
}
