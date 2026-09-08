using Explore.Secrets.Configuration;
using Explore.Secrets.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TUnit.Core;

namespace Explore.Secrets.UnitTests.Services;

public class RotationAwareHttpClientFactoryTests : IDisposable
{
    private readonly ILogger<RotationAwareHttpClientFactory> _logger;
    private RotationAwareHttpClientFactory? _factory;

    public RotationAwareHttpClientFactoryTests()
    {
        _logger = Substitute.For<ILogger<RotationAwareHttpClientFactory>>();
    }

    private RotationAwareHttpClientFactory CreateFactory(
        HttpClientCredentialOptions? credentials = null,
        RotationOptions? rotation = null)
    {
        credentials ??= new HttpClientCredentialOptions();
        rotation ??= new RotationOptions { Enabled = true, GracePeriod = TimeSpan.FromSeconds(1) };

        var credentialMonitor = CreateOptionsMonitor(credentials);
        var rotationMonitor = CreateOptionsMonitor(rotation);

        _factory = new RotationAwareHttpClientFactory(credentialMonitor, rotationMonitor, _logger);
        return _factory;
    }

    private static IOptionsMonitor<T> CreateOptionsMonitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    // ==================== Constructor Tests ====================

    [Test]
    public async Task Constructor_WithValidOptions_ShouldSucceed()
    {
        // Arrange & Act
        var factory = CreateFactory();

        // Assert
        await Assert.That(factory).IsNotNull();
        await Assert.That(factory.ActiveClientCount).IsEqualTo(0);
    }

    [Test]
    public async Task Constructor_WithNullCredentialOptions_ShouldThrow()
    {
        // Arrange
        var rotationMonitor = CreateOptionsMonitor(new RotationOptions());

        // Act
        var act = () => new RotationAwareHttpClientFactory(null!, rotationMonitor, _logger);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("credentialOptions");
    }

    [Test]
    public async Task Constructor_WithNullRotationOptions_ShouldThrow()
    {
        // Arrange
        var credentialMonitor = CreateOptionsMonitor(new HttpClientCredentialOptions());

        // Act
        var act = () => new RotationAwareHttpClientFactory(credentialMonitor, null!, _logger);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("rotationOptions");
    }

    [Test]
    public async Task Constructor_WithNullLogger_ShouldThrow()
    {
        // Arrange
        var credentialMonitor = CreateOptionsMonitor(new HttpClientCredentialOptions());
        var rotationMonitor = CreateOptionsMonitor(new RotationOptions());

        // Act
        var act = () => new RotationAwareHttpClientFactory(credentialMonitor, rotationMonitor, null!);

        // Assert
        await Assert.That(act).Throws<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // ==================== CreateClient Tests ====================

    [Test]
    public async Task CreateClient_WithNewName_ShouldCreateClient()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var client = factory.CreateClient("test-client");

        // Assert
        await Assert.That(client).IsNotNull();
        await Assert.That(factory.ActiveClientCount).IsEqualTo(1);
        await Assert.That(factory.HasClient("test-client")).IsTrue();
    }

    [Test]
    public async Task CreateClient_WithSameName_ShouldReturnSameClient()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var client1 = factory.CreateClient("test-client");
        var client2 = factory.CreateClient("test-client");

