using Microsoft.AspNetCore.DataProtection;

namespace Explore.Infrastructure.Tests.Geocoding;

internal sealed class PhotonRecordingDataProtectionProvider(IDataProtectionProvider inner)
    : IDataProtectionProvider
{
    public List<string> Purposes { get; } = [];

    public IDataProtector CreateProtector(string purpose)
    {
        Purposes.Add(purpose);
        return new RecordingProtector(inner.CreateProtector(purpose), Purposes);
    }

    private sealed class RecordingProtector(IDataProtector inner, List<string> purposes) : IDataProtector
    {
        public IDataProtector CreateProtector(string purpose)
        {
            purposes.Add(purpose);
            return new RecordingProtector(inner.CreateProtector(purpose), purposes);
        }

        public byte[] Protect(byte[] plaintext) => inner.Protect(plaintext);

        public byte[] Unprotect(byte[] protectedData) => inner.Unprotect(protectedData);
    }
}
