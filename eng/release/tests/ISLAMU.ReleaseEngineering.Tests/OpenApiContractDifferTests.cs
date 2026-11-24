using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class OpenApiContractDifferTests
{
    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task ClassifiesOperationHalLinkRequiredTypeAndSecurityChangesConservatively()
    {
        const string baseSpec = """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "security": [],
                    "parameters": [
                      { "name": "page", "in": "query", "required": false, "schema": { "type": "integer" } }
                    ],
                    "responses": {
                      "200": {
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/EventResource" }
                          }
                        }
                      }
                    }
                  },
                  "delete": {
                    "responses": { "204": { "description": "Deleted" } }
                  }
                }
              },
              "components": {
                "schemas": {
                  "EventResource": {
                    "type": "object",
                    "required": ["id"],
                    "properties": {
                      "id": { "type": "string" },
                      "title": { "type": ["null", "string"] },
                      "_links": {
                        "type": "object",
                        "properties": {
                          "self": { "type": "object" },
                          "edit": { "type": "object" }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        const string headSpec = """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "security": [{ "bearer": [] }],
                    "parameters": [
                      { "name": "page", "in": "query", "required": true, "schema": { "type": "integer" } },
                      { "name": "cursor", "in": "query", "required": false, "schema": { "type": "string" } }
                    ],
                    "responses": {
                      "200": {
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/EventResource" }
                          }
                        }
                      }
                    }
                  }
                },
                "/api/events/featured": {
                  "get": {
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              },
              "components": {
                "schemas": {
                  "EventResource": {
                    "type": "object",
                    "required": ["id", "title"],
                    "properties": {
                      "id": { "type": "string" },
                      "title": { "type": "string" },
                      "subtitle": { "type": ["null", "string"] },
                      "_links": {
                        "type": "object",
                        "properties": {
                          "self": { "type": "object" },
                          "publish": { "type": "object" }
                        }
                      }
                    }
                  }
                }
              }
            }
            """;

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = baseSpec })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = headSpec });

        ContractDifferResult result = new OpenApiContractDiffer().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "openapi diff");
        await Assert.That(result.Changes.Any(c => c.Kind == "operation_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("DELETE /api/events", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "operation_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("GET /api/events/featured", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "security_narrowed" && c.Severity == ContractChangeSeverity.Breaking)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "parameter_narrowed_required" && c.Severity == ContractChangeSeverity.Breaking)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "optional_parameter_added" && c.Severity == ContractChangeSeverity.Additive)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "hal_link_removed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("edit", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "hal_link_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("publish", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "required_property_added" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("title", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "schema_type_narrowed" && c.Severity == ContractChangeSeverity.Breaking && c.Target.Contains("title", StringComparison.Ordinal))).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "optional_property_added" && c.Severity == ContractChangeSeverity.Additive && c.Target.Contains("subtitle", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MarksUnresolvedRefsVendorExtensionsAndUnknownStructuralChangesAsReviewRequired()
    {
        const string baseSpec = """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "x-islamu-tier": "public",
                    "responses": {
                      "200": {
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "#/components/schemas/MissingExternal" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Payload": {
                    "type": "object",
                    "properties": {
                      "status": { "type": "string", "default": "draft", "pattern": "^[a-z]+$" }
                    }
                  }
                }
              }
            }
            """;

        const string headSpec = """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "x-islamu-tier": "federated",
                    "responses": {
                      "200": {
                        "content": {
                          "application/json": {
                            "schema": { "$ref": "https://example.invalid/external.json#/Payload" }
                          }
                        }
                      }
                    }
                  }
                }
              },
              "components": {
                "schemas": {
                  "Payload": {
                    "type": "object",
                    "properties": {
                      "status": { "type": "string", "default": "published", "pattern": "^[a-z0-9-]+$" }
                    }
                  }
                }
              }
            }
            """;

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = baseSpec })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = headSpec });

        ContractDifferResult result = new OpenApiContractDiffer().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsTrue().Because(result.Diagnostic ?? "review required");
        await Assert.That(result.Changes.Any(c => c.Kind == "unresolved_ref" && c.Severity == ContractChangeSeverity.ReviewRequired)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "extension_changed" && c.Severity == ContractChangeSeverity.ReviewRequired)).IsTrue();
        await Assert.That(result.Changes.Any(c => c.Kind == "schema_review_required" && c.Severity == ContractChangeSeverity.ReviewRequired && c.Target.Contains("status", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MalformedOpenApiJsonFailsClosed()
    {
        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = "{}" })
            .AddCommit(HeadOid, new Dictionary<string, string> { ["schemas/openapi_islamu-event.json"] = "{ invalid json" });

        ContractDifferResult result = new OpenApiContractDiffer().Diff(reader, BaseOid, HeadOid);

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("contract_diff_openapi_malformed");
    }
}
