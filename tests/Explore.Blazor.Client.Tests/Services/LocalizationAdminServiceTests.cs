using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class LocalizationAdminServiceTests
{
    private readonly ILocalizationAdminClient _api = Substitute.For<ILocalizationAdminClient>();

    [Test]
    public async Task GetConfigurationAsync_WhenApiSucceeds_ReturnsConfiguration()
    {
        var expected = new LocalizationConfigDto
        {
            DefaultLanguage = "en",
            TmsProvider = "tolgee",
            EnabledLanguages = ["en", "fr"],
            FallbackLanguage = "en"
        };
        _api.GetLocalizationConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateService().GetConfigurationAsync();

        await Assert.That(result).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task GetConfigurationAsync_WhenApiFails_ReturnsNull()
    {
        _api.GetLocalizationConfigurationAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<LocalizationConfigDto>>(_ => throw new HttpRequestException("network failed"));

        var result = await CreateService().GetConfigurationAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task TestConnectionAsync_WhenApiSucceeds_ReturnsSuccess()
    {
        var result = await CreateService().TestConnectionAsync();

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("TMS connection OK.");
        await _api.Received(1).TestLocalizationTmsConnectionAsync(
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TestConnectionAsync_WhenApiFails_ReturnsReason()
    {
        _api.TestLocalizationTmsConnectionAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new HttpRequestException("network failed"));

        var result = await CreateService().TestConnectionAsync();

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Message).Contains("network failed");
    }

    [Test]
    public async Task ExportFromTmsAsync_ForwardsLanguageCode()
    {
        var result = await CreateService().ExportFromTmsAsync("fr");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Message).IsEqualTo("Exported translations for 'fr'.");
        await _api.Received(1).ExportLocalizationFromTmsAsync(
            "fr",
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExportBundleAsync_ReturnsGeneratedDictionary()
    {
        IDictionary<string, string> expected = new Dictionary<string, string>
        {
            ["ui.common.appName"] = "ISLAMU Event"
        };
        _api.ExportLocalizationBundleAsync(
                "en",
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateService().ExportBundleAsync("en");

        await Assert.That(result).IsNotNull();
        await Assert.That(result!["ui.common.appName"]).IsEqualTo("ISLAMU Event");
    }

    [Test]
    public async Task ImportBundleAsync_ForwardsGeneratedBundleRequest()
    {
        _api.ImportLocalizationBundleAsync(
                Arg.Any<ImportLocalizationBundleDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Imported." });
        var translations = new Dictionary<string, string>
        {
            ["ui.common.appName"] = "ISLAMU Event"
        };

        var result = await CreateService().ImportBundleAsync("en", translations);

        await Assert.That(result.Success).IsTrue();
        await _api.Received(1).ImportLocalizationBundleAsync(
            Arg.Is<ImportLocalizationBundleDto>(request =>
                request.LanguageCode == "en" && request.Translations["ui.common.appName"] == "ISLAMU Event"),
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task UpdateGovernanceAsync_ForwardsGeneratedPayload()
    {
        _api.UpdateLocalizationGovernanceAsync(
                Arg.Any<UpdateLocalizationGovernanceDto>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Saved." });
        var payload = new UpdateLocalizationGovernanceDto
        {
            Runtime = new LocalizationRuntimeUpdateDto
            {
                ClientPickerEnabled = true,
                ForceOfflineMode = true
            }
        };

        var result = await CreateService().UpdateGovernanceAsync(payload);

        await Assert.That(result.Success).IsTrue();
        await _api.Received(1).UpdateLocalizationGovernanceAsync(
            payload,
            Arg.Any<string?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetBundlePathHealthAsync_ReturnsGeneratedHealth()
    {
        var expected = new WritablePathHealth
        {
            Exists = true,
            Writable = false,
            Reason = "Permission denied",
            TargetPath = "/app/App_Data/Localization/Bundles"
        };
        _api.CheckLocalizationBundleHealthAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await CreateService().GetBundlePathHealthAsync();

        await Assert.That(result).IsSameReferenceAs(expected);
        await Assert.That(result!.Writable).IsFalse();
    }

    private LocalizationAdminService CreateService() =>
        new(_api, NullLogger<LocalizationAdminService>.Instance);
}
