using AutoMapper;
using AutoMapper.Internal;
using Explore.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Configuration;

public sealed class AutoMapperSecurityConfigurationTests
{
    [Test]
    public async Task ConfigureApplicationServices_AppliesDepthCeilingToEveryTypeMap()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.ConfigureApplicationServices(configuration);

        await using var serviceProvider = services.BuildServiceProvider();
        var mapperConfiguration = serviceProvider.GetRequiredService<AutoMapper.IConfigurationProvider>();
        var typeMaps = mapperConfiguration.Internal().GetAllTypeMaps();

        await Assert.That(typeMaps).IsNotEmpty();
        await Assert.That(typeMaps.All(typeMap => typeMap.MaxDepth == 64)).IsTrue();
    }
}
