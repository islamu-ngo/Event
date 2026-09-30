namespace Event.SetupAssistant.Browser.Tests;

using System.Text;
using System.Text.Json;
using Bunit;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Browser;
using ISLAMU.Event.SetupAssistant.Browser.Pages;

public sealed class BrowserOfflineWorkflowTests
{
    [Test]
    public async Task PublicManifestProducesCanonicalPreviewAndDownload()
    {
        string topology = PlatformEnvironmentCatalogue.Catalogue.Topologies[0];
        byte[] input = Encoding.UTF8.GetBytes(
            $$"""
            {
              "providers": [],
              "capabilities": [],
              "topology": "{{topology}}",
              "name": "community-host",
              "kind": "public-template",
              "schema": "event-setup-public-manifest/v1"
            }
            """);

        BrowserManifestResult result = BrowserPublicManifest.ValidateAndPreview(input);

        await Assert.That(result.IsAccepted).IsTrue();
        await Assert.That(result.Preview).IsNotNull();
        await Assert.That(result.DownloadHref).StartsWith("data:application/json;base64,");
        byte[] downloaded = Convert.FromBase64String(
            result.DownloadHref!["data:application/json;base64,".Length..]);
        using JsonDocument document = JsonDocument.Parse(downloaded);
        await Assert.That(document.RootElement.GetProperty("name").GetString())
            .IsEqualTo("community-host");
        await Assert.That(document.RootElement.GetProperty("topology").GetString())
            .IsEqualTo(topology);
    }

    [Test]
    public async Task CatalogueContainsOnlyPublicCoreDefinitions()
    {
        BrowserCatalogue catalogue = BrowserPublicCatalogue.Create();
        string[] expected = PlatformEnvironmentCatalogue.Catalogue.Definitions
            .Where(definition => definition.Sensitivity == EnvironmentVariableSensitivity.Public)
            .Select(definition => definition.Key)
            .ToArray();

        await Assert.That(catalogue.Entries.Select(entry => entry.Key)).IsEquivalentTo(expected);
        await Assert.That(catalogue.Entries.Any(entry =>
            PlatformEnvironmentCatalogue.Catalogue.Lookup(entry.Key)?.Sensitivity
                != EnvironmentVariableSensitivity.Public)).IsFalse();
    }

    [Test]
    public async Task ManifestComponentRendersSemanticPublicSuccess()
    {
        string topology = PlatformEnvironmentCatalogue.Catalogue.Topologies[0];
        byte[] input = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema": "event-setup-public-manifest/v1",
              "kind": "public-template",
              "name": "community-host",
              "topology": "{{topology}}",
              "capabilities": [],
              "providers": []
            }
            """);
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();

        await component.InvokeAsync(() => component.Instance.LoadAsync(new MemoryStream(input)));

        await Assert.That(component.FindAll("h1")).Count().IsEqualTo(1);
        await Assert.That(component.FindAll("[role=status]").Count).IsEqualTo(1);
        await Assert.That(component.FindAll("a[download]")).Count().IsEqualTo(1);
        await Assert.That(component.Find("pre").TextContent).Contains("community-host");
        await Assert.That(component.Find("pre").GetAttribute("dir")).IsEqualTo("ltr");
    }
}
