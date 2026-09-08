using System.Security.Cryptography;
using System.Text;

namespace Explore.Application.Services.Registration;

public static class AdmissionIssuanceIdentityFactory
{
    public static Guid Create(Guid tenantId, Guid finalizationEffectId, Guid assignmentId, string purpose)
    {
        if (tenantId == Guid.Empty || finalizationEffectId == Guid.Empty || assignmentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Complete admission issuance identity lineage is required.");
        }

        byte[] material = Encoding.UTF8.GetBytes(
            $"admission:v1:{tenantId:N}:{finalizationEffectId:N}:{assignmentId:N}:{purpose.Trim()}");
        byte[] bytes = SHA256.HashData(material)[..16];
        byte[] effectBytes = finalizationEffectId.ToByteArray();
        Array.Copy(effectBytes, 0, bytes, 0, 6);
        bytes[7] = (byte)((bytes[7] & 0x0f) | 0x70);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }
}
