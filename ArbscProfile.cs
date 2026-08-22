namespace FanControl.MinisforumM1Pro;

internal enum M1Fan
{
    Cpu,
    System,
}

internal static class M1EcLayout
{
    internal const string IsaMutexName = "Global\\Access_ISABUS.HTP.Method";
    internal const string LpcResourceName =
        "LibreHardwareMonitor.Resources.PawnIo.LpcIO.bin";
    internal const ushort ModeAddress = 0x032e;
    internal const ushort CurveBlockStart = 0x0640;
    internal const int CurveBlockLength = 0x40;

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
    private static readonly ushort[] CpuOwned = [.. CpuBases, .. CpuSlopes];
    private static readonly ushort[] SystemOwned = [.. SystemBases, .. SystemSlopes];
    private static readonly ushort[] Owned = [.. CpuOwned, .. SystemOwned];
    private static readonly ushort[] AllRestoreOrder =
        [.. CpuBases, .. SystemBases, .. CpuSlopes, .. SystemSlopes];
    private static readonly ushort[] CurveBlock = Enumerable.Range(
            CurveBlockStart,
            CurveBlockLength)
        .Select(address => (ushort)address)
        .ToArray();
    private static readonly ushort[] Telemetry =
        [0x0438, 0x0439, 0x043a, 0x043b, 0x0428, 0x0429];
    private static readonly ushort[] RuntimeSample =
        [0x0420, 0x0421, 0x0422, 0x0423, 0x0428, 0x0429, 0x0432, 0x0433];
    private static readonly ushort[] NativeIdentity = [0x2000, 0x2001, 0x2002];

    internal static ushort[] OwnedAddresses => (ushort[])Owned.Clone();

    internal static ushort[] CurveBlockAddresses => (ushort[])CurveBlock.Clone();

    internal static ushort[] TelemetryAddresses => (ushort[])Telemetry.Clone();

    internal static ushort[] RuntimeSampleAddresses =>
        (ushort[])RuntimeSample.Clone();

    internal static ushort[] NativeIdentityAddresses =>
        (ushort[])NativeIdentity.Clone();

    internal static EcWrite[] ManualWrites(M1Fan fan, byte code)
    {
        (ushort[] bases, ushort[] slopes) = Layout(fan);
        return slopes.Select(address => new EcWrite(address, 0))
            .Concat(bases.Select(address => new EcWrite(address, code)))
            .ToArray();
    }

    internal static byte[] CaptureOwned(ReadOnlySpan<byte> curve)
    {
        AssertCurveLength(curve);
        byte[] captured = new byte[Owned.Length];
        for (int index = 0; index < Owned.Length; index++)
        {
            captured[index] = curve[Owned[index] - CurveBlockStart];
        }
        return captured;
    }

    internal static EcWrite[] RestoreWrites(M1Fan fan, ReadOnlySpan<byte> baseline)
    {
        AssertBaselineLength(baseline);
        ushort[] addresses = fan switch
        {
            M1Fan.Cpu => CpuOwned,
            M1Fan.System => SystemOwned,
            _ => throw new ArgumentOutOfRangeException(nameof(fan)),
        };
        byte[] values = baseline.ToArray();
        return addresses.Select(address =>
            new EcWrite(address, BaselineValue(address, values))).ToArray();
    }

    internal static EcWrite[] AllRestoreWrites(ReadOnlySpan<byte> baseline)
    {
        AssertBaselineLength(baseline);
        byte[] values = baseline.ToArray();
        return AllRestoreOrder.Select(address =>
            new EcWrite(address, BaselineValue(address, values))).ToArray();
    }

    internal static byte[] Apply(
        ReadOnlySpan<byte> curve,
        IEnumerable<EcWrite> writes)
    {
        AssertCurveLength(curve);
        byte[] result = curve.ToArray();
        foreach (EcWrite write in writes)
        {
            int offset = write.Address - CurveBlockStart;
            if ((uint)offset >= CurveBlockLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(writes),
                    $"EC write 0x{write.Address:x4} is outside the curve block.");
            }
            result[offset] = write.Value;
        }
        return result;
    }

    private static byte BaselineValue(ushort address, ReadOnlySpan<byte> baseline)
    {
        int index = Array.IndexOf(Owned, address);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"EC address 0x{address:x4} is not plugin-owned.");
        }
        return baseline[index];
    }

    private static void AssertCurveLength(ReadOnlySpan<byte> curve)
    {
        if (curve.Length != CurveBlockLength)
        {
            throw new ArgumentException(
                $"Expected a {CurveBlockLength}-byte EC curve block.",
                nameof(curve));
        }
    }

    private static void AssertBaselineLength(ReadOnlySpan<byte> baseline)
    {
        if (baseline.Length != Owned.Length)
        {
            throw new ArgumentException(
                $"Expected a {Owned.Length}-byte EC ownership baseline.",
                nameof(baseline));
        }
    }

    private static (ushort[] Bases, ushort[] Slopes) Layout(M1Fan fan) => fan switch
    {
        M1Fan.Cpu => (CpuBases, CpuSlopes),
        M1Fan.System => (SystemBases, SystemSlopes),
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };
}
