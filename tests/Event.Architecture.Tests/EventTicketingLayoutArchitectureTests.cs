using System.Text.RegularExpressions;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventTicketing;
using Explore.Application.Features.EventTicketing.Requests.Commands;
using Explore.Application.Features.EventTicketing.Requests.Queries;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Architecture.Tests;

public sealed class EventTicketingLayoutArchitectureTests
{
    private const string FeatureNamespace = "Explore.Application.Features.EventTicketing";

    [Test]
    public async Task EventTicketingHandlers_ShouldUseCanonicalFoldersAndNamespaces()
    {
        string featureRoot = GetFeatureRoot();
        string handlersRoot = Path.Combine(featureRoot, "Handlers");
        string commandsRoot = Path.Combine(handlersRoot, "Commands");
        string queriesRoot = Path.Combine(handlersRoot, "Queries");

        string[] handlerFiles = Directory.Exists(handlersRoot)
            ? Directory.GetFiles(handlersRoot, "*Handler.cs", SearchOption.AllDirectories)
            : [];

        var failures = new List<string>();
        foreach (string file in handlerFiles)
        {
            string fileName = Path.GetFileName(file);
            string directory = Path.GetDirectoryName(file)!;
            string source = await File.ReadAllTextAsync(file);
            string? expectedNamespace = fileName.EndsWith("CommandHandler.cs", StringComparison.Ordinal)
                ? $"namespace {FeatureNamespace}.Handlers.Commands;"
                : fileName.EndsWith("QueryHandler.cs", StringComparison.Ordinal)
                    ? $"namespace {FeatureNamespace}.Handlers.Queries;"
                    : null;

            if (expectedNamespace is null
                || !string.Equals(directory, fileName.EndsWith("CommandHandler.cs", StringComparison.Ordinal) ? commandsRoot : queriesRoot, StringComparison.Ordinal)
                || !source.Contains(expectedNamespace, StringComparison.Ordinal))
            {
                failures.Add(file);
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    public async Task EventTicketingRoot_ShouldNotContainHandlersServicesBasesOrRequests()
    {
        string featureRoot = GetFeatureRoot();
        string[] forbiddenFiles = Directory.Exists(featureRoot)
            ? Directory.GetFiles(featureRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Where(path =>
                    Path.GetFileName(path).EndsWith("Handler.cs", StringComparison.Ordinal)
                    || Path.GetFileName(path).EndsWith("Service.cs", StringComparison.Ordinal)
                    || Path.GetFileName(path).EndsWith("Base.cs", StringComparison.Ordinal)
                    || Path.GetFileName(path).Contains("Request", StringComparison.Ordinal))
                .ToArray()
            : [];

        await Assert.That(forbiddenFiles).IsEmpty();
    }

    [Test]
    public async Task EventTicketingRequestFiles_ShouldContainExactlyOneRequestType()
    {
        string featureRoot = GetFeatureRoot();
        string requestsRoot = Path.Combine(featureRoot, "Requests");
        string[] requestFiles = Directory.Exists(requestsRoot)
            ? Directory.GetFiles(requestsRoot, "*.cs", SearchOption.AllDirectories)
            : [];

        var failures = new List<string>();
        const string declarationPattern = @"\b(?:class|record)\s+\w+(?:Command|Query|Request)\b";
        foreach (string file in requestFiles)
        {
            string source = await File.ReadAllTextAsync(file);
            if (Regex.Matches(source, declarationPattern).Count != 1)
            {
                failures.Add(file);
            }
        }

        await Assert.That(failures).IsEmpty();
    }

    [Test]
    [Category("Phase43Ticketing")]
    public async Task EventTicketingRequests_ShouldAuthorizeParentEventWithManageTickets()
    {
        Type[] requestTypes =
        [
            typeof(CreateEventTicketCatalogDraftCommand),
            typeof(CloneEventTicketCatalogDraftCommand),
            typeof(PublishEventTicketCatalogCommand),
            typeof(CreateEventTicketTypeCommand),
            typeof(UpdateEventTicketTypeCommand),
            typeof(DeleteEventTicketTypeCommand),
            typeof(CreateEventCapacityPoolCommand),
            typeof(UpdateEventCapacityPoolCommand),
            typeof(DeleteEventCapacityPoolCommand),
            typeof(GetEventTicketCatalogManagementQuery)
        ];

        foreach (var requestType in requestTypes)
        {
            var authorization = requestType
                .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
                .Cast<AuthorizeResourceAttribute>()
                .Single();

            await Assert.That(authorization.Resource).IsEqualTo(ResourceKinds.Event);
            await Assert.That(authorization.Action).IsEqualTo(AuthorizationActions.Events.ManageTickets);
        }
    }

    [Test]
    public async Task EventTicketingManageDtos_ShouldOmitPersistedIds_ReadDtosShouldRetainThem()
    {
        Type[] manageDtos =
        [
            typeof(ManageEventTicketTypeDto),
            typeof(ManageEventCapacityPoolDto),
            typeof(ManageTicketTypeEntitlementDto)
        ];
        string[] failures = manageDtos
            .Where(type => type.GetProperty("Id") is not null)
            .Select(type => type.FullName!)
            .ToArray();

        await Assert.That(failures).IsEmpty();
        await Assert.That(typeof(EventTicketTypeDto).GetProperty("Id")).IsNotNull();
        await Assert.That(typeof(EventCapacityPoolDto).GetProperty("Id")).IsNotNull();
        await Assert.That(typeof(CreateEventTicketTypeCommand).GetProperty("TicketType")!.PropertyType)
            .IsEqualTo(typeof(ManageEventTicketTypeDto));
        await Assert.That(typeof(UpdateEventTicketTypeCommand).GetProperty("TicketType")!.PropertyType)
            .IsEqualTo(typeof(ManageEventTicketTypeDto));
        await Assert.That(typeof(CreateEventCapacityPoolCommand).GetProperty("CapacityPool")!.PropertyType)
            .IsEqualTo(typeof(ManageEventCapacityPoolDto));
        await Assert.That(typeof(UpdateEventCapacityPoolCommand).GetProperty("CapacityPool")!.PropertyType)
            .IsEqualTo(typeof(ManageEventCapacityPoolDto));
    }

    private static string GetFeatureRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "Explore.Application", "Features", "EventTicketing");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the EventTicketing feature root.");
    }
}
