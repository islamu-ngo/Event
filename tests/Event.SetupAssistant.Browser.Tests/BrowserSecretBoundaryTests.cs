namespace Event.SetupAssistant.Browser.Tests;

using System.Security.Cryptography;
using System.Text;
using Bunit;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Browser;
using ISLAMU.Event.SetupAssistant.Browser.Pages;

public sealed class BrowserSecretBoundaryTests
{
    [Test]
    public async Task RestrictedFieldsAreRejectedWithoutPreviewOrDownload()
    {
        string confidentialValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema": "event-setup-public-manifest/v1",
              "kind": "public-template",
              "name": "community-host",
              "topology": "single",
              "capabilities": [],
              "providers": [],
              "operatorIdentity": "{{confidentialValue}}"
            }
            """);

        BrowserManifestResult result = BrowserPublicManifest.ValidateAndPreview(manifest);

        await Assert.That(result.IsAccepted).IsFalse();
        await Assert.That(result.Preview).IsNull();
        await Assert.That(result.DownloadHref).IsNull();
        await Assert.That(result.Status).DoesNotContain(confidentialValue);
    }

    [Test]
    public async Task UnknownKindsAreRejectedWithoutRetainingGeneratedContent()
    {
        string confidentialValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema": "event-setup-public-manifest/v1",
              "kind": "future-unknown-kind",
              "name": "{{confidentialValue}}",
              "topology": "single",
              "capabilities": [],
              "providers": []
            }
            """);

        BrowserManifestResult result = BrowserPublicManifest.ValidateAndPreview(manifest);

        await Assert.That(result.IsAccepted).IsFalse();
        await Assert.That(result.Preview).IsNull();
        await Assert.That(result.DownloadHref).IsNull();
        await Assert.That(result.Status).DoesNotContain(confidentialValue);
    }

    [Test]
    public async Task RenderedRejectionDoesNotExposeConfidentialInput()
    {
        string confidentialValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        byte[] manifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema": "event-setup-public-manifest/v1",
              "kind": "public-template",
              "name": "community-host",
              "topology": "single",
              "capabilities": [],
              "providers": [],
              "secret": "{{confidentialValue}}"
            }
            """);
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();

        using (MemoryStream stream = new(manifest))
        {
            await component.InvokeAsync(() => component.Instance.LoadAsync(stream));
        }

        await Assert.That(component.Markup).DoesNotContain(confidentialValue);
        await Assert.That(component.FindAll("a[download]")).IsEmpty();
        await Assert.That(component.FindAll("[role=alert]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task RejectedUploadClearsEarlierPublicPreviewAndDownload()
    {
        string topology = PlatformEnvironmentCatalogue.Catalogue.Topologies[0];
        byte[] publicManifest = Encoding.UTF8.GetBytes(
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
        string confidentialValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        byte[] restrictedManifest = Encoding.UTF8.GetBytes(
            $$"""
            {
              "schema": "event-setup-public-manifest/v1",
              "kind": "public-template",
              "name": "community-host",
              "topology": "{{topology}}",
              "capabilities": [],
              "providers": [],
              "identity": "{{confidentialValue}}"
            }
            """);
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();
        using (MemoryStream publicStream = new(publicManifest))
        {
            await component.InvokeAsync(() =>
                component.Instance.LoadAsync(publicStream));
        }

        using (MemoryStream restrictedStream = new(restrictedManifest))
        {
            await component.InvokeAsync(() =>
                component.Instance.LoadAsync(restrictedStream));
        }

        await Assert.That(component.FindAll("a[download]")).IsEmpty();
        await Assert.That(component.FindAll("pre")).IsEmpty();
        await Assert.That(component.Markup).DoesNotContain(confidentialValue);
        await Assert.That(component.FindAll("[role=alert]").Count).IsEqualTo(1);
    }
}
