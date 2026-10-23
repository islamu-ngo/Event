namespace Explore.Blazor.Client.Tests.Pages.Admin;

using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Services.InstanceAdmin;
using Microsoft.AspNetCore.Components;

public sealed class ConfigurationModerationPreviewTests : IDisposable
{
    private readonly BlazorTestContext context = new();

    public void Dispose() => context.Dispose();

    [Test]
    [Arguments(ConfigurationImportScope.Instance, false)]
    [Arguments(ConfigurationImportScope.Instance, true)]
    [Arguments(ConfigurationImportScope.Tenant, false)]
    [Arguments(ConfigurationImportScope.Tenant, true)]
    public async Task GeneratedPreviewPreservesDestinationDetailsWithoutInventingApplyAuthority(
        ConfigurationImportScope scope, bool advertiseApply)
    {
        var sessionId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var links = new Dictionary<string, object>();
        if (advertiseApply)
            links.Add("apply-configuration-import", new { href = "/api/configuration/apply", method = "POST" });
        var json = JsonSerializer.Serialize(new
        {
            sessionId, targetScope = scope.ToString(), targetTenantId = scope == ConfigurationImportScope.Tenant ? (Guid?)tenantId : null,
            state = "PreviewReady", expiresAt = DateTimeOffset.UtcNow.AddMinutes(10), isApplyReady = true,
            items = Array.Empty<object>(), moderation = new[] { Target() }, _links = links
        });
        using var transport = new PreviewTransport(json);
        using var http = new HttpClient(transport) { BaseAddress = new Uri("https://localhost/") };
        var service = new ConfigurationPortabilityService(null!, null!,
            new Instance_ConfigurationClient(http), new Tenant_ConfigurationClient(http), null!, null!,
            context.Services.GetRequiredService<NavigationManager>());
        var session = new ConfigurationImportClientSession(sessionId, Guid.CreateVersion7().ToString("N"),
            scope, scope == ConfigurationImportScope.Tenant ? tenantId : null,
            DateTimeOffset.UtcNow.AddMinutes(10), ["tenant.moderation"]);

        var preview = await service.PreviewAsync(session, ["tenant.moderation"],
            new Dictionary<string, string>(), [], ConfigurationImportApplyMode.ApplySelected, CancellationToken.None);

        await Assert.That(preview.CanApply).IsEqualTo(advertiseApply);
        await Assert.That(preview.IsApplyReady).IsTrue();
        await Assert.That(preview.Moderation.Count).IsEqualTo(1);
        var target = preview.Moderation.Single();
        await Assert.That(target.TargetName).IsEqualTo("Destination <review>");
        await Assert.That(target.Changes!.Single().Conflict).IsTrue();
        await Assert.That(target.Diagnostics!.Single().RuleRevision).IsEqualTo(2L);
        await Assert.That(transport.Method).IsEqualTo(HttpMethod.Post);
    }

    [Test]
    public async Task ReadOnlyDetailsExposeConflictsAndDiagnosticsAsEncodedSemanticContent()
    {
        var target = JsonSerializer.Deserialize<ConfigurationImportModerationTargetPreview>(
            JsonSerializer.Serialize(Target()))!;
        var cut = context.Render<ConfigurationModerationPreview>(parameters =>
            parameters.Add(component => component.Targets, new[] { target }));

        await Assert.That(cut.Find("h5").TextContent).Contains("Destination <review>");
        await Assert.That(cut.FindAll("h6").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("li").Any(item => item.TextContent.Contains("MeaningRevisionConflict", StringComparison.Ordinal))).IsTrue();
        await Assert.That(cut.Markup).Contains("Conflict");
        await Assert.That(cut.FindAll("review, script, button, a")).IsEmpty();
    }

    private static object Target() => new
    {
        sectionKey = "tenant.moderation", sourceName = "Source", targetName = "Destination <review>",
        currentPolicyRevision = 3L, hasBaseline = true, canApply = false,
        changes = new[] { new { section = "profiles", referenceCode = "publication", referenceRevision = 2L, kind = "Changed", conflict = true } },
        diagnostics = new[] { new { kind = "MeaningRevisionConflict", severity = "Error", ruleCode = "publication", ruleRevision = 2L, consumerId = (string?)null, mode = (string?)null } }
    };

    private sealed class PreviewTransport(string json) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/hal+json")
            });
        }
    }
}
