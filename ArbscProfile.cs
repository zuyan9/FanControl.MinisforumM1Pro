namespace FanControl.MinisforumM1Pro;

internal enum ArbscFan
{
    Cpu,
    System,
}

internal static class ArbscProfile
{
    internal const string Board = "ARBSC";
    internal const string IsaMutexName = "Global\\Access_ISABUS.HTP.Method";
    internal const string LpcResourceName =
        "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
    internal const byte MaximumCode = 51;
    internal const byte MinimumStableCode = 7;

    internal static readonly ushort[] ControllerProfileAddresses =
        [0x2000, 0x2001, 0x2002, 0x200d];
    internal static readonly byte[] ExpectedControllerProfile =
        [0x55, 0x71, 0x07, 0xcb];
    internal static readonly ushort[] TelemetryAddresses =
        [0x0438, 0x0439, 0x043a, 0x043b, 0x0428, 0x0429];

    private static readonly ushort[] CpuBases = Enumerable.Range(0, 8)
        .Select(row => (ushort)(0x0640 + (row * 3)))
        .ToArray();
    private static readonly ushort[] CpuSlopes = Enumerable.Range(0x0670, 8)
        .Select(address => (ushort)address)
        .ToArray();
    private static readonly ushort[] SystemBases = Enumerable.Range(0, 7)
        .Select(row => (ushort)(0x0658 + (row * 3)))
        .ToArray();
    private static readonly ushort[] SystemSlopes = Enumerable.Range(0x0678, 7)
        .Select(address => (ushort)address)
        .ToArray();
    private static readonly ushort[] CpuRestoreAddresses =
        [.. CpuBases, .. CpuSlopes];
    private static readonly ushort[] SystemRestoreAddresses =
        [.. SystemBases, .. SystemSlopes];

    internal static readonly ushort[] OwnedAddresses =
        [.. CpuRestoreAddresses, .. SystemRestoreAddresses];

    internal static byte ToCode(float percentage) => (byte)Math.Round(
        percentage * MaximumCode / 100d,
        MidpointRounding.AwayFromZero);

    internal static byte NormalizeCode(byte code) =>
        code is > 0 and < MinimumStableCode ? (byte)0 : code;

    internal static float ToPercentage(byte code) => code * 100f / MaximumCode;

    internal static EcWrite[] ManualWrites(ArbscFan fan, byte code)
    {
        (ushort[] bases, ushort[] slopes) = Layout(fan);
        return slopes.Select(address => new EcWrite(address, 0))
            .Concat(bases.Select(address => new EcWrite(address, code)))
            .ToArray();
    }

    internal static EcWrite[] RestoreWrites(ArbscFan fan, byte[] baseline)
    {
        (ushort[] addresses, int start) = fan switch
        {
            ArbscFan.Cpu => (CpuRestoreAddresses, 0),
            ArbscFan.System => (SystemRestoreAddresses, CpuRestoreAddresses.Length),
            _ => throw new ArgumentOutOfRangeException(nameof(fan)),
        };
        return addresses.Select((address, index) =>
            new EcWrite(address, baseline[start + index])).ToArray();
    }

    internal static EcWrite[] AllRestoreWrites(byte[] baseline) =>
        OwnedAddresses.Select((address, index) =>
            new EcWrite(address, baseline[index])).ToArray();

    private static (ushort[] Bases, ushort[] Slopes) Layout(ArbscFan fan) => fan switch
    {
        ArbscFan.Cpu => (CpuBases, CpuSlopes),
        ArbscFan.System => (SystemBases, SystemSlopes),
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };
}
