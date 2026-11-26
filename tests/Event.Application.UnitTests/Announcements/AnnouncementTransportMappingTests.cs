using System.Text.Json;
using System.Text.Json.Nodes;
using Explore.Application.Features.Announcements;
using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.Announcements;

namespace Event.Application.UnitTests.Announcements;

public sealed class AnnouncementTransportMappingTests
{
    private static readonly Guid Tenant = Guid.Parse("019a3b40-0000-7000-8000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("019a3b40-0000-7000-8000-000000000002");
    private static readonly Guid Edition = Guid.Parse("019a3b40-0000-7000-8000-000000000003");
    private static readonly Guid InstanceEpoch = Guid.Parse("019a3b40-0000-7000-8000-000000000004");
    private static readonly Guid TenantEpoch = Guid.Parse("019a3b40-0000-7000-8000-000000000005");
    private static readonly Guid PreviousEdition = Guid.Parse("019a3b40-0000-7000-8000-000000000006");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SourceRoundTripPreservesFactoryOwnerMaterialEditionEpochAndLock(bool tenantOwner)
    {
        var owner = tenantOwner ? AnnouncementOwner.ForTenant(Tenant) : AnnouncementOwner.Instance;
        var source = Source(owner);
        var wire = AnnouncementTransportMapper.ToDto(source);
        var serialized = JsonSerializer.Serialize(wire, Json);
        var restored = JsonSerializer.Deserialize<AnnouncementSourceDto>(serialized, Json)
            ?? throw new InvalidOperationException("Source transport must deserialize.");

        await Assert.That(AnnouncementTransportMapper.ToNative(restored)).IsEqualTo(source);
    }

    [Test]
    public async Task AbsentSourceRoundTripRetainsExplicitAbsenceWithoutCreatingAGroup()
    {
        var source = new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(Tenant), null, false);
        var wire = AnnouncementTransportMapper.ToDto(source);
        var node = JsonSerializer.SerializeToNode(wire, Json)?.AsObject()
            ?? throw new InvalidOperationException("Source transport must serialize.");

        await Assert.That(node.ContainsKey("group")).IsTrue();
        await Assert.That(node["group"]).IsNull();
        await Assert.That(AnnouncementTransportMapper.ToNative(wire)).IsEqualTo(source);
    }

    [Test]
    [Arguments("publish")]
    [Arguments("set-enabled")]
    [Arguments("set-lock")]
    [Arguments("remove-override")]
    public async Task ClosedChangeRoundTripPreservesOnlyItsNativeFields(string kind)
    {
        var owner = AnnouncementOwner.ForTenant(Tenant);
        var input = new AnnouncementUpdateDto
        {
            ExpectedSource = AnnouncementTransportMapper.ToDto(Source(owner)),
            Change = kind switch
            {
                "publish" => new()
                {
                    Kind = kind,
                    Material = Material(),
                    Enabled = true,
                    Redisplay = false
                },
                "set-enabled" => new() { Kind = kind, Enabled = false },
                "set-lock" => new() { Kind = kind, IsLocked = true },
                "remove-override" => new() { Kind = kind },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            }
        };
        var restored = JsonSerializer.Deserialize<AnnouncementUpdateDto>(
            JsonSerializer.Serialize(input, Json), Json)
            ?? throw new InvalidOperationException("Update transport must deserialize.");

        var native = AnnouncementTransportMapper.ToNative(restored, owner);

        await Assert.That(native.Expected).IsEqualTo(Source(owner));
        await Assert.That(AnnouncementTransportMapper.ToDto(native)).IsEqualTo(input);
    }

