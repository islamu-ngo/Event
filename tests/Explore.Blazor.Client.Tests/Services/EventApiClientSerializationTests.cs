using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Webhooks;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Models.Responses;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Serialization;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class EventApiClientSerializationTests
{
    private static readonly Type[] AuthorityFreeRequestBodies =
    [
        typeof(CreateCategoryDto),
        typeof(ImportEventRequestDto),
        typeof(CreateEventSessionDto),
        typeof(CreateEventSessionAgendaItemDto),
        typeof(CreateEventSessionLanguageDto),
        typeof(CreateEventSessionSpeakerDto),
        typeof(CreateLocationDto),
        typeof(CreateTagDto),
    ];

    [Test]
    public async Task RegeneratedWriteBodiesRemainGeneratedAndWithoutTenantAuthority()
    {
        foreach (var bodyType in AuthorityFreeRequestBodies)
        {
            await Assert.That(bodyType.GetCustomAttribute<System.CodeDom.Compiler.GeneratedCodeAttribute>()).IsNotNull();
            await Assert.That(bodyType.GetProperty("TenantId")).IsNull();
        }

        var json = JsonSerializer.Serialize(new CreateCategoryDto
        {
            MasterCode = "COMMUNITY",
            FullName = "Community"
        });

        await Assert.That(json).Contains("\"masterCode\":\"COMMUNITY\"");
        await Assert.That(json).DoesNotContain("tenantId");
    }

    [Test]
    public async Task GeneratedNominalContractsUseRecordAndInitSemantics()
    {
        string[] recordNames = GetGeneratedRecordTypeNames();
        Type[] candidates = recordNames
            .Select(name => typeof(ActorDto).Assembly.GetType(
                $"{typeof(ActorDto).Namespace}.{name}",
                throwOnError: true)!)
            .ToArray();
        string[] classDebt = candidates
            .Where(type => !IsRecord(type))
            .Select(type => type.FullName!)
            .ToArray();
        string[] setterDebt = candidates
            .SelectMany(type => type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(property => property.SetMethod?.IsPublic == true
                && property.GetCustomAttribute<
                    System.Text.Json.Serialization.JsonExtensionDataAttribute>()
                    is null
                && !property.SetMethod.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Contains(typeof(IsExternalInit)))
            .Select(property => $"{property.DeclaringType!.FullName}.{property.Name}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(classDebt).IsEmpty()
            .Because($"plain generated nominal contracts must be records; first debt: {string.Join(", ", classDebt.Take(20))}");
        await Assert.That(setterDebt).IsEmpty()
            .Because($"generated record properties must be init-only; first debt: {string.Join(", ", setterDebt.Take(20))}");
    }

    [Test]
    public async Task FrameworkSensitiveGeneratedContractsRemainClasses()
    {
        Type[] protectedTypes =
        [
            typeof(PublicExperienceClient),
            typeof(ApiException),
            typeof(ApiException<>),
            typeof(FileResponse),
            typeof(FileContentResult),
            typeof(HalResourceOfActorDto),
            typeof(HalCollectionResourceOfActorListDto),
            typeof(PatchTenantFooterSettingsDto),
            typeof(UpdateCategoryDto),
        ];

        string[] accidentalRecords = protectedTypes
            .Where(IsRecord)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(accidentalRecords).IsEmpty();
        await Assert.That(typeof(PatchTenantFooterSettingsDto)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.SetMethod?.IsPublic == true
                    && !property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit))))
            .IsTrue();

        Type[] mutableStateTypes = LoadMutableGeneratedContractNames()
            .Select(name => typeof(ActorDto).Assembly.GetType(
                $"{typeof(ActorDto).Namespace}.{name}",
                throwOnError: true)!)
            .ToArray();
        await Assert.That(mutableStateTypes.Where(IsRecord)).IsEmpty();
        await Assert.That(mutableStateTypes.All(type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Any(property => property.SetMethod?.IsPublic == true
                    && !property.SetMethod.ReturnParameter
                        .GetRequiredCustomModifiers()
                        .Contains(typeof(IsExternalInit)))))
            .IsTrue();
    }

    [Test]
    public async Task GeneratedRecordSupportsWithCopyAndAotJsonRoundTrip()
    {
        Guid originalId = Guid.CreateVersion7();
        Guid replacementId = Guid.CreateVersion7();
        var original = new ActorDto { Id = originalId };

        ActorDto replacement = original with { Id = replacementId };
        string json = JsonSerializer.Serialize(
            replacement,
            AppJsonSerializerContext.Default.ActorDto);
        ActorDto? restored = JsonSerializer.Deserialize(
            json,
            AppJsonSerializerContext.Default.ActorDto);

        await Assert.That(original.Id).IsEqualTo(originalId);
        await Assert.That(replacement.Id).IsEqualTo(replacementId);
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Id).IsEqualTo(replacementId);
    }

    [Test]
    public async Task GeneratedRecordAotRoundTripPreservesUnknownHalExtensionData()
    {
        Guid actorId = Guid.CreateVersion7();
        string json =
            $$"""
              {
                "id": "{{actorId}}",
                "_links": {
                  "self": {
                    "href": "/api/actor/{{actorId}}"
                  }
                }
              }
              """;

        ActorDto? restored = JsonSerializer.Deserialize(
            json,
            AppJsonSerializerContext.Default.ActorDto);
        string roundTripped = JsonSerializer.Serialize(
            restored,
            AppJsonSerializerContext.Default.ActorDto);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.AdditionalProperties)
            .ContainsKey("_links");
        await Assert.That(roundTripped).Contains("\"_links\"");
        await Assert.That(roundTripped)
            .Contains($"/api/actor/{actorId}");
    }

    [Test]
    public async Task ImmutablePresentationResultsUseValueEqualityAndSnapshotCollections()
    {
        var first = ServiceResult<string>.Success("saved");
        var second = ServiceResult<string>.Success("saved");
        await Assert.That(first).IsEqualTo(second);

        var items = new List<string> { "first" };
        var page = new PaginatedResult<string>
        {
            Items = items,
            PageNumber = 1,
            PageSize = 20,
            TotalCount = 1
        };
        items.Add("forged");

        await Assert.That(page.Items).IsEquivalentTo(["first"]);
    }

    [Test]
    public async Task WebhookSnapshotsDefensivelyCopyPublishedCollections()
    {
        var eventTypes = new List<WebhookEventTypeDto>();
        var first = new WebhookManagementSnapshot { EventTypes = eventTypes };
        eventTypes.Add(new WebhookEventTypeDto());

        await Assert.That(first.EventTypes).IsEmpty();
    }

    [Test]
    public async Task EventDraftEditModelRemainsMutableFormState()
    {
        var editState = new EventDraftEditModel { Title = "Draft" };
        editState.Title = "Revised";

        await Assert.That(editState.Title).IsEqualTo("Revised");
    }

    [Test]
    public async Task PersistedEventDetailAndSaveResultsUseImmutableVariants()
    {
        var sessions = new List<EventSessionListDto>();
        var original = new EventDetail.EventDetailState
        {
            EventId = Guid.CreateVersion7(),
            EventSessions = sessions
        };
        var revised = original with { PrimarySession = new EventSessionListDto() };
        sessions.Add(new EventSessionListDto());

        await Assert.That(original.EventSessions).IsEmpty();
        await Assert.That(original.PrimarySession).IsNull();
        await Assert.That(revised.PrimarySession).IsNotNull();
        await Assert.That(TenantBrandingSettingsSaveResult.Failed("denied"))
            .IsEqualTo(TenantBrandingSettingsSaveResult.Failed("denied"));
    }

    [Test]
    public async Task AotContextRoundTripsLocalRecordWithoutProviderCredentialMetadata()
    {
        var snapshot = new DockLayoutSnapshot(
            "main",
            [],
            new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

        var json = LocalStorageDockLayoutPersistence.Serialize(snapshot);
        var restored = LocalStorageDockLayoutPersistence.Deserialize("main", json, NullLogger.Instance);

        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.LayoutKey).IsEqualTo(snapshot.LayoutKey);
        await Assert.That(restored.UpdatedAt).IsEqualTo(snapshot.UpdatedAt);
        await Assert.That(restored.Panels).IsEmpty();
        await Assert.That(AppJsonSerializerContext.Default.GetTypeInfo(typeof(DockLayoutStorageEnvelope))).IsNotNull();
        await Assert.That(AppJsonSerializerContext.Default.GetTypeInfo(typeof(ReportingProviderCredentialsUpdateDto))).IsNull();
    }

    [Test]
    public async Task GetHomeDiscoveryAsyncDeserializesStringEnumDictionaryValues()
    {
        const string responseBody = """
            {
              "schemaVersion": 1,
              "context": {
                "mode": "All",
                "selectedAreaDisplayName": "All events",
                "availableAreas": []
              },
              "hero": [],
              "upcomingInArea": [],
              "mostViewedInArea": [],
              "mostViewedOnline": [],
              "curatedSections": [],
              "recentlyAdded": [],
              "sectionStatuses": {
                "hero": "Available"
              },
              "generatedAtUtc": "2026-07-16T10:00:00Z"
            }
            """;
        using var httpClient = new HttpClient(new StaticResponseHandler(responseBody))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new PublicExperienceClient(httpClient);

        var result = await client.GetHomeDiscoveryAsync();

        await Assert.That(result.SectionStatuses).IsNotNull();
        await Assert.That(result.SectionStatuses!["hero"]).IsEqualTo(HomeDiscoverySectionStatus.Available);
        await Assert.That(result.Context?.Mode).IsEqualTo(HomeDiscoveryMode.All);
    }

    [Test]
    public async Task GetInstanceAtprotoFederationSettingsAsyncDeserializesStringSettingSource()
    {
        const string responseBody = """
            {
              "category": "AtprotoFederation",
              "settings": [
                {
                  "key": "atproto.eventPublishing.capability",
                  "value": "Enabled",
                  "settingValueTypeId": 1,
                  "settingValueTypeCode": "String",
                  "settingValueTypeName": "String",
                  "source": "SystemLocked",
                  "isLocked": true,
                  "isLockable": true,
                  "canEdit": true
                }
              ],
              "_links": {}
            }
            """;
        using var httpClient = new HttpClient(new StaticResponseHandler(responseBody))
        {
            BaseAddress = new Uri("https://example.test/")
        };
        var client = new SettingsClient(httpClient);

        var result = await client.GetInstanceAtprotoFederationSettingsAsync();

        var setting = await Assert.That(result.Settings).HasSingleItem();
        await Assert.That(setting.Source).IsEqualTo(SettingSource.SystemLocked);
    }

    private sealed class StaticResponseHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod(
            "<Clone>$",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
        is not null;

    private static string[] GetGeneratedRecordTypeNames()
    {
        string sourcePath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs"));
        const string declaration = "public partial record class ";
        return File.ReadLines(sourcePath)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(
                declaration,
                StringComparison.Ordinal))
            .Select(line => line[declaration.Length..]
                .Split(
                    [' ', '<'],
                    StringSplitOptions.RemoveEmptyEntries)[0])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static HashSet<string> LoadMutableGeneratedContractNames()
    {
        string policyPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt"));
        return File.ReadAllLines(policyPath)
            .Select(line => line.Trim())
            .Where(line => line.Length != 0
                && !line.StartsWith('#'))
            .ToHashSet(StringComparer.Ordinal);
    }

}
