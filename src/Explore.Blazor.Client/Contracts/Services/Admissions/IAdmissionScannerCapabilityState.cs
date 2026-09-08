namespace Explore.Blazor.Client.Contracts.Services.Admissions;

public interface IAdmissionScannerCapabilityState : IDisposable
{
    bool IsActive { get; }
    long Generation { get; }

    void Activate(string capability);
    void Clear();
}
