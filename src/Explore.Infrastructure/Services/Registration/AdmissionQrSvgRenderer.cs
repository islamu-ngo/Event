using System.Globalization;
using System.Text;
using ISLAMU.Wire.Contracts.Admissions;
using Explore.Application.Contracts.Admissions;
using Net.Codecrete.QrCodeGenerator;

namespace Explore.Infrastructure.Services.Registration;

public sealed class AdmissionQrSvgRenderer : IAdmissionQrRenderer
{
    private const int QuietZoneModules = 4;
    private const int MaximumSvgLength = 262_144;

    public AdmissionQrSvg Render(AdmissionQrPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string encodedPayload = AdmissionQrPayloadCodec.Encode(payload.Bearer);
        if (!AdmissionQrPayloadCodec.TryDecode(encodedPayload, out _))
        {
            throw new ArgumentException("Admission QR payload is invalid.", nameof(payload));
        }

        QrCode code = QrCode.EncodeText(encodedPayload, QrCode.Ecc.Quartile);
        int viewBoxSize = code.Size + (QuietZoneModules * 2);
        var path = new StringBuilder(code.Size * code.Size * 6);
        for (int y = 0; y < code.Size; y++)
        {
            for (int x = 0; x < code.Size; x++)
            {
                if (!code.GetModule(x, y))
                {
                    continue;
                }

                path.Append('M').Append(x + QuietZoneModules).Append(' ')
                    .Append(y + QuietZoneModules).Append("h1v1h-1z");
            }
        }

        string svg = string.Create(
            CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {viewBoxSize} {viewBoxSize}\" shape-rendering=\"crispEdges\"><rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"{path}\"/></svg>");
        if (svg.Length > MaximumSvgLength)
        {
            throw new InvalidOperationException("Admission QR rendering exceeded the output bound.");
        }

        return new AdmissionQrSvg(svg, code.Size, QuietZoneModules);
    }
}
