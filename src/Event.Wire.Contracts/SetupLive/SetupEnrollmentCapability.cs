namespace ISLAMU.Wire.Contracts.SetupLive;

using System.Diagnostics;

[DebuggerDisplay("SetupEnrollmentCapability(<redacted>)")]
public sealed class SetupEnrollmentCapability
{
    public const int ByteLength = 32;
    public const int EncodedLength = 43;

    private SetupEnrollmentCapability(string value)
    {
        EncodedValue = value;
    }

    internal string EncodedValue { get; }

    public static SetupEnrollmentCapability FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
        {
            throw new ArgumentException(
                "Setup enrollment capability material must have the required length.",
                nameof(bytes));
        }

        return new SetupEnrollmentCapability(SetupLiveBase64Url32.Encode(bytes));
    }

    public static bool TryCreate(
        string? candidate,
        out SetupEnrollmentCapability? capability)
    {
        capability = null;
        if (!SetupLiveBase64Url32.IsCanonical(candidate))
            return false;

        capability = new SetupEnrollmentCapability(candidate!);
        return true;
    }

    public string ToHeaderValue() => EncodedValue;

    public override string ToString() =>
        "SetupEnrollmentCapability(<redacted>)";

}
