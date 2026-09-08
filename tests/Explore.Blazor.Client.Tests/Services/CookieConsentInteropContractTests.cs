namespace Explore.Blazor.Client.Tests.Services;

public class CookieConsentInteropContractTests
{
    private static readonly Type InterfaceType = typeof(ICookieConsentInterop);

    [Test]
    public async Task ICookieConsentInterop_HasExactly3Methods()
    {
        var methods = InterfaceType.GetMethods();

        await Assert.That(methods).Count().IsEqualTo(3);
    }

    [Test]
    public async Task ReadConsentAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("ReadConsentAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task<string?>));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(1);
        await Assert.That(parameters[0].Name).IsEqualTo("consentCookieKey");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task WriteConsentAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("WriteConsentAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(3);
        await Assert.That(parameters[0].Name).IsEqualTo("consentCookieKey");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[1].Name).IsEqualTo("value");
        await Assert.That(parameters[1].ParameterType).IsEqualTo(typeof(string));
        await Assert.That(parameters[2].Name).IsEqualTo("lifetimeDays");
        await Assert.That(parameters[2].ParameterType).IsEqualTo(typeof(int));
    }

    [Test]
    public async Task ClearConsentAsync_HasCorrectSignature()
    {
        var method = InterfaceType.GetMethod("ClearConsentAsync");

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));

        var parameters = method.GetParameters();
        await Assert.That(parameters).Count().IsEqualTo(1);
        await Assert.That(parameters[0].Name).IsEqualTo("consentCookieKey");
        await Assert.That(parameters[0].ParameterType).IsEqualTo(typeof(string));
    }

    [Test]
    public async Task AllMethods_AcceptConsentCookieKeyAsFirstParameter()
    {
        var methods = InterfaceType.GetMethods();

        foreach (var method in methods)
        {
            var firstParam = method.GetParameters().FirstOrDefault();
            await Assert.That(firstParam).IsNotNull();
            await Assert.That(firstParam!.Name).IsEqualTo("consentCookieKey");
            await Assert.That(firstParam.ParameterType).IsEqualTo(typeof(string));
        }
    }

    [Test]
    public async Task ReadConsentAsync_ReturnsNullableString()
    {
        // ReadConsentAsync should return Task<string?> — null means no consent recorded
        var method = InterfaceType.GetMethod("ReadConsentAsync");
        var returnType = method!.ReturnType;

        await Assert.That(returnType).IsEqualTo(typeof(Task<string?>));
    }

    [Test]
    public async Task WriteAndClear_ReturnPlainTask()
    {
        var write = InterfaceType.GetMethod("WriteConsentAsync");
        var clear = InterfaceType.GetMethod("ClearConsentAsync");

        await Assert.That(write!.ReturnType).IsEqualTo(typeof(Task));
        await Assert.That(clear!.ReturnType).IsEqualTo(typeof(Task));
    }
}
