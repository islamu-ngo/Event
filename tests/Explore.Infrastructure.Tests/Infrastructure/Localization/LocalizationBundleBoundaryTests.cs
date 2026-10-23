using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Common.Localization;
using Explore.Infrastructure.Localization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure.Localization;

public sealed class LocalizationBundleBoundaryTests
{
    [Test]
    public async Task WriteFailureDoesNotPropagatePrivateFilesystemException()
    {
        string root = NewRoot();
        Directory.CreateDirectory(root);
        string blocker = Path.Combine(root, "App_Data");
        await File.WriteAllTextAsync(blocker, Guid.NewGuid().ToString("N"));
        var logger = new BundleLogger();
        try
        {
            BundleWriteException? failure = null;
            try
            {
                await new BundleFileWriter(Environment(root), logger).WriteBundleAsync("en",
                    new Dictionary<string, string> { ["ui.test"] = "Valid" });
            }
            catch (BundleWriteException exception)
            {
                failure = exception;
            }

            await Assert.That(failure).IsNotNull();
            await Assert.That(failure!.InnerException).IsNull();
            await Assert.That(failure.ToString()).DoesNotContain(root);
            await Assert.That(File.Exists(blocker)).IsTrue();
            await Assert.That(logger.Events.Single().Exception).IsNull();
            await Assert.That(logger.Events.Single().Text).DoesNotContain(root);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task RejectedBundleDoesNotExportFilesystemCoordinatesOrRawExceptions()
    {
        string root = NewRoot();
        var logger = new BundleLogger();
        var writer = new BundleFileWriter(Environment(root), logger);
        var privateText = $"{Guid.NewGuid():N}@example.invalid\r\nforged-event";
        try
        {
            await writer.WriteBundleAsync("en", new Dictionary<string, string> { ["ui.text"] = privateText });
            await Assert.That(async () => await writer.WriteBundleAsync("en",
                new Dictionary<string, string> { ["invalid"] = privateText }))
                .Throws<BundleWriteException>();

            await Assert.That(logger.Events.Count).IsEqualTo(2);
            foreach (var recorded in logger.Events)
            {
                await Assert.That(recorded.Exception).IsNull();
                await Assert.That(recorded.Text).DoesNotContain(root);
                await Assert.That(recorded.Text).DoesNotContain(privateText);
                foreach (var field in recorded.Fields.Values)
                {
                    await Assert.That(field?.ToString() ?? string.Empty).DoesNotContain(root);
                    await Assert.That(field?.ToString() ?? string.Empty).DoesNotContain(privateText);
                }
            }
            await Assert.That(logger.Events.Single(item => item.Level == LogLevel.Information)
                .Fields["KeyCount"]).IsEqualTo(1);
            await Assert.That(logger.Events.Single(item => item.Level == LogLevel.Error)
                .Fields.Keys).DoesNotContain("Path");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task Write_InvalidBundleDoesNotCreateOutputDirectoryOrReplaceExistingFile()
    {
        string root = NewRoot();
        var writer = Writer(root);
        var invalid = new Dictionary<string, string>
        {
            ["ui.test"] = new string('x', LocalizationBundlePolicy.MaximumValueCharacters + 1)
        };
        try
        {
            await Assert.That(async () => await writer.WriteBundleAsync("en", invalid))
                .Throws<BundleWriteException>();
            await Assert.That(Directory.Exists(root)).IsFalse();

            string path = await writer.WriteBundleAsync("en", new Dictionary<string, string>
            {
                ["ui.test"] = "Original"
            });
            byte[] original = await File.ReadAllBytesAsync(path);
            await Assert.That(async () => await writer.WriteBundleAsync("en", invalid))
                .Throws<BundleWriteException>();
            await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(original);
            await Assert.That(File.Exists(path + ".tmp")).IsFalse();

            await File.WriteAllTextAsync(path + ".tmp", "Other pending output");
            await Assert.That(async () => await writer.WriteBundleAsync("en", invalid))
                .Throws<BundleWriteException>();
            await Assert.That(await File.ReadAllTextAsync(path + ".tmp")).IsEqualTo("Other pending output");
            await Assert.That(await File.ReadAllBytesAsync(path)).IsEquivalentTo(original);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task Write_LiteralTemplateTextRoundTripsThroughBoundedFileAndReader()
    {
        string root = NewRoot();
        const string value = "{{operator.secret}} <script>alert(1)</script> $(whoami) & \u0627\u0644\u0639\u0631\u0628\u064a\u0629";
        try
        {
            string path = await Writer(root).WriteBundleAsync("en",
                new Dictionary<string, string> { ["ui.boundary.literal"] = value });
            byte[] output = await File.ReadAllBytesAsync(path);
            await Assert.That(output.Length).IsLessThanOrEqualTo(LocalizationBundlePolicy.MaximumBytes);
            using JsonDocument parsed = JsonDocument.Parse(output);
            await Assert.That(parsed.RootElement.GetProperty("ui.boundary.literal").GetString()).IsEqualTo(value);

            var read = await Reader(root).ReadBundleAsync("en");
            await Assert.That(read["ui.boundary.literal"]).IsEqualTo(value);
            await Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)!).Length).IsEqualTo(1);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    [Arguments("""{"ui.common.appName":"First","ui.common.appName":"Second"}""")]
    [Arguments("""{"ui.common.appName":{"$type":"System.Object"}}""")]
    [Arguments("""{"ui.common.appName":[[[["nested"]]]]}""")]
    public async Task Read_RejectsEntireAmbiguousOverrideAndRetainsEmbeddedDefaults(string json)
    {
        string root = NewRoot();
        try
        {
            await WriteRaw(root, json);
            var read = await Reader(root).ReadBundleAsync("en");
            await Assert.That(read["ui.common.appName"]).IsEqualTo("ISLAMU Event");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Test]
    public async Task Read_RejectsOversizedStreamAndValueWithoutPublishingPartialOverrides()
    {
        string root = NewRoot();
        try
        {
            await WriteRaw(root, "{\"ui.common.appName\":\"Override\","
                + new string(' ', LocalizationBundlePolicy.MaximumBytes) + "\"ui.test\":\"data\"}");
            await Assert.That((await Reader(root).ReadBundleAsync("en"))["ui.common.appName"])
                .IsEqualTo("ISLAMU Event");

            await WriteRaw(root, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["ui.common.appName"] = "Override",
                ["ui.test"] = new string('x', LocalizationBundlePolicy.MaximumValueCharacters + 1)
            }));
            await Assert.That((await Reader(root).ReadBundleAsync("en"))["ui.common.appName"])
                .IsEqualTo("ISLAMU Event");
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), $"localization-boundary-{Guid.NewGuid():N}");

    private static IWebHostEnvironment Environment(string root)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(root);
        return environment;
    }

    private static BundleFileWriter Writer(string root) =>
        new(Environment(root), NullLogger<BundleFileWriter>.Instance);

    private static OfflineTranslationProvider Reader(string root) =>
        new(NullLogger<OfflineTranslationProvider>.Instance, Environment(root));

    private static async Task WriteRaw(string root, string json)
    {
        string directory = Path.Combine(root, "App_Data", "Localization", "Bundles");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "en.json"), json);
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class BundleLogger : ILogger<BundleFileWriter>
    {
        public List<LoggedEvent> Events { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Events.Add(new LoggedEvent(level, formatter(state, exception), exception,
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!)
                .ToDictionary(field => field.Key, field => field.Value)));
    }

    private sealed record LoggedEvent(LogLevel Level, string Text, Exception? Exception,
        Dictionary<string, object?> Fields);
}