    [Test]
    [Arguments("unknown")]
    [Arguments("missing-material")]
    [Arguments("missing-enabled")]
    [Arguments("missing-redisplay")]
    [Arguments("publish-with-lock")]
    [Arguments("enable-with-material")]
    [Arguments("lock-with-enabled")]
    [Arguments("remove-with-redisplay")]
    public void MalformedOrUnknownChangeCannotReachANativeVariant(string attack)
    {
        var material = Material();
        AnnouncementChangeDto change = attack switch
        {
            "unknown" => new AnnouncementChangeDto { Kind = "replace-everything" },
            "missing-material" => new() { Kind = "publish", Enabled = true, Redisplay = false },
            "missing-enabled" => new() { Kind = "publish", Material = material, Redisplay = false },
            "missing-redisplay" => new() { Kind = "publish", Material = material, Enabled = true },
            "publish-with-lock" => new()
            {
                Kind = "publish",
                Material = material,
                Enabled = true,
                Redisplay = false,
                IsLocked = false
            },
            "enable-with-material" => new() { Kind = "set-enabled", Enabled = true, Material = material },
            "lock-with-enabled" => new() { Kind = "set-lock", IsLocked = true, Enabled = false },
            "remove-with-redisplay" => new() { Kind = "remove-override", Redisplay = false },
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };

        Assert.Throws<ArgumentException>(() => AnnouncementTransportMapper.ToNative(change));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void ExpectedSourceCannotSubstituteInstanceOrAnotherTenantForSelectedOwner(bool instance)
    {
        var input = new AnnouncementUpdateDto
        {
            Change = new() { Kind = "set-lock", IsLocked = false },
            ExpectedSource = AnnouncementTransportMapper.ToDto(
                Source(instance ? AnnouncementOwner.Instance : AnnouncementOwner.ForTenant(OtherTenant)))
        };

        Assert.Throws<ArgumentException>(() =>
            AnnouncementTransportMapper.ToNative(input, AnnouncementOwner.ForTenant(Tenant)));
    }

    [Test]
    public void ObservationCannotNameAnotherTenantsOwner()
    {
        var input = AnnouncementTransportMapper.ToDto(Observation()) with
        {
            Owner = new() { TenantId = OtherTenant }
        };

        Assert.Throws<ArgumentException>(() => AnnouncementTransportMapper.ToNative(input));
    }

    [Test]
    public void InstancePublicationCannotBeRemovedAsATenantOverride()
    {
        var input = new AnnouncementUpdateDto
        {
            Change = new() { Kind = "remove-override" },
            ExpectedSource = AnnouncementTransportMapper.ToDto(Source(AnnouncementOwner.Instance))
        };

        Assert.Throws<ArgumentException>(() =>
            AnnouncementTransportMapper.ToNative(input, AnnouncementOwner.Instance));
    }

    [Test]
    [Arguments("empty-owner")]
    [Arguments("empty-edition")]
    [Arguments("empty-epoch")]
    [Arguments("enabled-without-material")]
    [Arguments("locked-absent-source")]
    public void InvalidSourceUsesNativeValueInvariants(string attack)
    {
        var input = AnnouncementTransportMapper.ToDto(Source(AnnouncementOwner.ForTenant(Tenant)));
        var group = input.Group ?? throw new InvalidOperationException("Fixture source must have a group.");
        var publication = group.Publication
            ?? throw new InvalidOperationException("Fixture source must have publication material.");
        input = attack switch
        {
            "empty-owner" => input with { Owner = new() { TenantId = Guid.Empty } },
            "empty-edition" => input with
            {
                Group = group with { Publication = publication with { Edition = Guid.Empty } }
            },
            "empty-epoch" => input with { Group = group with { SelectionEpoch = Guid.Empty } },
            "enabled-without-material" => input with { Group = group with { Publication = null } },
            "locked-absent-source" => input with { Group = null, IsLocked = true },
            _ => throw new ArgumentOutOfRangeException(nameof(attack))
        };

        Assert.Throws<ArgumentException>(() => AnnouncementTransportMapper.ToNative(input));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DismissalRoundTripPreservesWholeWitnessAndExpectedMarker(bool hasMarker)
    {
        var observed = Observation();
        var marker = hasMarker ? new AnnouncementDismissalMarker(observed) : null;
        var input = new AnnouncementDismissRequestDto
        {
            Observed = AnnouncementTransportMapper.ToDto(observed),
            ExpectedMarker = marker is null ? null : AnnouncementTransportMapper.ToDto(marker)
        };
        var restored = JsonSerializer.Deserialize<AnnouncementDismissRequestDto>(
            JsonSerializer.Serialize(input, Json), Json)
            ?? throw new InvalidOperationException("Dismissal transport must deserialize.");

        var native = AnnouncementTransportMapper.ToNative(restored, Tenant);

        await Assert.That(restored.Observed.SourceWitness.InstanceSelectionEpoch).IsEqualTo(InstanceEpoch);
        await Assert.That(restored.Observed.SourceWitness.InstanceIsLocked).IsFalse();
        await Assert.That(restored.Observed.SourceWitness.TenantSelectionEpoch).IsEqualTo(TenantEpoch);
        await Assert.That(restored.Observed.SourceWitness.TenantIsLocked).IsTrue();
        await Assert.That(native.Observed).IsEqualTo(observed);
        await Assert.That(native.ExpectedMarker).IsEqualTo(marker);
    }

    [Test]
    public async Task ExpectedMarkerCanRetainAPreviousOwnerAndEditionWithinTheSelectedTenant()
    {
        var previous = new AnnouncementDismissalMarker(new(Tenant, AnnouncementOwner.Instance,
            PreviousEdition, new(InstanceEpoch, false, null, false)));
        var input = new AnnouncementDismissRequestDto
        {
            Observed = AnnouncementTransportMapper.ToDto(Observation()),
            ExpectedMarker = AnnouncementTransportMapper.ToDto(previous)
        };

        var native = AnnouncementTransportMapper.ToNative(input, Tenant);

        await Assert.That(native.ExpectedMarker).IsEqualTo(previous);
        await Assert.That(native.Observed).IsEqualTo(Observation());
    }

    [Test]
    [Arguments("observation")]
    [Arguments("marker")]
    public void DismissalCannotSubstituteAnotherTenant(string attack)
    {
        var observed = AnnouncementTransportMapper.ToDto(Observation());
        var foreign = observed with
        {
            TenantId = OtherTenant,
            Owner = new() { TenantId = OtherTenant }
        };
        var input = new AnnouncementDismissRequestDto
        {
            Observed = attack == "observation" ? foreign : observed,
            ExpectedMarker = attack == "marker" ? new() { Observation = foreign } : null
        };

        Assert.Throws<ArgumentException>(() => AnnouncementTransportMapper.ToNative(input, Tenant));
    }

    [Test]
    public async Task ExpectedAbsentMarkerMustBeExplicitAndSurvivesNullOmissionOptions()
    {
        var input = new AnnouncementDismissRequestDto
        {
            Observed = AnnouncementTransportMapper.ToDto(Observation()),
            ExpectedMarker = null
        };
        var node = JsonSerializer.SerializeToNode(input, Json)?.AsObject()
            ?? throw new InvalidOperationException("Dismissal transport must serialize.");

        await Assert.That(node.ContainsKey("expectedMarker")).IsTrue();
        await Assert.That(node["expectedMarker"]).IsNull();
        node.Remove("expectedMarker");

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnnouncementDismissRequestDto>(node.ToJsonString(), Json));
    }

    [Test]
    [Arguments("request")]
    [Arguments("owner")]
    [Arguments("witness")]
    public void UnknownRequestAndNestedFieldsAreRejected(string target)
    {
        var input = new AnnouncementDismissRequestDto
        {
            Observed = AnnouncementTransportMapper.ToDto(Observation()),
            ExpectedMarker = null
        };
        var node = JsonSerializer.SerializeToNode(input, Json)?.AsObject()
            ?? throw new InvalidOperationException("Dismissal transport must serialize.");
        var selected = target switch
        {
            "request" => node,
            "owner" => node["observed"]!["owner"]!.AsObject(),
            "witness" => node["observed"]!["sourceWitness"]!.AsObject(),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        selected["actorUserId"] = OtherTenant;

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<AnnouncementDismissRequestDto>(node.ToJsonString(), Json));
    }

    [Test]
    public async Task PublicProjectionContainsMaterialAndObservationWithoutPrivateMarker()
    {
        var observed = Observation();
        var resolution = new AnnouncementResolution(observed, new("Message", "Details", "/events"), true);

        var wire = AnnouncementTransportMapper.ToDto(resolution, Tenant);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(wire, Json));

        await Assert.That(wire.Observation).IsEqualTo(AnnouncementTransportMapper.ToDto(observed));
        await Assert.That(wire.Material).IsEqualTo(Material());
        await Assert.That(wire.IsVisible).IsTrue();
        await Assert.That(json.RootElement.TryGetProperty("marker", out _)).IsFalse();
        await Assert.That(json.RootElement.TryGetProperty("expectedMarker", out _)).IsFalse();
    }

