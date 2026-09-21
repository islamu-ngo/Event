using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Services;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class OperatorIdentityMetadataTests
{
    private const string Path = "/api/operator-identity-metadata";

    [Test]
    public async Task Anonymous_cannot_read_metadata()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(Path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Authenticated_metadata_uses_canonical_codes_and_constraints()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        Authenticate(client, Guid.CreateVersion7());
        using var response = await client.GetAsync(Path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var codes = json.GetProperty("operatorKinds").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!).ToHashSet(StringComparer.Ordinal);
        await Assert.That(codes.SetEquals(TenantDirectoryOperatorKinds.All)).IsTrue();
        await Assert.That(json.GetProperty("countryState").GetString()).IsEqualTo("Available");
        var countries = json.GetProperty("countries").EnumerateArray().ToArray();
        await Assert.That(countries.Any(item => item.GetProperty("code").GetString() == "GB")).IsTrue();
        await Assert.That(countries.Select(item => item.GetProperty("code").GetString()).Distinct().Count())
            .IsEqualTo(countries.Length);
        var fields = json.GetProperty("fields").EnumerateArray()
            .ToDictionary(field => field.GetProperty("name").GetString()!);
        await Assert.That(fields["legalName"].GetProperty("maxLength").GetInt32())
            .IsEqualTo(TenantDirectoryOperatorIdentity.MaxLegalNameLength);
        await Assert.That(fields["registrationIdentifier"].GetProperty("requiredForDisclosure").GetBoolean()).IsFalse();
        await Assert.That(fields["termsUrl"].GetProperty("requiredForDisclosure").GetBoolean()).IsFalse();
        await Assert.That(fields["termsUrl"].GetProperty("requiredForPaidCommerce").GetBoolean()).IsTrue();
        await Assert.That(fields.Values.All(field => field.GetProperty("labelId").GetString()!.Length > 0
            && field.GetProperty("helpId").GetString()!.Length > 0)).IsTrue();
        await Assert.That(json.GetProperty("registrationAuthorityState").GetString()).IsEqualTo("NotSupported");
        await Assert.That(json.GetProperty("registrationAuthorities").GetArrayLength()).IsEqualTo(0);
    }

    [Test]
    public async Task Metadata_links_offer_refresh_without_identity_write_authority()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        Authenticate(client, Guid.CreateVersion7());
        using var response = await client.GetAsync(Path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var links = json.GetProperty("_links");
        await Assert.That(links.GetProperty("self").GetProperty("href").GetString()).IsEqualTo(Path);
        await Assert.That(links.GetProperty("refresh").GetProperty("href").GetString()).IsEqualTo(Path);
        await Assert.That(links.TryGetProperty("update", out _)).IsFalse();
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
    }

    [Test]
    public async Task Metadata_payload_is_independent_of_authenticated_identity_and_secret_input()
    {
        using var factory = new OnboardingWebApplicationFactory();
        using var client = factory.CreateClient();
        var firstId = Guid.CreateVersion7();
        Authenticate(client, firstId);
        using var first = await client.GetAsync(Path);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string original = await first.Content.ReadAsStringAsync();
        var sentinel = Guid.CreateVersion7().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<InstanceOperatorIdentityService>();
            var saved = await identity.SaveAsync(new InstanceOperatorIdentitySettings
            {
                PublicName = sentinel,
                LegalName = sentinel,
                RegistrationIdentifier = sentinel,
                PublicContactEmail = $"{sentinel}@example.test"
            }, null, CancellationToken.None);
            await Assert.That(saved.IsSuccess).IsTrue();
            var persisted = await identity.GetCurrentAsync(CancellationToken.None);
            await Assert.That(persisted.Settings?.LegalName).IsEqualTo(sentinel);
        }
        Authenticate(client, Guid.CreateVersion7());
        client.DefaultRequestHeaders.Add("X-Setup-Secret", OnboardingWebApplicationFactory.SetupSecret);
        using var second = await client.GetAsync(Path);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string changed = await second.Content.ReadAsStringAsync();
        await Assert.That(changed).IsEqualTo(original);
        await Assert.That(changed.Contains(sentinel, StringComparison.Ordinal)).IsFalse();
        await Assert.That(changed.Contains(firstId.ToString(), StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(changed.Contains(OnboardingWebApplicationFactory.SetupSecret, StringComparison.Ordinal)).IsFalse();
        using var document = JsonDocument.Parse(changed);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet();
        await Assert.That(names.SetEquals(["operatorKinds", "countries", "countryState",
            "registrationAuthorities", "registrationAuthorityState", "fields", "_links"])).IsTrue();
    }

    private static void Authenticate(HttpClient client, Guid userId)
    {
        client.DefaultRequestHeaders.Remove(TestAuthHandler.AuthHeaderName);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(userId, "Metadata reader"));
    }
}
