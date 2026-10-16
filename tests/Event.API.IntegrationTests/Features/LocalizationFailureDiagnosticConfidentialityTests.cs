using System.Security.Claims;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Localization;
using Explore.Application.Features.Localization.Handlers.Commands;
using Explore.Application.Features.Localization.Requests.Commands;
using Explore.Application.Telemetry;
using Explore.Infrastructure.Localization;
using Explore.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalizationFailureDiagnosticConfidentialityTests
{
    [Test]
    public async Task ProviderAndPersistenceFailuresDoNotExportPrivateExceptionValues()
    {
        var canary = $"{Guid.NewGuid():N}\r\nprivate-bundle";
        await using var fixture = await LocalAdmissionWebApplicationFactory.CreateAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var userId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        var role = await database.Set<Explore.Domain.Role>()
            .SingleAsync(item => item.MasterCode == "platform.admin");
        database.PlatformUserRoles.Add(new Explore.Domain.PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            User = await database.Users.SingleAsync(user => user.Id == userId),
            RoleId = role.Id,
            Role = role
        });
        await database.SaveChangesAsync();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "native-test"))
        };
        var admin = scope.ServiceProvider.GetRequiredService<IAdminContext>();
        await Assert.That(await admin.IsInstanceAdminAsync(userId)).IsTrue();
        var provider = Substitute.For<ITranslationManagementProvider>();
        provider.ExportTranslationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IEnumerable<TranslationExport>>(new InvalidOperationException(canary)));
        string root = Path.Combine(Path.GetTempPath(), $"localization-failure-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(root);
        string blocker = Path.Combine(root, "App_Data");
        await File.WriteAllTextAsync(blocker, canary);
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.ContentRootPath.Returns(root);
        var writerLogger = new Collector<BundleFileWriter>();
        var writer = new BundleFileWriter(environment, writerLogger);
        var resolver = scope.ServiceProvider.GetRequiredService<ITranslationResolver>();
        var metrics = scope.ServiceProvider.GetRequiredService<TranslationMetrics>();
        var exportLogger = new Collector<ExportFromTmsCommandHandler>();
        var export = new ExportFromTmsCommandHandler(admin, provider, resolver, writer, metrics, exportLogger);

        try
        {
            var providerFailure = await export.ExecuteAsync(new ExportFromTmsCommand { LanguageCode = "en" }, CancellationToken.None);
            await Assert.That(providerFailure.IsSuccess).IsFalse();
            await Assert.That(writerLogger.Exports.Count).IsEqualTo(0);
            provider.ExportTranslationsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns([new TranslationExport("ui.test", canary)]);
            var persistenceFailure = await export.ExecuteAsync(new ExportFromTmsCommand { LanguageCode = "en" }, CancellationToken.None);
            await Assert.That(persistenceFailure.IsSuccess).IsFalse();
            await Assert.That(writerLogger.Exports.Count).IsEqualTo(1);

            var importLogger = new Collector<ImportLocalizationBundleCommandHandler>();
            var import = new ImportLocalizationBundleCommandHandler(admin, writer, resolver, metrics, importLogger);
            var importFailure = await import.ExecuteAsync(new ImportLocalizationBundleCommand
            {
                Dto = new ImportLocalizationBundleDto
                {
                    LanguageCode = "en",
                    Translations = new Dictionary<string, string> { ["ui.test"] = canary }
                }
            }, CancellationToken.None);
            await Assert.That(importFailure.IsSuccess).IsFalse();
            await Assert.That(writerLogger.Exports.Count).IsEqualTo(2);
            var invalidImport = await import.ExecuteAsync(new ImportLocalizationBundleCommand
            {
                Dto = new ImportLocalizationBundleDto
                {
                    LanguageCode = "en",
                    Translations = new Dictionary<string, string>
                    {
                        ["ui.test"] = new string('x',
                            Explore.Domain.Common.Localization.LocalizationBundlePolicy.MaximumValueCharacters + 1)
                    }
                }
            }, CancellationToken.None);
            await Assert.That(invalidImport.IsSuccess).IsFalse();
            await Assert.That(writerLogger.Exports.Count).IsEqualTo(2);
            await Assert.That(await File.ReadAllTextAsync(blocker)).IsEqualTo(canary);
            var entries = exportLogger.Exports.Concat(importLogger.Exports).ToArray();
            await Assert.That(entries.Length).IsEqualTo(3);
            var finalOutput = JsonSerializer.Serialize(new[] { providerFailure, persistenceFailure, importFailure })
                + string.Join("", entries) + string.Join("", writerLogger.Exports);
            await Assert.That(finalOutput.Contains(canary[..32], StringComparison.Ordinal)).IsFalse();
            await Assert.That(finalOutput.Contains(root, StringComparison.Ordinal)).IsFalse();
            await Assert.That(entries.Concat(writerLogger.Exports)
                .All(entry => entry.Contains("\"Exception\":null", StringComparison.Ordinal))).IsTrue();
            var categories = new HashSet<string?>();
            foreach (var entry in entries)
            {
                using var document = JsonDocument.Parse(entry);
                categories.Add(document.RootElement.GetProperty("Message").GetString());
            }
            await Assert.That(categories.Count).IsEqualTo(3);
        }
        finally
        {
            scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = null;
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Collector<T> : ILogger<T>
    {
        public List<string> Exports { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Exports.Add(JsonSerializer.Serialize(new
            {
                Message = formatter(state, exception),
                State = state is IEnumerable<KeyValuePair<string, object?>> properties ? properties.ToArray() : [],
                Exception = exception?.ToString()
            }));
    }

}
