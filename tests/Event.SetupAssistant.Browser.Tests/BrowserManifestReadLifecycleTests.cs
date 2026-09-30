namespace Event.SetupAssistant.Browser.Tests;

using System.Text;
using Bunit;
using ISLAMU.Event.Setup.Core.Environment;
using ISLAMU.Event.SetupAssistant.Browser.Pages;
using Microsoft.AspNetCore.Components.Forms;

public sealed class BrowserManifestReadLifecycleTests
{
    private const string DownloadAnchor = "a[download]";
    private const string PreElement = "pre";

    [Test]
    public async Task RejectedUploadReleasesTheSelectedFileControl()
    {
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();
        var input = component.FindComponent<InputFile>();
        InputFile selectedControl = input.Instance;

        await component.InvokeAsync(() => input.UploadFiles(
            InputFileContent.CreateFromBinary(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
                "invalid.json",
                contentType: "application/json")));

        await Assert.That(ReferenceEquals(
            selectedControl, component.FindComponent<InputFile>().Instance)).IsFalse();
        await Assert.That(component.FindAll(DownloadAnchor)).IsEmpty();
    }

    [Test]
    public async Task FailedReadClearsPreviouslyAcceptedDownload()
    {
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();
        using var valid = new MemoryStream(PublicManifest());
        await component.InvokeAsync(() => component.Instance.LoadAsync(valid));
        await Assert.That(component.FindAll(DownloadAnchor).Count).IsEqualTo(1);
        using var failed = new FailedReadStream();

        await component.InvokeAsync(() => component.Instance.LoadAsync(failed));

        await Assert.That(component.FindAll(DownloadAnchor)).IsEmpty();
        await Assert.That(component.FindAll(PreElement)).IsEmpty();
        await Assert.That(component.FindAll("[role=alert]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task OlderReadCannotRestoreDownloadAfterNewerRejection()
    {
        using var context = new BunitContext();
        var component = context.Render<ManifestPreview>();
        using var older = new GatedReadStream(PublicManifest());
        Task pending = component.InvokeAsync(() => component.Instance.LoadAsync(older));
        await older.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            using var invalid = new MemoryStream("invalid"u8.ToArray());
            await component.InvokeAsync(() => component.Instance.LoadAsync(invalid));
        }
        finally
        {
            older.Release.TrySetResult();
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(component.FindAll(DownloadAnchor)).IsEmpty();
        await Assert.That(component.FindAll(PreElement)).IsEmpty();
    }

    private static byte[] PublicManifest() => Encoding.UTF8.GetBytes(
        $$"""
        {"schema":"event-setup-public-manifest/v1","kind":"public-template",
         "name":"community-host","topology":"{{PlatformEnvironmentCatalogue.Catalogue.Topologies[0]}}",
         "capabilities":[],"providers":[]}
        """);

    private sealed class FailedReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("read-failed"));
    }

    private sealed class GatedReadStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
