using Explore.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Configuration;

public sealed class RuntimeMappingDependencyTests
{
    [Test]
    public async Task Application_HasNoCompiledAutoMapperDependency()
    {
        var references = typeof(ApplicationServicesRegistration).Assembly.GetReferencedAssemblies();
        await Assert.That(references.Any(reference => reference.Name == "AutoMapper")).IsFalse();
    }

    [Test]
    public async Task ApplicationComposition_HasNoAutoMapperServiceRegistrations()
    {
        var services = new ServiceCollection();
        services.ConfigureApplicationServices(new ConfigurationBuilder().Build());
        await Assert.That(services.Any(descriptor =>
            descriptor.ServiceType.Assembly.GetName().Name == "AutoMapper")).IsFalse();
    }
}
