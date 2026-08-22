namespace FanControl.MinisforumM1Pro;

internal sealed record M1Telemetry(
    int CpuFanRpm,
    int SystemFanRpm,
    int CpuTemperatureC,
    int SystemTemperatureC);

internal static class M1TelemetryDecoder
{
    internal static M1Telemetry Decode(ReadOnlySpan<byte> values)
    {
        if (values.Length != M1EcLayout.TelemetryAddresses.Length)
        {
            throw new ArgumentException("Unexpected M1 EC telemetry length.", nameof(values));
        }

        return new M1Telemetry(
            (values[0] << 8) | values[1],
            (values[2] << 8) | values[3],
            values[4],
            values[5]);
    }
}
