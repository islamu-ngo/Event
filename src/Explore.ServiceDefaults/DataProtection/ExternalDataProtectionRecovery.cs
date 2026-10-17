using System.Text.Json;
using System.Xml.Linq;

namespace Explore.ServiceDefaults.DataProtection;

/// <summary>Operator recovery validates pinned history at issuance, never admits it to serve traffic.</summary>
public static class ExternalDataProtectionRecovery
{
    public static (ExternalDataProtectionAdmission Admission, XElement[] Documents) ReadPinnedHistory(
        ExternalDataProtectionInput input, string partition)
    {
        if (string.IsNullOrWhiteSpace(input.Admission)
            || System.Text.Encoding.UTF8.GetByteCount(input.Admission) > 32 * 1024)
            throw ExternalDataProtectionRingAdmission.Invalid();
        try
        {
            var admission = JsonSerializer.Deserialize<ExternalDataProtectionAdmission>(input.Admission)
                ?? throw ExternalDataProtectionRingAdmission.Invalid();
            return ExternalDataProtectionRingAdmission.Validate(input, partition, admission.NotBeforeUtc);
        }
        catch (JsonException) { throw ExternalDataProtectionRingAdmission.Invalid(); }
    }
}