        // Assert
        await Assert.That(client1).IsSameReferenceAs(client2);
        await Assert.That(factory.ActiveClientCount).IsEqualTo(1);
    }

    [Test]
    public async Task CreateClient_WithDifferentNames_ShouldCreateDifferentClients()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var client1 = factory.CreateClient("client-1");
        var client2 = factory.CreateClient("client-2");

        // Assert
        await Assert.That(ReferenceEquals(client1, client2)).IsFalse();
        await Assert.That(factory.ActiveClientCount).IsEqualTo(2);
    }

    [Test]
    public async Task CreateClient_WithCredentials_ShouldApplyCredentials()
    {
        // Arrange
        string bearerToken = SecretsTestValues.CreateSecret();
        var credentials = new HttpClientCredentialOptions
        {
            Clients = new Dictionary<string, HttpClientCredential>
            {
                ["api-client"] = new HttpClientCredential
                {
                    BaseAddress = "https://api.example.com",
                    BearerToken = bearerToken,
                    Timeout = TimeSpan.FromSeconds(30)
                }
            }
        };
        var factory = CreateFactory(credentials);

        // Act
        var client = factory.CreateClient("api-client");

        // Assert
        await Assert.That(client.BaseAddress).IsEqualTo(new Uri("https://api.example.com"));
        await Assert.That(client.DefaultRequestHeaders.Authorization).IsNotNull();
        await Assert.That(client.DefaultRequestHeaders.Authorization!.Scheme).IsEqualTo("Bearer");
        await Assert.That(client.DefaultRequestHeaders.Authorization.Parameter).IsEqualTo(bearerToken);
        await Assert.That(client.Timeout).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    public async Task CreateClient_WithApiKey_ShouldAddApiKeyHeader()
    {
        // Arrange
        string apiKey = SecretsTestValues.CreateSecret();
        var credentials = new HttpClientCredentialOptions
        {
            Clients = new Dictionary<string, HttpClientCredential>
            {
                ["api-client"] = new HttpClientCredential
                {
                    ApiKey = apiKey
                }
            }
        };
        var factory = CreateFactory(credentials);

        // Act
        var client = factory.CreateClient("api-client");

        // Assert
        await Assert.That(client.DefaultRequestHeaders.TryGetValues("X-API-Key", out var values)).IsTrue();
        await Assert.That(values).Contains(apiKey);
    }

    [Test]
    public async Task CreateClient_WithCustomHeaders_ShouldAddHeaders()
    {
        // Arrange
        var credentials = new HttpClientCredentialOptions
        {
            Clients = new Dictionary<string, HttpClientCredential>
            {
                ["api-client"] = new HttpClientCredential
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Custom-Header"] = "custom-value",
                        ["X-Another-Header"] = "another-value"
                    }
                }
            }
        };
        var factory = CreateFactory(credentials);

        // Act
        var client = factory.CreateClient("api-client");

        // Assert
        await Assert.That(client.DefaultRequestHeaders.TryGetValues("X-Custom-Header", out var values1)).IsTrue();
        await Assert.That(values1).Contains("custom-value");
        await Assert.That(client.DefaultRequestHeaders.TryGetValues("X-Another-Header", out var values2)).IsTrue();
        await Assert.That(values2).Contains("another-value");
    }

    [Test]
    public async Task CreateClient_AfterDispose_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory();
        factory.Dispose();

        // Act
        var act = () => factory.CreateClient("test-client");

        // Assert
        await Assert.That(act).Throws<ObjectDisposedException>();
    }

    // ==================== ForceRotate Tests ====================

    [Test]
    public async Task ForceRotateAsync_WithExistingClient_ShouldRotate()
    {
        // Arrange
        var factory = CreateFactory();
        var originalClient = factory.CreateClient("test-client");
        await Assert.That(factory.ActiveClientCount).IsEqualTo(1);

        // Act
        await factory.ForceRotateAsync("test-client");

        // Assert
        var newClient = factory.CreateClient("test-client");
        await Assert.That(ReferenceEquals(newClient, originalClient)).IsFalse();
    }

    [Test]
    public async Task ForceRotateAsync_WithNonExistentClient_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var act = () => factory.ForceRotateAsync("non-existent");

        // Assert
        await Assert.That(act).Throws<ArgumentException>()
            .WithParameterName("name");
    }

    [Test]
    public async Task ForceRotateAsync_AfterDispose_ShouldThrow()
    {
        // Arrange
        var factory = CreateFactory();
        factory.CreateClient("test-client");
        factory.Dispose();

        // Act
        var act = () => factory.ForceRotateAsync("test-client");

        // Assert
        await Assert.That(act).Throws<ObjectDisposedException>();
    }

    [Test]
    public async Task ForceRotateAllAsync_ShouldRotateAllClients()
    {
        // Arrange
        var factory = CreateFactory();
        var client1 = factory.CreateClient("client-1");
        var client2 = factory.CreateClient("client-2");

        // Act
        await factory.ForceRotateAllAsync();

        // Assert
        var newClient1 = factory.CreateClient("client-1");
        var newClient2 = factory.CreateClient("client-2");
        await Assert.That(ReferenceEquals(newClient1, client1)).IsFalse();
        await Assert.That(ReferenceEquals(newClient2, client2)).IsFalse();
    }

    // ==================== HasClient Tests ====================

    [Test]
    public async Task HasClient_WithExistingClient_ShouldReturnTrue()
    {
        // Arrange
        var factory = CreateFactory();
        factory.CreateClient("test-client");

        // Act
        var result = factory.HasClient("test-client");

        // Assert
        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task HasClient_WithNonExistentClient_ShouldReturnFalse()
    {
        // Arrange
        var factory = CreateFactory();

        // Act
        var result = factory.HasClient("non-existent");

        // Assert
        await Assert.That(result).IsFalse();
    }

    // ==================== Dispose Tests ====================

    [Test]
    public async Task Dispose_ShouldClearClients()
    {
        // Arrange
        var factory = CreateFactory();
        factory.CreateClient("client-1");
        factory.CreateClient("client-2");
        await Assert.That(factory.ActiveClientCount).IsEqualTo(2);

        // Act
        factory.Dispose();

        // Assert
        await Assert.That(factory.ActiveClientCount).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var factory = CreateFactory();
        factory.CreateClient("test-client");

        // Act
        var act = () =>
        {
            factory.Dispose();
            factory.Dispose();
            factory.Dispose();
        };

        // Assert
        await Assert.That(act).ThrowsNothing();
    }

    public void Dispose()
    {
        _factory?.Dispose();
    }
}