    [Test]
    public async Task PrivateSelfProjectionRetainsCurrentObservationAndMarker()
    {
        var observed = Observation();
        var marker = new AnnouncementDismissalMarker(observed);

        var wire = AnnouncementTransportMapper.ToSelfStateDto(Tenant, observed, marker);
        var restored = JsonSerializer.Deserialize<AnnouncementSelfStateDto>(
            JsonSerializer.Serialize(wire, Json), Json)
            ?? throw new InvalidOperationException("Self transport must deserialize.");

        await Assert.That(restored.TenantId).IsEqualTo(Tenant);
        await Assert.That(AnnouncementTransportMapper.ToNative(restored.CurrentObservation!)).IsEqualTo(observed);
        await Assert.That(AnnouncementTransportMapper.ToNative(restored.Marker!)).IsEqualTo(marker);
    }

    [Test]
    public async Task AbsentPublicAndPrivatePublicationsRoundTripWithExplicitNulls()
    {
        var publicState = AnnouncementTransportMapper.ToDto(new AnnouncementResolution(null, null, false), Tenant);
        var selfState = AnnouncementTransportMapper.ToSelfStateDto(Tenant, null, null);
        string publicPayload = JsonSerializer.Serialize(publicState, Json);
        string selfPayload = JsonSerializer.Serialize(selfState, Json);
        using var publicJson = JsonDocument.Parse(publicPayload);
        using var selfJson = JsonDocument.Parse(selfPayload);

        await Assert.That(publicJson.RootElement.GetProperty("observation").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(publicJson.RootElement.GetProperty("material").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(selfJson.RootElement.GetProperty("currentObservation").ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(selfJson.RootElement.GetProperty("marker").ValueKind).IsEqualTo(JsonValueKind.Null);

        var restoredPublic = JsonSerializer.Deserialize<AnnouncementPublicStateDto>(publicPayload, Json)
            ?? throw new InvalidOperationException("Absent public transport must deserialize.");
        var restoredSelf = JsonSerializer.Deserialize<AnnouncementSelfStateDto>(selfPayload, Json)
            ?? throw new InvalidOperationException("Absent self transport must deserialize.");

        await Assert.That(restoredPublic).IsEqualTo(publicState);
        await Assert.That(restoredSelf).IsEqualTo(selfState);
    }

    private static AnnouncementMaterialDto Material() => new()
    {
        Message = "Message",
        LinkText = "Details",
        LinkUrl = "/events"
    };

    private static AnnouncementSourceSnapshot Source(AnnouncementOwner owner) =>
        new(owner, new(TenantEpoch, true, new(Edition, new(" Message ", " Details ", " /events "))), true);

    private static AnnouncementObservation Observation() =>
        new(Tenant, AnnouncementOwner.ForTenant(Tenant), Edition,
            new(InstanceEpoch, false, TenantEpoch, true));
}
