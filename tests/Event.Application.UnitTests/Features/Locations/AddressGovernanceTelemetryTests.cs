using Explore.Application.Authorization;
using Explore.Application.Behaviors;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Location;
using Explore.Application.Features.Locations.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Event.Application.UnitTests.Features.Locations;

public sealed class AddressGovernanceTelemetryTests
{
    [Test]
    public async Task UpdateAuthorizationDenialLogExcludesLocationAndAddressCanaries()
    {
        Guid locationId = Guid.Parse("019d2f35-47d8-7b2d-96d3-570cc42f8c11");
        Guid concurrencyStamp = Guid.Parse("019d2f35-866c-790c-8dcd-eed429c4f322");
        const string address = "TELEMETRY-ADDRESS-CANARY";
        const string postcode = "PII-1040";
        var authorization = Substitute.For<IAuthorizationProvider>();
        authorization.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(AuthorizationDecision.Deny(
                AuthorizationProviderMetadata.Runtime,
                AuthorizationDecisionReasonCodes.Denied));
        var logger = new CaptureLogger<AuthorizationBehavior<UpdateLocationCommand, BaseCommandResponse<Guid>>>();
        var behavior = new AuthorizationBehavior<UpdateLocationCommand, BaseCommandResponse<Guid>>(
            authorization,
            logger);
        var command = new UpdateLocationCommand
        {
            LocationId = locationId,
            ExpectedConcurrencyStamp = concurrencyStamp,
            UpdateLocationDto = new UpdateLocationDto
            {
                Address = new UpdateLocationAddressDto { Value = address },
                Postcode = new UpdateLocationPostcodeDto { Value = postcode }
            }
        };

        await Assert.That(async () => await behavior.Handle(
            command,
            _ => Task.FromResult(BaseCommandResponse.Success(Guid.Empty)),
            CancellationToken.None)).Throws<Explore.Application.Exceptions.AuthorizationException>();

        CapturedLog warning = logger.Entries.Single(entry => entry.Level == LogLevel.Warning);
        string observable = warning.Observable;
        await Assert.That(observable).DoesNotContain(locationId.ToString("D"));
        await Assert.That(observable).DoesNotContain(concurrencyStamp.ToString("D"));
        await Assert.That(observable).DoesNotContain(address);
        await Assert.That(observable).DoesNotContain(postcode);
        await Assert.That(warning.State.Keys).DoesNotContain("ResourceId");
    }

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<CapturedLog> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new CapturedLog(
                logLevel,
                formatter(state, exception),
                properties.ToDictionary(property => property.Key, property => property.Value),
                exception));
        }
    }

    private sealed record CapturedLog(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> State,
        Exception? Exception)
    {
        public string Observable => string.Join('|',
            Message,
            string.Join('|', State.Keys),
            string.Join('|', State.Values.Select(value => value?.ToString() ?? string.Empty)),
            Exception?.ToString() ?? string.Empty);
    }
}
