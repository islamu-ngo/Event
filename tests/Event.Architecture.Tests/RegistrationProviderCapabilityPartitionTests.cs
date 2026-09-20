using System.Reflection;
using Explore.API.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Event.Architecture.Tests;

public sealed class RegistrationProviderCapabilityPartitionTests
{
    [Test]
    public async Task ProviderManagement_HasFourConcreteCapabilityOwners()
    {
        const string prefix = "api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers";
        Type[] owners = typeof(EventControllerBase).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type)
                && type.GetCustomAttributes<RouteAttribute>().Any(route => route.Template == prefix))
            .ToArray();

        await Assert.That(owners.Length).IsEqualTo(4);
        await Assert.That(owners.All(type => !type.IsGenericType)).IsTrue();
    }
}
