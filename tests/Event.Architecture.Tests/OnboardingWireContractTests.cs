using System.Text.Json;
using System.Text.Json.Serialization;
using Client = Explore.Blazor.Client.Clients;
using Server = Explore.Application.DTOs.Onboarding;

namespace Event.Architecture.Tests;

public sealed class OnboardingWireContractTests
{
    private static readonly JsonSerializerOptions ClientOptions = Client.EventApiJsonSerializerSettings.Configure(new());
    private static readonly JsonSerializerOptions ApiOptions = CreateApiOptions();

    [Test]
    public async Task GeneratedCompletionBody_BindsToStrictApiContract()
    {
        var request = new Client.CompleteInstanceOnboardingRequest
        {
            DeploymentMode = Client.DeploymentMode.SingleTenant,
            ExpectedJourneyGeneration = "reviewed-generation",
            AdministrationAccessMode = "Embedded",
            SiteProfile = new()
            {
                SiteName = "Community", CanonicalUrl = "https://events.example.test",
                Locale = "en", TimeZone = "UTC"
            }
        };
        var json = JsonSerializer.Serialize(request, ClientOptions);
        var bound = JsonSerializer.Deserialize<Server.CompleteInstanceOnboardingRequest>(json, ApiOptions);

        await Assert.That(bound!.SiteProfile.SiteName).IsEqualTo("Community");
        await Assert.That(bound.ExpectedJourneyGeneration).IsEqualTo("reviewed-generation");

        var profileJson = JsonSerializer.Serialize(request.SiteProfile, ClientOptions);
        var profile = JsonSerializer.Deserialize<Server.SelfHostOnboardingProfileDto>(profileJson, ApiOptions);
        await Assert.That(profile!.CanonicalUrl).IsEqualTo("https://events.example.test");

        var localJson = JsonSerializer.Serialize(new Client.CompleteLocalInstanceOnboardingRequestDto
        {
            OperationId = Guid.CreateVersion7(), Settings = request
        }, ClientOptions);
        var local = JsonSerializer.Deserialize<Server.CompleteLocalInstanceOnboardingRequestDto>(localJson, ApiOptions);
        await Assert.That(local!.Settings.SiteProfile.SiteName).IsEqualTo("Community");
    }

    [Test]
    public async Task ReadOnlyJourneyMetadata_IsNotAcceptedInSubmittedProfile()
    {
        var profile = new Client.SelfHostOnboardingProfileDto { SiteName = "Community" };
        profile.AdditionalProperties["canonicalUrlManagedByDeployment"] = true;
        var json = JsonSerializer.Serialize(profile, ClientOptions);

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Server.SelfHostOnboardingProfileDto>(json, ApiOptions));
        await Task.CompletedTask;
    }

    private static JsonSerializerOptions CreateApiOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.TypeInfoResolverChain.Add(Explore.Application.Serialization.ExploreJsonContext.Default);
        options.TypeInfoResolverChain.Add(new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver());
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
