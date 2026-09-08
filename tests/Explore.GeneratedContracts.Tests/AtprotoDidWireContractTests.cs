using System.Text.Json;

namespace Explore.GeneratedContracts.Tests;

public sealed class AtprotoDidWireContractTests
{
    [Test]
    public async Task GeneratedOpenApiRetainsScalarDidContract()
    {
        string schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "schemas",
            "openapi_islamu-event.json");
        await using FileStream schema = File.OpenRead(schemaPath);
        using JsonDocument document = await JsonDocument.ParseAsync(schema);

        AssertDidContract(document.RootElement);
    }

    [Test]
    public async Task GeneratedShapeMutationIsRejected()
    {
        using JsonDocument document = JsonDocument.Parse(
            """
            {
              "paths": {
                "/api/actor/by-did/{did}": {
                  "get": {
                    "operationId": "GetActorByDid",
                    "parameters": [
                      {
                        "name": "did",
                        "in": "path",
                        "required": true,
                        "schema": { "type": "object" }
                      }
                    ]
                  }
                }
              },
              "components": {
                "schemas": {
                  "ActorDto": {
                    "properties": {
                      "did": { "type": ["null", "string"] }
                    }
                  }
                }
              }
            }
            """);

        await Assert.That(() => AssertDidContract(document.RootElement))
            .Throws<InvalidOperationException>();
    }

    private static void AssertDidContract(JsonElement root)
    {
        JsonElement paths = root.GetProperty("paths");
        string[] didRoutes = paths.EnumerateObject()
            .Select(path => path.Name)
            .Where(path => path.EndsWith("/by-did/{did}", StringComparison.Ordinal))
            .ToArray();
        if (didRoutes is not ["/api/actor/by-did/{did}"])
        {
            throw new InvalidOperationException("The Actor DID route contract changed.");
        }

        JsonElement operation = paths.GetProperty(didRoutes[0]).GetProperty("get");
        if (operation.GetProperty("operationId").GetString() != "GetActorByDid")
        {
            throw new InvalidOperationException("The Actor DID operation identifier changed.");
        }

        JsonElement didParameter = operation.GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter =>
                parameter.GetProperty("name").GetString() == "did"
                && parameter.GetProperty("in").GetString() == "path");
        if (!didParameter.GetProperty("required").GetBoolean()
            || didParameter.GetProperty("schema").GetProperty("type").GetString() != "string")
        {
            throw new InvalidOperationException("The Actor DID path parameter is no longer a required string.");
        }

        JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
        if (schemas.EnumerateObject().Any(schema =>
                schema.Name.Contains("AtprotoDid", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A compatibility DID schema appeared in the generated contract.");
        }

        HashSet<string?> actorDidTypes = schemas.GetProperty("ActorDto")
            .GetProperty("properties")
            .GetProperty("did")
            .GetProperty("type")
            .EnumerateArray()
            .Select(type => type.GetString())
            .ToHashSet(StringComparer.Ordinal);
        if (!actorDidTypes.SetEquals(["null", "string"]))
        {
            throw new InvalidOperationException("ActorDto.did is no longer a nullable scalar string.");
        }
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && File.Exists(Path.Combine(
                    directory.FullName,
                    "schemas",
                    "openapi_islamu-event.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
