namespace FanControl.MinisforumM1Pro;

internal enum ArbscFan
{
    Cpu,
    System,
}

internal static class ArbscProfile
{
    internal const string Manufacturer = "Micro Computer (HK) Tech Limited";
    internal const string Product = "AI Series";
    internal const string Board = "ARBSC";
    internal const string IsaMutexName = "Global\\Access_ISABUS.HTP.Method";
    internal const string LpcResourceName =
        "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
    internal const ushort CurveStart = 0x0640;
    internal const byte MaximumCode = 51;
    internal const byte MinimumStableCode = 7;

    internal static readonly byte[] StockCurve =
    [
        0x00, 0x19, 0x00, 0x12, 0x2d, 0x19, 0x16, 0x36,
        0x2d, 0x1c, 0x42, 0x36, 0x21, 0x50, 0x42, 0x25,
        0x5c, 0x50, 0x28, 0x60, 0x5c, 0x33, 0x64, 0x60,
        0x0a, 0x19, 0x00, 0x11, 0x3c, 0x19, 0x17, 0x5b,
        0x3c, 0x29, 0x64, 0x5a, 0x29, 0x64, 0x5a, 0x29,
        0x64, 0x5a, 0x29, 0x64, 0x5a, 0x00, 0x00, 0x00,
        0x00, 0x14, 0x42, 0x29, 0x1c, 0x19, 0x13, 0x00,
        0x00, 0x11, 0x3a, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    internal static readonly ushort[] ChipAddresses =
        [0x2000, 0x2001, 0x2002, 0x200d];
    internal static readonly ushort[] TelemetryAddresses =
        [0x0438, 0x0439, 0x043a, 0x043b, 0x0428, 0x0429];
    internal static readonly ushort[] CurveAddresses = Enumerable
        .Range(CurveStart, StockCurve.Length)
        .Select(value => (ushort)value)
        .ToArray();

    private static readonly int[] CpuBases =
        Enumerable.Range(0, 8).Select(row => row * 3).ToArray();
    private static readonly int[] CpuSlopes = Enumerable.Range(0x30, 8).ToArray();
    private static readonly int[] SystemBases =
        Enumerable.Range(0, 7).Select(row => 0x18 + (row * 3)).ToArray();
    private static readonly int[] SystemSlopes = Enumerable.Range(0x38, 7).ToArray();

    internal static byte ToCode(float percentage) => (byte)Math.Round(
        percentage * MaximumCode / 100d,
        MidpointRounding.AwayFromZero);

    internal static byte NormalizeCode(byte code) =>
        code is > 0 and < MinimumStableCode ? (byte)0 : code;

    internal static float ToPercentage(byte code) => code * 100f / MaximumCode;

    internal static EcWrite[] ManualWrites(ArbscFan fan, byte code)
    {
        (int[] bases, int[] slopes) = Layout(fan);
        return slopes.Select(offset => Write(offset, 0))
            .Concat(bases.Select(offset => Write(offset, code)))
            .ToArray();
    }

    internal static EcWrite[] StockWrites(ArbscFan fan)
    {
        (int[] bases, int[] slopes) = Layout(fan);
        return bases.Concat(slopes)
            .Select(offset => Write(offset, StockCurve[offset]))
            .ToArray();
    }

    internal static EcWrite[] AllStockWrites() =>
        StockWrites(ArbscFan.Cpu).Concat(StockWrites(ArbscFan.System)).ToArray();

    private static (int[] Bases, int[] Slopes) Layout(ArbscFan fan) => fan switch
    {
        ArbscFan.Cpu => (CpuBases, CpuSlopes),
        ArbscFan.System => (SystemBases, SystemSlopes),
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    private static EcWrite Write(int offset, byte value) =>
        new((ushort)(CurveStart + offset), value);
}
