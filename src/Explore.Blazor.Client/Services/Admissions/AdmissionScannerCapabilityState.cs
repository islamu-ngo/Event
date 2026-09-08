using Explore.Blazor.Client.Contracts.Services.Admissions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Explore.Blazor.Client.Services.Admissions;

public sealed class AdmissionScannerCapabilityState : IAdmissionScannerCapabilityState
{
    public const int MaximumCapabilityLength = 256;

    private readonly NavigationManager _navigation;
    private string? _capability;
    private bool _disposed;

    public AdmissionScannerCapabilityState(NavigationManager navigation)
    {
        _navigation = navigation;
        _navigation.LocationChanged += OnLocationChanged;
    }

    public bool IsActive => !_disposed && _capability is not null;
    public long Generation { get; private set; }

    public void Activate(string capability)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);
        if (capability.Length > MaximumCapabilityLength ||
            !string.Equals(capability, capability.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Scanner capability material is invalid.", nameof(capability));
        }

        ClearCore();
        _capability = capability;
    }

    public void Clear()
    {
        if (!_disposed)
        {
            ClearCore();
        }
    }

    internal bool TryGetCapability(out string? capability)
    {
        capability = _disposed ? null : _capability;
        return capability is not null;
    }

    public override string ToString() =>
        $"AdmissionScannerCapabilityState(active={IsActive}, generation={Generation}, <redacted>)";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _navigation.LocationChanged -= OnLocationChanged;
        ClearCore();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args) => Clear();

    private void ClearCore()
    {
        _capability = null;
        Generation++;
    }
}
