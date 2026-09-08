namespace Explore.Application.Services;

using System.Globalization;

public readonly struct HslColor
{
    public double H { get; }
    public double S { get; }
    public double L { get; }
    public double A { get; }

    private HslColor(double h, double s, double l, double a = 1.0)
    {
        H = h;
        S = s;
        L = l;
        A = a;
    }

    public static HslColor FromHex(string hex)
    {
        var rgb = HexToRgb(hex);
        return FromRgb(rgb.r, rgb.g, rgb.b);
    }

    public static HslColor FromRgb(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2.0;

        if (Math.Abs(max - min) < double.Epsilon)
        {
            return new HslColor(0, 0, l * 100);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

        double h;
        if (Math.Abs(max - r) < double.Epsilon)
            h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < double.Epsilon)
            h = (b - r) / d + 2;
        else
            h = (r - g) / d + 4;

        h /= 6.0;

        return new HslColor(h * 360, s * 100, l * 100);
    }

    public HslColor AdjustLightness(double newL) => new(H, S, Math.Clamp(newL, 0, 100), A);

    public HslColor AdjustSaturation(double newS) => new(H, Math.Clamp(newS, 0, 100), L, A);

    public HslColor WithAlpha(double alpha) => new(H, S, L, alpha);

    public string ToHex() => ToHex(isDark: false);

    public string ToHex(bool isDark)
    {
        var (r, g, b) = ToRgb();
        if (A < 1.0)
        {
            return $"rgba({(int)Math.Round(r * 255)},{(int)Math.Round(g * 255)},{(int)Math.Round(b * 255)},{A:F2})";
        }
        return $"#{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
    }

    public (double r, double g, double b) ToRgb()
    {
        var h = H / 360.0;
        var s = S / 100.0;
        var l = L / 100.0;

        if (s < double.Epsilon)
        {
            return (l, l, l);
        }

        double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6;
            return p;
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        return (HueToRgb(p, q, h + 1.0 / 3.0), HueToRgb(p, q, h), HueToRgb(p, q, h - 1.0 / 3.0));
    }

    /// <summary>
    /// Returns black or white depending on which has better contrast against this color (WCAG-style).
    /// </summary>
    public string ContrastTextColor()
    {
        var (r, g, b) = ToRgb();
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        return luminance > 0.4 ? "#0F172A" : "#FFFFFF";
    }

    private static (double r, double g, double b) HexToRgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 3)
        {
            hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";
        }

        var r = int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0;
        var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255.0;
        var b = int.Parse(hex[4..6], NumberStyles.HexNumber) / 255.0;

        return (r, g, b);
    }
}
