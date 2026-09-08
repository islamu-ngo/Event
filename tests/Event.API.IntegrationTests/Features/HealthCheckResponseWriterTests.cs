using System.Text;
using System.Text.Json.Nodes;
using Event.Api.IntegrationTests.Fixtures;
using Explore.ServiceDefaults.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
public sealed class HealthCheckResponseWriterTests
{
    [Test]
    public async Task WriteAsync_WhenEntryHasException_RedactsRawFailureDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var data = new Dictionary<string, object>
        {
            ["provider"] = "s3_compatible",
            ["status"] = "unhealthy",
            ["failureCode"] = "provider_unreachable",
            ["endpoint"] = "https://s3.example.test/private",
            ["bucketName"] = "private-bucket",
            ["objectKey"] = "tenant-a/private/file.png",
            ["localPath"] = "/srv/islamu-event/private/file.png",
            ["apiKeyConfigured"] = true,
            ["durationMs"] = 12
        };

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["storage"] = new(
                    HealthStatus.Unhealthy,
                    "Provider failed at https://s3.example.test/private",
                    TimeSpan.FromMilliseconds(12),
                    new InvalidOperationException("Failed to probe https://s3.example.test/private with secret-token from /srv/islamu-event/private/file.png"),
                    data)
            },
            TimeSpan.FromMilliseconds(12));

        await HealthCheckResponseWriter.WriteAsync(context, report);

        var json = ReadResponseJson(context);
        var root = JsonNode.Parse(json)!.AsObject();
        var check = root["checks"]!.AsArray()[0]!.AsObject();
        var checkData = check["data"]!.AsObject();

        await Assert.That(check["error"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedErrorMessage);
        await Assert.That(check["description"]).IsNull();
        await Assert.That(checkData["provider"]!.GetValue<string>()).IsEqualTo("s3_compatible");
        await Assert.That(checkData["status"]!.GetValue<string>()).IsEqualTo("unhealthy");
        await Assert.That(checkData["failureCode"]!.GetValue<string>()).IsEqualTo("provider_unreachable");
        await Assert.That(checkData["apiKeyConfigured"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["durationMs"]!.GetValue<int>()).IsEqualTo(12);
        await Assert.That(checkData["endpoint"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["bucketName"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["objectKey"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["localPath"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);

        await Assert.That(json).DoesNotContain("https://s3.example.test");
        await Assert.That(json).DoesNotContain("secret-token");
        await Assert.That(json).DoesNotContain("private-bucket");
        await Assert.That(json).DoesNotContain("/srv/islamu-event");
        await Assert.That(json).DoesNotContain("tenant-a/private/file.png");
    }

    [Test]
    public async Task WriteAsync_WhenEntryIsHealthy_PreservesDocumentedShapeAndSafeData()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["mcp-adapter"] = new(
                    HealthStatus.Healthy,
                    "MCP adapter posture is bounded.",
                    TimeSpan.FromMilliseconds(3),
                    exception: null,
                    data: new Dictionary<string, object>
                    {
                        ["enabled"] = true,
                        ["startupEnabled"] = true,
                        ["runtimeEnabled"] = true,
                        ["legacySseRuntimeEnabled"] = false
                    })
            },
            TimeSpan.FromMilliseconds(3));

        await HealthCheckResponseWriter.WriteAsync(context, report);

        var json = ReadResponseJson(context);
        var root = JsonNode.Parse(json)!.AsObject();
        var check = root["checks"]!.AsArray()[0]!.AsObject();
        var checkData = check["data"]!.AsObject();

        await Assert.That(context.Response.ContentType).IsEqualTo("application/json; charset=utf-8");
        await Assert.That(context.Response.Headers["Connection"].ToString()).IsEqualTo("close");
        await Assert.That(context.Response.Headers["Access-Control-Allow-Origin"].ToString()).IsEqualTo("*");
        await Assert.That(context.Response.Headers["X-Health-Status"].ToString()).IsEqualTo("Healthy");
        await Assert.That(context.Response.Headers["Cache-Control"].ToString()).IsEqualTo("no-cache, no-store, must-revalidate");
        await Assert.That(context.Response.Headers["Pragma"].ToString()).IsEqualTo("no-cache");
        await Assert.That(root["status"]!.GetValue<string>()).IsEqualTo("Healthy");
        await Assert.That(root["message"]!.GetValue<string>()).IsEqualTo("Ok");
        await Assert.That(check["name"]!.GetValue<string>()).IsEqualTo("mcp-adapter");
        await Assert.That(check["status"]!.GetValue<string>()).IsEqualTo("Healthy");
        await Assert.That(check["description"]!.GetValue<string>()).IsEqualTo("MCP adapter posture is bounded.");
        await Assert.That(check["error"]).IsNull();
        await Assert.That(checkData["enabled"]!.GetValue<bool>()).IsTrue();
        await Assert.That(checkData["startupEnabled"]!.GetValue<bool>()).IsTrue();
        await Assert.That(checkData["runtimeEnabled"]!.GetValue<bool>()).IsTrue();
        await Assert.That(checkData["legacySseRuntimeEnabled"]!.GetValue<bool>()).IsFalse();
    }

    [Test]
    public async Task WriteAsync_WhenSensitiveKeysContainPrimitiveValues_RedactsBeforeTypeInspection()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["email-dispatch"] = new(
                    HealthStatus.Healthy,
                    description: null,
                    TimeSpan.Zero,
                    exception: null,
                    data: new Dictionary<string, object>
                    {
                        ["tenantId"] = 42,
                        ["userId"] = 17L,
                        ["providerId"] = 3,
                        ["recipientAddress"] = "person@example.test",
                        ["pendingCount"] = 9
                    })
            },
            TimeSpan.Zero);

        await HealthCheckResponseWriter.WriteAsync(context, report);

        var root = JsonNode.Parse(ReadResponseJson(context))!.AsObject();
        var checkData = root["checks"]!.AsArray()[0]!["data"]!.AsObject();
        await Assert.That(checkData["tenantId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["userId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["providerId"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["recipientAddress"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(checkData["pendingCount"]!.GetValue<int>()).IsEqualTo(9);
    }

    [Test]
    public async Task WriteAsync_WhenDescriptionOrDataContainDerivedConnectionStrings_RedactsThem()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        const string connection = "Host=db.internal;Port=5432;Database=events;Username=runtime;Password=canary";
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Unhealthy,
                    connection,
                    TimeSpan.Zero,
                    exception: null,
                    data: new Dictionary<string, object> { ["runtimeConnection"] = connection })
            },
            TimeSpan.Zero);

        await HealthCheckResponseWriter.WriteAsync(context, report);

        var root = JsonNode.Parse(ReadResponseJson(context))!.AsObject();
        var check = root["checks"]!.AsArray()[0]!.AsObject();
        await Assert.That(check["description"]).IsNull();
        await Assert.That(check["data"]!["runtimeConnection"]!.GetValue<string>()).IsEqualTo(HealthCheckResponseWriter.RedactedValue);
        await Assert.That(root.ToJsonString()).DoesNotContain("db.internal");
        await Assert.That(root.ToJsonString()).DoesNotContain("canary");
    }

    private static string ReadResponseJson(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }
}
