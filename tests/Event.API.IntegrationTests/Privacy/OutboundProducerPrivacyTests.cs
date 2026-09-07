using System.Text.Json;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Location;
using TUnit.Core;

namespace ApiIntegrationTests.Privacy;

/// <summary>
/// Every producer that can reach a recipient outside the request that authorized it is audited here.
/// The rule is uniform: an outbound builder consumes an already purpose-evaluated projection, and never
/// reads <c>Location.Pii</c>, <c>Location.Address</c>, or coordinates itself.
/// </summary>
[Category("EventLocationPrivacy")]
public sealed class OutboundProducerPrivacyTests
{
    /// <summary>
    /// Raw physical-venue accessors. Reaching for any of these in an outbound builder means the builder
    /// decided visibility for itself instead of asking the disclosure authority.
    /// </summary>
    private static readonly string[] ForbiddenRawVenueAccess =
    [
        "LocationPii",
        ".Pii.Address",
        ".Pii.Postcode",
        ".Pii.Latitude",
        ".Pii.Longitude",
        "Location.Address",
        "Location.Postcode",
        "Location.Latitude",
        "Location.Longitude"
    ];

    /// <summary>
    /// Disclosure enforcement points that legitimately name venue PII in order to classify and block it.
    /// They are the gate, not a producer: the MCP guard passes the entity name to the AI disclosure
    /// gateway so a sanitization gap fails the response closed.
    /// </summary>
    private static readonly string[] DisclosureEnforcementPoints =
    [
        Path.Combine("Explore.API", "Mcp", "EventMcpLocationDisclosureGuard.cs")
    ];

    /// <summary>Outbound producer families, addressed by their source directory or file.</summary>
    private static readonly string[] OutboundProducerPaths =
    [
        Path.Combine("Explore.Application", "Notifications"),
        Path.Combine("Explore.Application", "Webhooks"),
        Path.Combine("Explore.Application", "Features", "Notifications"),
        Path.Combine("Explore.Application", "Features", "Webhooks"),
        Path.Combine("Explore.Application", "Features", "EventTicketing"),
        Path.Combine("Explore.Application", "Features", "RegistrationOrders"),
        Path.Combine("Explore.Application", "Features", "EventReporting"),
        Path.Combine("Explore.Application", "Services", "Email"),
        Path.Combine("Explore.API", "Mcp"),
        Path.Combine("Explore.Infrastructure", "Services", "Email"),
        Path.Combine("Explore.Infrastructure", "Services", "Webhooks")
    ];

    [Test]
    public async Task NoOutboundProducerReadsRawPhysicalVenueData()
    {
        var violations = new List<string>();

        foreach (string relativePath in OutboundProducerPaths)
        {
            string absolute = Path.Combine(RepoRoot, "src", relativePath);
            if (!Directory.Exists(absolute))
            {
                continue;
            }

            foreach (string file in EnumerateSources(absolute))
            {
                if (DisclosureEnforcementPoints.Any(point =>
                    file.EndsWith(Path.Combine("src", point), StringComparison.Ordinal)))
                {
                    continue;
                }

                string source = File.ReadAllText(file);
                violations.AddRange(ForbiddenRawVenueAccess
                    .Where(token => source.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(RepoRoot, file)} -> {token}"));
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    [Arguments("GetEventCalendarExportRequestHandler.cs")]
    [Arguments("GetAttendeeEventCalendarExportRequestHandler.cs")]
    public async Task CalendarExportBuildersResolveThroughTheDisclosureAuthority(string handlerFileName)
    {
        string path = Path.Combine(
            RepoRoot,
            "src",
            "Explore.Application",
            "Features",
            "Events",
            "Handlers",
            "Queries",
            handlerFileName);
        string source = await File.ReadAllTextAsync(path);

        await Assert.That(source).Contains(nameof(IEventLocationDisclosureService));
        foreach (string token in ForbiddenRawVenueAccess)
        {
            await Assert.That(source).DoesNotContain(token);
        }
    }

    [Test]
    public async Task PublicCalendarExportNeverDependsOnRequesterIdentity()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            RepoRoot,
            "src",
            "Explore.Application",
            "Features",
            "Events",
            "Handlers",
            "Queries",
            "GetEventCalendarExportRequestHandler.cs"));

        // A public ICS feed that varied by cookie would be cached under one key and served to everyone.
        await Assert.That(source).DoesNotContain("ICurrentUserService");
        await Assert.That(source).Contains($"{nameof(EventLocationDisclosurePurpose)}.{nameof(EventLocationDisclosurePurpose.Public)}");
    }

    [Test]
    public async Task PublicProjectionSerializationCarriesNoExactVenueDataWhenTheServerWithheldIt()
    {
        // The public contract is shaped so a withheld field is absent, not null-but-present, so a naive
        // downstream serializer cannot resurrect an empty address line into a real-looking one.
        var withheld = new EventLocationPublicFieldsDto(
            Country: "BE",
            Timezone: null,
            City: null,
            VenueName: "Private venue",
            RoomName: null,
            StreetAddress: null,
            Postcode: null,
            Latitude: null,
            Longitude: null,
            FormattedAddress: null,
            MapUrl: null,
            Geohash: null);

        string json = JsonSerializer.Serialize(withheld, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        await Assert.That(json).DoesNotContain("StreetAddress");
        await Assert.That(json).DoesNotContain("Postcode");
        await Assert.That(json).DoesNotContain("Latitude");
        await Assert.That(json).DoesNotContain("Geohash");
        await Assert.That(json).Contains("Private venue");
    }

    [Test]
    public async Task ManagementOnlyFieldsAreAbsentFromEveryNonManagementContract()
    {
        string[] managementOnly = ["RoomDescription", "AccessInstructions", "EntryDetails", "DoorCode"];

        foreach (string field in managementOnly)
        {
            await Assert.That(typeof(EventLocationPublicFieldsDto).GetProperty(field)).IsNull();
            await Assert.That(typeof(EventLocationAttendeeFieldsDto).GetProperty(field)).IsNull();
        }

        // Room description is management-only; operational secrets are on no contract at all.
        await Assert.That(typeof(EventLocationManagementFieldsDto).GetProperty("RoomDescription")).IsNotNull();
        await Assert.That(typeof(EventLocationManagementFieldsDto).GetProperty("DoorCode")).IsNull();
        await Assert.That(typeof(EventLocationManagementFieldsDto).GetProperty("AccessInstructions")).IsNull();
    }

    [Test]
    public async Task OutboundProducersDoNotProjectThePhysicalLocationIdentifier()
    {
        // Only the management contract may expose the physical LocationId; public and attendee surfaces
        // address venues through the event-scoped EventLocationId.
        await Assert.That(typeof(EventLocationPublicDto).GetProperty("LocationId")).IsNull();
        await Assert.That(typeof(EventLocationAttendeeDto).GetProperty("LocationId")).IsNull();
        await Assert.That(typeof(EventLocationPublicDto).GetProperty("EventLocationId")).IsNotNull();
        await Assert.That(typeof(EventLocationAttendeeDto).GetProperty("EventLocationId")).IsNotNull();
    }

    private static IEnumerable<string> EnumerateSources(string root) =>
        Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx"))
                && Directory.Exists(Path.Combine(directory.FullName, "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found from the test output directory.");
    }
}
