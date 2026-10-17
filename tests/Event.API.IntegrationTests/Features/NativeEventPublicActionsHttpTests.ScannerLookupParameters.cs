using System.Data.Common;
using System.Diagnostics;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Admissions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventPublicActionsHttpTests
{
    [Test]
    public async Task PortableScannerLookupBindsAnAdversarialDigestAsAValue()
    {
        await using var factory = new NativeEventSeriesFactory();
        await Seed(factory);
        using var scope = Scope(factory);
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        const string candidate = "digest'; SELECT 1; --";
        using var observation = new ScannerParameterObservation(database.Database.GetDbConnection().DataSource, candidate);
        var repository = scope.ServiceProvider.GetRequiredService<IAdmissionScannerAuthenticationRepository>();

        var result = await repository.FindByDigestCandidatesAsync(
            [new AdmissionScannerCapabilityDigestCandidate(1, candidate)], default);

        await Assert.That(result).IsNull();
        await Assert.That(observation.SawBoundDigest).IsTrue();
    }

    private sealed class ScannerParameterObservation :
        IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string _dataSource;
        private readonly string _candidate;
        private readonly List<IDisposable> _subscriptions = [];
        private readonly IDisposable _listeners;
        private readonly object _gate = new();
        private bool _disposed;
        private int _sawBoundDigest;
        public bool SawBoundDigest => Volatile.Read(ref _sawBoundDigest) != 0;

        public ScannerParameterObservation(string dataSource, string candidate)
        {
            _dataSource = dataSource;
            _candidate = candidate;
            _listeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name != DbLoggerCategory.Name)
                return;
            lock (_gate)
            {
                if (!_disposed)
                    _subscriptions.Add(listener.Subscribe(this,
                        name => name == RelationalEventId.CommandExecuting.Name));
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is CommandEventData command
                && command.Command.Connection?.DataSource == _dataSource
                && command.Command.Parameters.Cast<DbParameter>()
                    .Any(parameter => Equals(parameter.Value, _candidate)))
                Interlocked.Exchange(ref _sawBoundDigest, 1);
        }

        public void OnError(Exception error) => throw error;
        public void OnCompleted() { }

        public void Dispose()
        {
            _listeners.Dispose();
            lock (_gate)
            {
                _disposed = true;
                foreach (var subscription in _subscriptions)
                    subscription.Dispose();
            }
        }
    }
}
