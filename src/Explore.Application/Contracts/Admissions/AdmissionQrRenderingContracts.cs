using ISLAMU.Wire.Contracts.Admissions;

namespace Explore.Application.Contracts.Admissions;

public interface IAdmissionQrRenderer
{
    AdmissionQrSvg Render(AdmissionQrPayload payload);
}

public sealed class AdmissionQrSvg
{
    public AdmissionQrSvg(string content, int moduleCount, int quietZoneModules)
    {
        Content = content;
        ModuleCount = moduleCount;
        QuietZoneModules = quietZoneModules;
    }

    public string Content { get; }
    public int ModuleCount { get; }
    public int QuietZoneModules { get; }
    public int ViewBoxSize => ModuleCount + (QuietZoneModules * 2);

    public override string ToString() =>
        $"AdmissionQrSvg(modules={ModuleCount}, quietZone={QuietZoneModules}, <redacted>)";
}
