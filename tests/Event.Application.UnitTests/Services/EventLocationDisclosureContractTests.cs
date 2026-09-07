using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.DTOs.Location;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Services;

public sealed class EventLocationDisclosureContractTests
{
    private static readonly IReadOnlyDictionary<EventLocationDisclosureField, EventLocationDisclosureFieldClass> ExpectedClasses =
        new Dictionary<EventLocationDisclosureField, EventLocationDisclosureFieldClass>
        {
            [EventLocationDisclosureField.Country] = EventLocationDisclosureFieldClass.Baseline,
            [EventLocationDisclosureField.Timezone] = EventLocationDisclosureFieldClass.Baseline,
            [EventLocationDisclosureField.City] = EventLocationDisclosureFieldClass.ContextSensitive,
            [EventLocationDisclosureField.VenueName] = EventLocationDisclosureFieldClass.ContextSensitive,
            [EventLocationDisclosureField.RoomName] = EventLocationDisclosureFieldClass.ContextSensitive,
            [EventLocationDisclosureField.RoomDescription] = EventLocationDisclosureFieldClass.ManagementOnly,
            [EventLocationDisclosureField.StreetAddress] = EventLocationDisclosureFieldClass.ExactSensitive,
            [EventLocationDisclosureField.Postcode] = EventLocationDisclosureFieldClass.ExactSensitive,
            [EventLocationDisclosureField.Latitude] = EventLocationDisclosureFieldClass.ExactSensitive,
            [EventLocationDisclosureField.Longitude] = EventLocationDisclosureFieldClass.ExactSensitive,
            [EventLocationDisclosureField.FormattedAddress] = EventLocationDisclosureFieldClass.ExactSensitiveDerivative,
            [EventLocationDisclosureField.MapUrl] = EventLocationDisclosureFieldClass.ExactSensitiveDerivative,
            [EventLocationDisclosureField.Geohash] = EventLocationDisclosureFieldClass.ExactSensitiveDerivative,
            [EventLocationDisclosureField.AccessInstructions] = EventLocationDisclosureFieldClass.RestrictedOperationalSecret,
            [EventLocationDisclosureField.EntryDetails] = EventLocationDisclosureFieldClass.RestrictedOperationalSecret,
            [EventLocationDisclosureField.DoorCode] = EventLocationDisclosureFieldClass.RestrictedOperationalSecret
        };

    [Test]
    public async Task FieldVectors_ClassifyEveryFieldExactlyOnce()
    {
        var vectors = EventLocationDisclosureContract.FieldVectors;

        await Assert.That(vectors.Count).IsEqualTo(Enum.GetValues<EventLocationDisclosureField>().Length);
        foreach (var expected in ExpectedClasses)
        {
            await Assert.That(vectors[expected.Key].FieldClass).IsEqualTo(expected.Value);
        }
    }

    [Test]
    public async Task PurposeCeilings_MatchFrozenRouteContract()
    {
        var everyRoute = new[]
        {
            EventLocationDisclosureField.Country,
            EventLocationDisclosureField.Timezone,
            EventLocationDisclosureField.City,
            EventLocationDisclosureField.VenueName,
            EventLocationDisclosureField.RoomName,
            EventLocationDisclosureField.StreetAddress,
            EventLocationDisclosureField.Postcode,
            EventLocationDisclosureField.Latitude,
            EventLocationDisclosureField.Longitude,
            EventLocationDisclosureField.FormattedAddress,
            EventLocationDisclosureField.MapUrl,
            EventLocationDisclosureField.Geohash
        };

        foreach (var purpose in Enum.GetValues<EventLocationDisclosurePurpose>())
        {
            foreach (var field in everyRoute)
            {
                await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(purpose, field)).IsTrue();
            }
        }

