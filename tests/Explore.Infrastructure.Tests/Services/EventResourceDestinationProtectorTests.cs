using System.Security.Cryptography;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Explore.Infrastructure.Tests.Services;

public sealed class EventResourceDestinationProtectorTests
{
    [Test]
    public async Task FullDestinationIsOpaqueAndCannotCrossTenantResourceVersionOrOperation()
    {
        using var provider = CreateEphemeralProvider();
        var keys = provider.GetRequiredService<IDataProtectionProvider>();
        IEventResourceDestinationProtector protector = new EventResourceDestinationProtector(keys);
        var tenant = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var destination = $"https://example.org/meeting/{credential}?invite={credential}#fragment";
        await Assert.That(protector.CurrentVersion).IsEqualTo(1);
        var encrypted = protector.Protect(destination, tenant, resource, protector.CurrentVersion);

        await Assert.That(encrypted.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(encrypted.Contains("example.org", StringComparison.Ordinal)).IsFalse();
        await Assert.That(protector.Unprotect(encrypted, tenant, resource, 1)).IsEqualTo(destination);
        await Assert.That(() => protector.Unprotect(encrypted, Guid.NewGuid(), resource, 1)).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(encrypted, tenant, Guid.NewGuid(), 1)).Throws<CryptographicException>();
        await Assert.That(() => protector.Unprotect(encrypted, tenant, resource, 2)).Throws<CryptographicException>();
        await Assert.That(() => keys.CreateProtector("EventResource", "OtherOperation", "v1", tenant.ToString("D"), resource.ToString("D"))
            .Unprotect(encrypted)).Throws<CryptographicException>();
        var tampered = encrypted[..^2] + (encrypted[^2] == 'A' ? 'B' : 'A') + encrypted[^1];
        await Assert.That(() => protector.Unprotect(tampered, tenant, resource, 1)).Throws<CryptographicException>();
    }

    [Test]
    public async Task MissingKeyFailsWithoutLeakingDestinationOrCredentialInDiagnostics()
    {
        var credential = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var destination = $"https://example.org/{credential}?token={credential}";
        var tenant = Guid.NewGuid();
        var resource = Guid.NewGuid();
        var logs = new List<string>();
        using var logger = LoggerFactory.Create(builder => builder.AddProvider(new RecordingLoggerProvider(logs)));
        using var original = CreateEphemeralProvider(logger);
        using var missing = CreateEphemeralProvider(logger);
        var token = new EventResourceDestinationProtector(original.GetRequiredService<IDataProtectionProvider>())
            .Protect(destination, tenant, resource, 1);
        var other = new EventResourceDestinationProtector(missing.GetRequiredService<IDataProtectionProvider>());
        var failure = await Assert.That(() => other.Unprotect(token, tenant, resource, 1)).Throws<CryptographicException>();
        await Assert.That(failure!.Message.Contains(destination, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.Message.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Join("\n", logs).Contains(destination, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Join("\n", logs).Contains(credential, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task RetainedRetiredKeysSurviveRotationAndRestartButChangedApplicationIdentityDoesNot()
    {
        var ring = Directory.CreateTempSubdirectory("resource-destination-keys-");
        try
        {
            var tenant = Guid.NewGuid();
            var resource = Guid.NewGuid();
            var destination = $"https://example.org/{Guid.NewGuid():N}?ref={Guid.NewGuid():N}";
            string protectedDestination;
            using (var first = CreatePersistentProvider(ring, "islamu-event"))
            {
                protectedDestination = new EventResourceDestinationProtector(first.GetRequiredService<IDataProtectionProvider>())
                    .Protect(destination, tenant, resource, 1);
                var manager = first.GetRequiredService<IKeyManager>();
                var now = DateTimeOffset.UtcNow;
                manager.CreateNewKey(now.AddMinutes(-1), now.AddDays(14));
            }

            using var restarted = CreatePersistentProvider(ring, "islamu-event");
            var restored = new EventResourceDestinationProtector(restarted.GetRequiredService<IDataProtectionProvider>());
            await Assert.That(restored.Unprotect(protectedDestination, tenant, resource, 1)).IsEqualTo(destination);
            await Assert.That(restored.Unprotect(restored.Protect(destination, tenant, resource, 1), tenant, resource, 1))
                .IsEqualTo(destination);
            using var otherApp = CreatePersistentProvider(ring, "other-application");
            var wrongIdentity = new EventResourceDestinationProtector(otherApp.GetRequiredService<IDataProtectionProvider>());
            await Assert.That(() => wrongIdentity.Unprotect(protectedDestination, tenant, resource, 1))
                .Throws<CryptographicException>();
        }
        finally
        {
            ring.Delete(recursive: true);
        }
    }

    private static ServiceProvider CreateEphemeralProvider(ILoggerFactory? logger = null)
    {
        var services = new ServiceCollection();
        if (logger is not null) services.AddSingleton(logger);
        services.AddDataProtection().UseEphemeralDataProtectionProvider().SetApplicationName("islamu-event");
        return services.BuildServiceProvider();
    }

    private static ServiceProvider CreatePersistentProvider(DirectoryInfo directory, string applicationName)
    {
        var services = new ServiceCollection();
        services.AddDataProtection().PersistKeysToFileSystem(directory).SetApplicationName(applicationName);
        return services.BuildServiceProvider();
    }

    private sealed class RecordingLoggerProvider(List<string> messages) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new RecordingLogger(messages);
        public void Dispose() { }

        private sealed class RecordingLogger(List<string> messages) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
