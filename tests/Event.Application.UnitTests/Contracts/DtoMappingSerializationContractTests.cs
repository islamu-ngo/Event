using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AutoMapper;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Analytics;
using Explore.Application.DTOs.ContactShareConsent;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.DTOs.Footer;
using Explore.Application.DTOs.Organization;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.DTOs.Settings;
using Explore.Application.DTOs.Studio;
using Explore.Application.Hateoas;
using Explore.Application.Profiles;
using Explore.Application.Responses;
using Explore.Application.Serialization;
using Explore.Domain;

namespace Event.Application.UnitTests.Contracts;

public sealed class DtoMappingSerializationContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task PositionalCandidate_ConstructionPreservesItsSingleFact()
    {
        var dto = ConstructFromNamedFacts<UpdateOrganizationApprovalStatusDto>(
            (nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId), 7));

        await Assert.That(dto.ApprovalStatusId).IsEqualTo(7);
    }

    [Test]
    public async Task PositionalCandidate_EquivalentValueIsFoundByAConsumingSet()
    {
        var first = ConstructFromNamedFacts<UpdateOrganizationApprovalStatusDto>(
            (nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId), 7));
        var equivalent = ConstructFromNamedFacts<UpdateOrganizationApprovalStatusDto>(
            (nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId), 7));
        var consumed = new HashSet<UpdateOrganizationApprovalStatusDto> { first };

        await Assert.That(consumed.Contains(equivalent)).IsTrue();
    }

    [Test]
    public async Task PositionalCandidate_EquivalentOneFactVariantLeavesOriginalUnchanged()
    {
        var original = ConstructFromNamedFacts<UpdateOrganizationApprovalStatusDto>(
            (nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId), 7));

        var variant = CreateEquivalentOneFactVariant(
            original,
            nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId),
            9);

        await Assert.That(original.ApprovalStatusId).IsEqualTo(7);
        await Assert.That(variant.ApprovalStatusId).IsEqualTo(9);
    }

    [Test]
    public async Task NominalCandidate_EquivalentValueIsFoundByAConsumingDictionary()
    {
        Guid id = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();
        OrganizationDto first = CreateOrganizationDto(id, concurrencyStamp);
        OrganizationDto equivalent = CreateOrganizationDto(id, concurrencyStamp);
        var consumed = new Dictionary<OrganizationDto, string> { [first] = "mapped" };

        await Assert.That(consumed.TryGetValue(equivalent, out string? value)).IsTrue();
        await Assert.That(value).IsEqualTo("mapped");
    }

    [Test]
    public async Task NestedCollectionCandidate_EquivalentShallowSnapshotIsFoundByAConsumingSet()
    {
        var settings = new FooterSettingsDto { Enabled = true, Template = "compact" };
        IReadOnlyList<FooterLinkGroupDto> groups =
        [
            new FooterLinkGroupDto
            {
                Id = Guid.CreateVersion7(),
                Title = "Legal",
                Order = 1,
                Links =
                [
                    new FooterLinkItemDto
                    {
                        Id = Guid.CreateVersion7(),
                        Label = "Privacy",
                        Url = "/privacy",
                        IsActive = true,
                        Order = 1
                    }
                ]
            }
        ];
        var first = new FooterConfigDto { Settings = settings, LinkGroups = groups };
        var equivalentSnapshot = new FooterConfigDto { Settings = settings, LinkGroups = groups };
        var consumed = new HashSet<FooterConfigDto> { first };

        await Assert.That(consumed.Contains(equivalentSnapshot)).IsTrue();
    }

    [Test]
    public async Task CandidateSnapshots_DoNotExposePostConstructionFactMutation()
    {
        (Type Type, string Property)[] representativeFacts =
        [
            (typeof(UpdateOrganizationApprovalStatusDto), nameof(UpdateOrganizationApprovalStatusDto.ApprovalStatusId)),
            (typeof(OrganizationDto), nameof(OrganizationDto.FullName)),
            (typeof(FooterConfigDto), nameof(FooterConfigDto.LinkGroups)),
            (typeof(FooterLinkGroupDto), nameof(FooterLinkGroupDto.Links)),
            (typeof(PatchFooterGovernanceSettingsDto), nameof(PatchFooterGovernanceSettingsDto.LockTenantTemplate))
        ];

        string[] mutableFacts = representativeFacts
            .Where(fact => SupportsPostConstructionAssignment(fact.Type, fact.Property))
            .Select(fact => $"{fact.Type.Name}.{fact.Property}")
            .ToArray();

        await Assert.That(mutableFacts).IsEmpty();
    }

    [Test]
    public async Task AutoMapper_MapsOrganizationFactsIntoTheNominalSnapshot()
    {
        Guid id = Guid.CreateVersion7();
        Guid concurrencyStamp = Guid.CreateVersion7();
        var source = new Organization
        {
            Id = id,
            ConcurrencyStamp = concurrencyStamp,
            WebsiteUrl = "https://community.example.test",
            Pii = new OrganizationPii
            {
                FullName = "Community Association",
                Email = "hello@example.test",
                Country = "BE",
                City = "Brussels",
                Postcode = "1000",
                Address = "Main Square 1"
            }
        };

        OrganizationDto mapped = CreateMapper().Map<OrganizationDto>(source);

        await Assert.That(mapped.Id).IsEqualTo(id);
        await Assert.That(mapped.ConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(mapped.FullName).IsEqualTo("Community Association");
        await Assert.That(mapped.Email).IsEqualTo("hello@example.test");
        await Assert.That(mapped.WebsiteUrl).IsEqualTo("https://community.example.test");
        await Assert.That(mapped.Country).IsEqualTo("BE");
        await Assert.That(mapped.City).IsEqualTo("Brussels");
        await Assert.That(mapped.Postcode).IsEqualTo("1000");
        await Assert.That(mapped.Address).IsEqualTo("Main Square 1");
    }

    [Test]
    public async Task SystemTextJson_RoundTripsRequiredFactsAndExplicitNullableNull()
    {
        OrganizationDto source = CreateOrganizationDto(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            websiteUrl: null);

        string json = JsonSerializer.Serialize(source, JsonOptions);
        OrganizationDto roundTrip = JsonSerializer.Deserialize<OrganizationDto>(json, JsonOptions)!;

        await Assert.That(roundTrip.Id).IsEqualTo(source.Id);
        await Assert.That(roundTrip.ConcurrencyStamp).IsEqualTo(source.ConcurrencyStamp);
        await Assert.That(roundTrip.FullName).IsEqualTo(source.FullName);
        await Assert.That(roundTrip.Email).IsEqualTo(source.Email);
        await Assert.That(roundTrip.WebsiteUrl).IsNull();
    }

    [Test]
    public async Task SystemTextJson_MissingRequiredFactIsRejected()
    {
        JsonObject payload = JsonSerializer.SerializeToNode(
            CreateOrganizationDto(Guid.CreateVersion7(), Guid.CreateVersion7()),
            JsonOptions)!.AsObject();
        payload.Remove("fullName");

        await Assert.That(() => JsonSerializer.Deserialize<OrganizationDto>(payload, JsonOptions))
            .Throws<JsonException>();
    }

    [Test]
    public async Task HalResource_RoundTripsFlattenedNominalDataLinksAndNulls()
    {
        OrganizationDto dto = CreateOrganizationDto(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            websiteUrl: null);
        var resource = new HalResource<OrganizationDto>(dto)
            .WithSelfLink($"/api/organizations/{dto.Id}");

        string json = JsonSerializer.Serialize(resource, JsonOptions);
        HalResource<OrganizationDto> roundTrip =
            JsonSerializer.Deserialize<HalResource<OrganizationDto>>(json, JsonOptions)!;
        using var document = JsonDocument.Parse(json);

        await Assert.That(document.RootElement.TryGetProperty("data", out _)).IsFalse();
        await Assert.That(document.RootElement.GetProperty("fullName").GetString()).IsEqualTo(dto.FullName);
        await Assert.That(document.RootElement.GetProperty("websiteUrl").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(roundTrip.Data.Id).IsEqualTo(dto.Id);
        await Assert.That(roundTrip.Data.WebsiteUrl).IsNull();
        await Assert.That(roundTrip.Links[LinkRelations.Self].Href)
            .IsEqualTo($"/api/organizations/{dto.Id}");
    }

    [Test]
    public async Task PaginatedResult_RoundTripsItemsAndDerivedPageSemantics()
    {
        OrganizationListDto item = CreateOrganizationListDto(Guid.CreateVersion7(), Guid.CreateVersion7());
        PaginatedResult<OrganizationListDto> page = PaginatedResult<OrganizationListDto>.Create(
            [item],
            totalCount: 21,
            pageNumber: 2,
            pageSize: 10);

        string json = JsonSerializer.Serialize(page, JsonOptions);
        PaginatedResult<OrganizationListDto> roundTrip =
            JsonSerializer.Deserialize<PaginatedResult<OrganizationListDto>>(json, JsonOptions)!;

        await Assert.That(roundTrip.Items.Count).IsEqualTo(1);
        await Assert.That(roundTrip.Items[0].Id).IsEqualTo(item.Id);
        await Assert.That(roundTrip.PageNumber).IsEqualTo(2);
        await Assert.That(roundTrip.PageSize).IsEqualTo(10);
        await Assert.That(roundTrip.TotalCount).IsEqualTo(21);
        await Assert.That(roundTrip.TotalPages).IsEqualTo(3);
        await Assert.That(roundTrip.HasPreviousPage).IsTrue();
        await Assert.That(roundTrip.HasNextPage).IsTrue();
    }

    [Test]
    public async Task PatchJson_DistinguishesOmittedExplicitClearAndReplacement()
    {
        UpdateOrganizationDto omitted = JsonSerializer.Deserialize<UpdateOrganizationDto>("{}", JsonOptions)!;
        UpdateOrganizationDto clear = JsonSerializer.Deserialize<UpdateOrganizationDto>(
            """
            {
              "websiteUrl": {
                "value": { "hasValue": true, "value": null }
              }
            }
            """,
            JsonOptions)!;
        UpdateOrganizationDto replacement = JsonSerializer.Deserialize<UpdateOrganizationDto>(
            """
            {
              "websiteUrl": {
                "value": { "hasValue": true, "value": "https://new.example.test" }
              }
            }
            """,
            JsonOptions)!;

        await Assert.That(omitted.WebsiteUrl).IsNull();
        await Assert.That(clear.WebsiteUrl).IsNotNull();
        await Assert.That(clear.WebsiteUrl!.Value.HasValue).IsTrue();
        await Assert.That(clear.WebsiteUrl.Value.Value).IsNull();
        await Assert.That(replacement.WebsiteUrl).IsNotNull();
        await Assert.That(replacement.WebsiteUrl!.Value.HasValue).IsTrue();
        await Assert.That(replacement.WebsiteUrl.Value.Value)
            .IsEqualTo("https://new.example.test");
    }

    [Test]
    public async Task PlatformDtoCollectionInputsAreDetachedFromCallerMutation()
    {
        var scopes = new List<string> { "events:read" };
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["mode"] = "all" };
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["path"] = JsonSerializer.SerializeToElement("/events")
        };
        var sections = new List<PublicExperienceEventSectionDto>
        {
            new() { Key = "upcoming", Label = "Upcoming" }
        };

        var key = new CreateExternalApiKeyDto { Name = "automation", Scopes = scopes };
        var batch = new UpdateSettingBatchDto { Values = values };
        var analytics = new RelayAnalyticsEventDto { Properties = properties };
        var shell = new PublicExperienceShellDto { EventSections = sections };

        scopes.Add("events:write");
        values["mode"] = "area";
        properties["extra"] = JsonSerializer.SerializeToElement(true);
        sections.Clear();

        await Assert.That(key.Scopes).IsEquivalentTo(["events:read"]);
        await Assert.That(batch.Values["mode"]).IsEqualTo("all");
        await Assert.That(analytics.Properties.ContainsKey("extra")).IsFalse();
        await Assert.That(shell.EventSections.Count).IsEqualTo(1);
    }

    [Test]
    public async Task PlatformDtoBinaryHalAndSetInputsRetainWireAndComparerSemantics()
    {
        byte[] bytes = [1, 2, 3];
        var links = new Dictionary<string, HalLink>(StringComparer.Ordinal)
        {
            [LinkRelations.Self] = new() { Href = "/actions/1" }
        };
        var relations = new HashSet<string>(StringComparer.Ordinal) { "edit" };
        var export = new SharedContactExportResultDto { FileContent = bytes };
        var action = new AiProposedActionDto { Links = links };
        var studio = new StudioContextDto { AllowedLinkRelations = relations };

        bytes[0] = 9;
        links.Clear();
        relations.Add("EDIT");

        await Assert.That(export.FileContent!.Value.Span[0]).IsEqualTo((byte)1);
        await Assert.That(action.Links!.ContainsKey(LinkRelations.Self)).IsTrue();
        await Assert.That(studio.AllowedLinkRelations.Contains("EDIT")).IsFalse();

        string json = JsonSerializer.Serialize(export);
        using var document = JsonDocument.Parse(json);
        await Assert.That(document.RootElement.GetProperty("FileContent").GetString()).IsEqualTo("AQID");
    }

    [Test]
    public async Task SourceGeneratedDiscoveryJsonRetainsReadOnlyExtensionData()
    {
        const string json = """{"source":"remote","futureField":{"enabled":true}}""";

        EventDiscoveryItemDto item = JsonSerializer.Deserialize(
            json,
            ExploreJsonContext.Default.EventDiscoveryItemDto)!;
        string roundTrip = JsonSerializer.Serialize(item, ExploreJsonContext.Default.EventDiscoveryItemDto);

        await Assert.That(item.AdditionalProperties.ContainsKey("futureField")).IsTrue();
        await Assert.That(((ICollection<KeyValuePair<string, object>>)item.AdditionalProperties).IsReadOnly).IsTrue();
        using var document = JsonDocument.Parse(roundTrip);
        await Assert.That(document.RootElement.GetProperty("futureField").GetProperty("enabled").GetBoolean()).IsTrue();
    }

    [Test]
    public async Task PlatformDtoCollectionsRetainNullAndEmptyDefaults()
    {
        CreateExternalApiKeyDto nullable = JsonSerializer.Deserialize<CreateExternalApiKeyDto>(
            """{"Name":"automation","Scopes":null}""")!;
        var defaults = new CreateExternalApiKeyDto { Name = "automation" };

        await Assert.That(nullable.Scopes).IsNull();
        await Assert.That(defaults.Scopes).IsEmpty();
    }

    private static T ConstructFromNamedFacts<T>(params (string Name, object? Value)[] facts)
        where T : class
    {
        Type type = typeof(T);
        ConstructorInfo? positionalConstructor = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length == facts.Length
                    && parameters.Select(parameter => parameter.Name)
                        .SequenceEqual(facts.Select(fact => fact.Name), StringComparer.OrdinalIgnoreCase);
            });

        if (positionalConstructor is not null)
        {
            return (T)positionalConstructor.Invoke(facts.Select(fact => fact.Value).ToArray());
        }

        T instance = Activator.CreateInstance<T>();
        foreach ((string name, object? value) in facts)
        {
            type.GetProperty(name)!.SetValue(instance, value);
        }

        return instance;
    }

    private static T CreateEquivalentOneFactVariant<T>(T source, string propertyName, object? value)
        where T : class
    {
        JsonObject snapshot = JsonSerializer.SerializeToNode(source, JsonOptions)!.AsObject();
        string jsonPropertyName = JsonOptions.PropertyNamingPolicy!.ConvertName(propertyName);
        snapshot[jsonPropertyName] = JsonSerializer.SerializeToNode(value, JsonOptions);
        return snapshot.Deserialize<T>(JsonOptions)!;
    }

    private static bool SupportsPostConstructionAssignment(Type type, string propertyName)
    {
        MethodInfo? setter = type.GetProperty(propertyName)?.SetMethod;
        return setter is { IsPublic: true }
            && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
    }

    private static OrganizationDto CreateOrganizationDto(
        Guid id,
        Guid concurrencyStamp,
        string? websiteUrl = "https://community.example.test") => new()
    {
        Id = id,
        ConcurrencyStamp = concurrencyStamp,
        FullName = "Community Association",
        WebsiteUrl = websiteUrl,
        Email = "hello@example.test",
        Country = "BE",
        City = "Brussels",
        Postcode = "1000",
        Address = "Main Square 1",
        ApprovalStatusId = 2,
        ApprovalStatusFullName = "Approved",
        TenantId = Guid.Parse("0198e6f8-4ab9-7e27-a772-d4bc29ab6ad4")
    };

    private static OrganizationListDto CreateOrganizationListDto(Guid id, Guid concurrencyStamp) => new()
    {
        Id = id,
        ConcurrencyStamp = concurrencyStamp,
        TenantId = Guid.Parse("0198e6f8-4ab9-7e27-a772-d4bc29ab6ad4"),
        FullName = "Community Association",
        WebsiteUrl = null,
        Email = "hello@example.test",
        Country = "BE",
        City = "Brussels",
        Postcode = "1000",
        Address = "Main Square 1",
        ApprovalStatusId = 2,
        ApprovalStatusFullName = "Approved"
    };

    private static IMapper CreateMapper()
    {
#if USE_COMMERCIAL_LUCKYPENNY_LIBS
        var configuration = new MapperConfiguration(
            cfg => cfg.AddProfile<OrganizationMappingProfile>(),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
#else
        var configuration = new MapperConfiguration(cfg => cfg.AddProfile<OrganizationMappingProfile>());
#endif
        return configuration.CreateMapper();
    }
}