        await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureField.RoomDescription)).IsFalse();
        await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(
            EventLocationDisclosurePurpose.Attendee,
            EventLocationDisclosureField.RoomDescription)).IsFalse();
        await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(
            EventLocationDisclosurePurpose.Management,
            EventLocationDisclosureField.RoomDescription)).IsTrue();
    }

    [Test]
    public async Task PolicySelectedFields_ReuseDomainPolicyFlags()
    {
        var expected = new Dictionary<EventLocationDisclosureField, EventLocationDisclosureFields>
        {
            [EventLocationDisclosureField.Country] = EventLocationDisclosureFields.Country,
            [EventLocationDisclosureField.City] = EventLocationDisclosureFields.City,
            [EventLocationDisclosureField.VenueName] = EventLocationDisclosureFields.VenueName,
            [EventLocationDisclosureField.RoomName] = EventLocationDisclosureFields.RoomName,
            [EventLocationDisclosureField.StreetAddress] = EventLocationDisclosureFields.StreetAddress,
            [EventLocationDisclosureField.Postcode] = EventLocationDisclosureFields.Postcode,
            [EventLocationDisclosureField.Latitude] = EventLocationDisclosureFields.Coordinates,
            [EventLocationDisclosureField.Longitude] = EventLocationDisclosureFields.Coordinates
        };

        foreach (var pair in expected)
        {
            await Assert.That(EventLocationDisclosureContract.FieldVectors[pair.Key].PolicySelection).IsEqualTo(pair.Value);
        }
    }

    [Test]
    public async Task DerivedAndTimezoneFields_RequireExplicitAuthorityGates()
    {
        var vectors = EventLocationDisclosureContract.FieldVectors;

        await Assert.That(vectors[EventLocationDisclosureField.FormattedAddress].PolicyGate)
            .IsEqualTo(EventLocationDisclosurePolicyGate.DerivedFromSourcePolicy);
        await Assert.That(vectors[EventLocationDisclosureField.FormattedAddress].SourceAuthoritySelection)
            .IsEqualTo(EventLocationDisclosureFields.StreetAddress);
        await Assert.That(vectors[EventLocationDisclosureField.MapUrl].SourceAuthoritySelection)
            .IsEqualTo(EventLocationDisclosureFields.Coordinates);
        await Assert.That(vectors[EventLocationDisclosureField.Geohash].SourceAuthoritySelection)
            .IsEqualTo(EventLocationDisclosureFields.Coordinates);
        await Assert.That(vectors[EventLocationDisclosureField.Timezone].PolicyGate)
            .IsEqualTo(EventLocationDisclosurePolicyGate.UnavailableUntilExplicitTimezonePolicy);
        await Assert.That(vectors[EventLocationDisclosureField.Timezone].PolicySelection).IsNull();
        await Assert.That(EventLocationDisclosureContract.HasCurrentlySatisfiablePolicyGate(
            EventLocationDisclosureField.Timezone)).IsFalse();
    }

    [Test]
    public async Task EveryDerivedVector_UsesContractOwnedSourceAuthorityValidation()
    {
        var derivedVectors = EventLocationDisclosureContract.FieldVectors.Values
            .Where(vector => vector.PolicyGate == EventLocationDisclosurePolicyGate.DerivedFromSourcePolicy)
            .ToArray();

        await Assert.That(derivedVectors).IsNotEmpty();
        foreach (var vector in derivedVectors)
        {
            await Assert.That(vector.RequiredSourceFields).IsNotEmpty();
            await Assert.That(EventLocationDisclosureContract.HasRequiredSourceAuthority(vector.Field, []))
                .IsFalse();
            await Assert.That(EventLocationDisclosureContract.HasRequiredSourceAuthority(
                vector.Field,
                vector.RequiredSourceFields.ToArray())).IsTrue();
        }
    }

    [Test]
    public async Task OperationalSecrets_HaveNoGeneralRoutePurpose()
    {
        var secretFields = ExpectedClasses
            .Where(pair => pair.Value == EventLocationDisclosureFieldClass.RestrictedOperationalSecret)
            .Select(pair => pair.Key);

        foreach (var secretField in secretFields)
        {
            foreach (var purpose in Enum.GetValues<EventLocationDisclosurePurpose>())
            {
                await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(purpose, secretField)).IsFalse();
            }
        }
    }

    [Test]
    public async Task PublicShape_ExposesAssociationIdButNoPhysicalIdOrManagementFields()
    {
        var propertyNames = PropertiesOf<EventLocationPublicDto>()
            .Concat(PropertiesOf<EventLocationPublicFieldsDto>())
            .ToHashSet(StringComparer.Ordinal);

        await Assert.That(propertyNames).Contains(nameof(EventLocationPublicDto.EventLocationId));
        await Assert.That(propertyNames).DoesNotContain("LocationId");
        await Assert.That(propertyNames).DoesNotContain("RoomDescription");
        await Assert.That(propertyNames).DoesNotContain("AccessInstructions");
        await Assert.That(propertyNames).DoesNotContain("EntryDetails");
        await Assert.That(propertyNames).DoesNotContain("DoorCode");
    }

    [Test]
    public async Task PurposeShapes_AreSeparateTypesWithManagementOnlyCeiling()
    {
        await Assert.That(typeof(EventLocationPublicDto)).IsNotEqualTo(typeof(EventLocationAttendeeDto));
        await Assert.That(typeof(EventLocationAttendeeDto)).IsNotEqualTo(typeof(EventLocationManagementDto));
        await Assert.That(typeof(EventLocationPublicFieldsDto)).IsNotEqualTo(typeof(EventLocationAttendeeFieldsDto));
        await Assert.That(PropertiesOf<EventLocationAttendeeFieldsDto>()).DoesNotContain("RoomDescription");
        await Assert.That(PropertiesOf<EventLocationManagementFieldsDto>()).Contains("RoomDescription");
        await Assert.That(PropertiesOf<EventLocationManagementDto>()).Contains("LocationId");
    }

    [Test]
    public async Task HiddenAndTbaPurposeResponses_SerializeWithoutPhysicalValues()
    {
        var eventLocationId = Guid.CreateVersion7();
        var policy = new EventLocationDisclosurePolicyDto(false, false, false, false, false, false, false, 1, null);
        var publicHidden = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.Hidden);
        var publicTba = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.ToBeAnnounced);
        var attendeeHidden = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Attendee,
            EventLocationDisclosureState.Hidden);
        var attendeeTba = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Attendee,
            EventLocationDisclosureState.ToBeAnnounced);
        var managementHidden = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Management,
            EventLocationDisclosureState.Hidden);
        var managementTba = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Management,
            EventLocationDisclosureState.ToBeAnnounced);
        object[] responses =
        {
            EventLocationPublicDto.FromDisclosureResult(publicHidden),
            EventLocationPublicDto.FromDisclosureResult(publicTba),
            EventLocationAttendeeDto.FromDisclosureResult(attendeeHidden),
            EventLocationAttendeeDto.FromDisclosureResult(attendeeTba),
            EventLocationManagementDto.FromDisclosureResult(managementHidden, policy, true, 1, Guid.CreateVersion7()),
            EventLocationManagementDto.FromDisclosureResult(managementTba, policy, false, 1, Guid.CreateVersion7())
        };

        foreach (var response in responses)
        {
            var json = JsonSerializer.Serialize(response, response.GetType());
            await Assert.That(json).DoesNotContain("Fields");
            await Assert.That(json).DoesNotContain("\"LocationId\":");
        }
    }

    [Test]
    public async Task PublicPrivateVenue_AllowsOnlyExactGenericLabel()
    {
        var eventLocationId = Guid.CreateVersion7();
        var safe = EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.PrivateVenue,
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel));

        await Assert.That(safe.DisclosedFields).IsEquivalentTo(
            new[] { EventLocationDisclosureField.VenueName });
        await Assert.That(() => EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.PrivateVenue,
            new EventLocationDisclosureValues(VenueName: "Identifying household label")))
            .Throws<ArgumentException>();

        var forbiddenValues = new[]
        {
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                City: "Identifying city"),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                RoomName: "Identifying room"),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                StreetAddress: "Identifying address"),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                Postcode: "Identifying postcode"),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                Latitude: 1,
                Longitude: 2),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                StreetAddress: "Source",
                FormattedAddress: "Identifying derivative"),
            new EventLocationDisclosureValues(
                VenueName: EventLocationDisclosureContract.PrivateHomePublicLabel,
                Latitude: 1,
                Longitude: 2,
                MapUrl: "Identifying derivative",
                Geohash: "Identifying derivative")
        };

        foreach (var values in forbiddenValues)
        {
            await Assert.That(() => EventLocationDisclosureResult.Public(
                eventLocationId,
                EventLocationDisclosureState.PrivateVenue,
                values)).Throws<ArgumentException>();
        }
    }

    [Test]
    public async Task ResponseDtos_CanOnlyMaterializeFromValidatedResults()
    {
        await Assert.That(typeof(EventLocationPublicDto).GetConstructors()).IsEmpty();
        await Assert.That(typeof(EventLocationAttendeeDto).GetConstructors()).IsEmpty();
        await Assert.That(typeof(EventLocationManagementDto).GetConstructors()).IsEmpty();

        var eventLocationId = Guid.CreateVersion7();
        var hidden = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.Hidden);
        var dto = EventLocationPublicDto.FromDisclosureResult(hidden);

        await Assert.That(dto.State).IsEqualTo(EventLocationDisclosureState.Hidden);
        await Assert.That(dto.Fields).IsNull();
        await Assert.That(() => EventLocationAttendeeDto.FromDisclosureResult(hidden))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DisclosureState_UsesOneStableTypedJsonVocabulary()
    {
        var expected = new Dictionary<EventLocationDisclosureState, string>
        {
            [EventLocationDisclosureState.Hidden] = "hidden",
            [EventLocationDisclosureState.ToBeAnnounced] = "to_be_announced",
            [EventLocationDisclosureState.Available] = "available",
            [EventLocationDisclosureState.PrivateVenue] = "private_venue",
            [EventLocationDisclosureState.Unavailable] = "unavailable",
            [EventLocationDisclosureState.NeedsPrivacyReview] = "needs_privacy_review"
        };

        foreach (var pair in expected)
        {
            await Assert.That(JsonSerializer.Serialize(pair.Key)).IsEqualTo($"\"{pair.Value}\"");
            await Assert.That(JsonSerializer.Deserialize<EventLocationDisclosureState>($"\"{pair.Value}\""))
                .IsEqualTo(pair.Key);
        }

        await Assert.That(() => JsonSerializer.Deserialize<EventLocationDisclosureState>("\"unknown\""))
            .Throws<JsonException>();
        await Assert.That(() => JsonSerializer.Deserialize<EventLocationDisclosureState>("1"))
            .Throws<JsonException>();
    }

    [Test]
    public async Task ResultFactories_RejectPublicPhysicalIdAndSuppressedValues()
    {
        var eventLocationId = Guid.CreateVersion7();
        var publicResult = EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(VenueName: "Selected venue"));
        var hidden = EventLocationDisclosureResult.Suppressed(
            eventLocationId,
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.Hidden);

        await Assert.That(typeof(EventLocationDisclosureResult).GetConstructors()).IsEmpty();
        await Assert.That(publicResult.LocationId).IsNull();
        await Assert.That(hidden.LocationId).IsNull();
        await Assert.That(hidden.Values).IsNull();
        await Assert.That(hidden.DisclosedFields).IsEmpty();
        await Assert.That(() => EventLocationDisclosureResult.Suppressed(
            Guid.Empty,
            EventLocationDisclosurePurpose.Public,
            EventLocationDisclosureState.Hidden))
            .Throws<ArgumentException>();
        await Assert.That(() => EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.Hidden,
            new EventLocationDisclosureValues(VenueName: "Must not survive")))
            .Throws<ArgumentException>();
        await Assert.That(() => EventLocationDisclosureResult.Management(
            eventLocationId,
            EventLocationDisclosureState.ToBeAnnounced,
            new EventLocationDisclosureValues(VenueName: "Must not survive")))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task ResultFactories_EnforcePurposeAndUnavailablePolicyCeilings()
    {
        var eventLocationId = Guid.CreateVersion7();

        await Assert.That(() => EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(RoomDescription: "Management only")))
            .Throws<ArgumentException>();
        await Assert.That(() => EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(Timezone: "Europe/Brussels")))
            .Throws<ArgumentException>();

        var management = EventLocationDisclosureResult.Management(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(RoomDescription: "Authorized manager detail"));

        await Assert.That(management.State).IsEqualTo(EventLocationDisclosureState.Available);
        await Assert.That(management.LocationId).IsNull();
        await Assert.That(management.Values!.RoomDescription).IsEqualTo("Authorized manager detail");
        await Assert.That(management.DisclosedFields).Contains(EventLocationDisclosureField.RoomDescription);
    }

    [Test]
    public async Task ResultFactories_EnforceDerivativeSourceValues()
    {
        var eventLocationId = Guid.CreateVersion7();

        await Assert.That(() => EventLocationDisclosureResult.Public(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(FormattedAddress: "Derived")))
            .Throws<ArgumentException>();
        await Assert.That(() => EventLocationDisclosureResult.Attendee(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(MapUrl: "derived")))
            .Throws<ArgumentException>();
        await Assert.That(() => EventLocationDisclosureResult.Attendee(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(Geohash: "derived", Latitude: 1)))
            .Throws<ArgumentException>();

        var exact = EventLocationDisclosureResult.Attendee(
            eventLocationId,
            EventLocationDisclosureState.Available,
            new EventLocationDisclosureValues(
                StreetAddress: "Authorized source",
                FormattedAddress: "Authorized derivative",
                Latitude: 1,
                Longitude: 2,
                MapUrl: "Authorized coordinate derivative",
                Geohash: "Authorized coordinate derivative"));

        await Assert.That(exact.DisclosedFields).Contains(EventLocationDisclosureField.FormattedAddress);
        await Assert.That(exact.DisclosedFields).Contains(EventLocationDisclosureField.MapUrl);
        await Assert.That(exact.DisclosedFields).Contains(EventLocationDisclosureField.Geohash);
    }

    [Test]
    public async Task PrivateHomePublicLabel_IsStableAndGeneric()
    {
        await Assert.That(EventLocationDisclosureContract.PrivateHomePublicLabel).IsEqualTo("Private venue");
    }

    [Test]
    public async Task Contracts_AreImmutableRecordsAndDoNotReuseGenericLocationDto()
    {
        var contractTypes = new[]
        {
            typeof(EventLocationPublicDto),
            typeof(EventLocationPublicFieldsDto),
            typeof(EventLocationAttendeeDto),
            typeof(EventLocationAttendeeFieldsDto),
            typeof(EventLocationManagementDto),
            typeof(EventLocationManagementFieldsDto),
            typeof(EventLocationDisclosurePolicyDto),
            typeof(EventLocationDisclosureRequest),
            typeof(EventLocationDisclosureValues),
            typeof(EventLocationDisclosureResult)
        };

        foreach (var contractType in contractTypes)
        {
            await Assert.That(IsRecord(contractType)).IsTrue();
            await Assert.That(contractType.GetProperties().All(property => property.SetMethod?.IsInitOnly() != false)).IsTrue();
            await Assert.That(contractType.GetProperties().Any(property => property.PropertyType == typeof(LocationDto))).IsFalse();
        }

        await Assert.That(typeof(UpdateEventLocationDisclosureDto).GetProperties()
            .Any(property => property.PropertyType == typeof(LocationDto))).IsFalse();

        await Assert.That(typeof(EventLocationDisclosureResult).GetProperty("DisclosedFields")?.PropertyType)
            .IsEqualTo(typeof(ImmutableArray<EventLocationDisclosureField>));
    }

    private static string[] PropertiesOf<T>()
        => typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
            .ToArray();

    private static bool IsRecord(Type type)
        => type.GetMethod("<Clone>$", BindingFlags.Instance | BindingFlags.Public) is not null;
}

internal static class PropertyInfoExtensions
{
    public static bool IsInitOnly(this MethodInfo method)
        => method.ReturnParameter.GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
}
