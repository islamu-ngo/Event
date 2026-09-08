using System.Reflection;

namespace Explore.Blazor.Client.Tests.Services;

public class AnalyticsInteropContractTests
{
    private static readonly Type InterfaceType = typeof(IAnalyticsInterop);

    [Test]
    public async Task IAnalyticsInterop_HasExactly6Methods()
    {
        var methods = InterfaceType.GetMethods();

        await Assert.That(methods).Count().IsEqualTo(6);
    }

    [Test]
    public async Task InitAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("InitAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(8);
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));    // analyticsProvider
        await Assert.That(parameters[1].ParameterType).IsEqualTo(typeof(bool));      // analyticsEnabled
        await Assert.That(parameters[2].ParameterType).IsEqualTo(typeof(string));    // analyticsConsentMode
        await Assert.That(parameters[3].ParameterType).IsEqualTo(typeof(string));    // analyticsTransportMode
        await Assert.That(parameters[4].ParameterType).IsEqualTo(typeof(bool));      // allowIdentify
        await Assert.That(parameters[5].ParameterType).IsEqualTo(typeof(string));    // apiKey (nullable)
        await Assert.That(parameters[6].ParameterType).IsEqualTo(typeof(string));    // endpointUrl (nullable)
    }

    [Test]
    public async Task TrackAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("TrackAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(2);
        await Assert.That(parameters[0].Name).IsEqualTo("eventName");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[1].Name).IsEqualTo("properties");
    }

    [Test]
    public async Task IdentifyAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("IdentifyAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(2);
        await Assert.That(parameters[0].Name).IsEqualTo("distinctId");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task PageViewAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("PageViewAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(2);
        await Assert.That(parameters[0].Name).IsEqualTo("pagePath");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task OptInCapturingAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("OptInCapturingAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));
        await Assert.That(method.GetParameters()).Count().IsEqualTo(0);
    }

    [Test]
    public async Task OptOutCapturingAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("OptOutCapturingAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));
        await Assert.That(method.GetParameters()).Count().IsEqualTo(0);
    }

    [Test]
    public async Task AllMethods_ReturnTask()
    {
        var methods = InterfaceType.GetMethods();

        foreach (var method in methods)
        {
            await Assert.That(method.ReturnType).IsEqualTo(typeof(Task));
        }
    }

    [Test]
    public async Task ConsentMethods_ExistForOptInAndOptOut()
    {
        // Verify the consent-specific methods exist (added for cookie consent support)
        var optIn = InterfaceType.GetMethod("OptInCapturingAsync");
        var optOut = InterfaceType.GetMethod("OptOutCapturingAsync");

        await Assert.That(optIn).IsNotNull();
        await Assert.That(optOut).IsNotNull();
    }
}
