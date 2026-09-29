namespace Event.Application.UnitTests.Features.SetupLive;

using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Application.Features.SetupLive;

public sealed class SetupLiveApplyContractTests
{
    [Test]
    public async Task LiveImportComposesTheExistingSessionAndApplyServices()
    {
        Type[] dependencies = typeof(SetupConfigurationImportApplicationService)
            .GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();

        await Assert.That(dependencies).IsEquivalentTo(
        [
            typeof(SetupLiveApplicationService),
            typeof(ConfigurationImportSessionApplicationService),
            typeof(ConfigurationImportApplyService)
        ]);
        await Assert.That(typeof(SetupConfigurationImportApplicationService)
            .GetMethod("PreviewAsync")!.ReturnType)
            .IsEqualTo(typeof(Task<ConfigurationImportPreviewResult>));
        await Assert.That(typeof(SetupConfigurationImportApplicationService)
            .GetMethod("ApplyAsync")!.ReturnType)
            .IsEqualTo(typeof(Task<ConfigurationImportOperationResult>));
    }
}
