namespace FanControl.MinisforumM1Pro;

internal sealed record M1Telemetry(
    int CpuFanRpm,
    int SystemFanRpm,
    int CpuTemperatureC,
    int SystemTemperatureC);

internal static class M1TelemetryDecoder
{
    internal static bool TryDecodeRpm(ReadOnlySpan<byte> highLowHigh, out int rpm)
    {
        if (highLowHigh.Length != 3)
        {
            throw new ArgumentException(
                "An M1 EC RPM sample must contain high/low/high bytes.",
                nameof(highLowHigh));
        }
        if (highLowHigh[0] != highLowHigh[2])
        {
            rpm = 0;
            return false;
        }

        rpm = (highLowHigh[0] << 8) | highLowHigh[1];
        return true;
    }

    internal static M1Telemetry Decode(ReadOnlySpan<byte> values)
    {
        if (values.Length != M1EcLayout.TelemetryAddresses.Length)
        {
            throw new ArgumentException("Unexpected M1 EC telemetry length.", nameof(values));
        }
        if (!TryDecodeRpm(values[..3], out int cpuRpm) ||
            !TryDecodeRpm(values.Slice(3, 3), out int systemRpm))
        {
            throw new IOException("M1 EC RPM mirror changed during sample read.");
        }

        return new M1Telemetry(
            cpuRpm,
            systemRpm,
            values[6],
            values[7]);
    }
}
