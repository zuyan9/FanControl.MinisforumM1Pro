namespace FanControl.MinisforumM1Pro;

internal sealed record ArbscTelemetry(
    int Fan1Rpm,
    int Fan2Rpm,
    int CpuTemperatureC,
    int SystemTemperatureC);

internal static class ArbscTelemetryDecoder
{
    internal static ArbscTelemetry Decode(ReadOnlySpan<byte> values)
    {
        if (values.Length != ArbscProfile.TelemetryAddresses.Length)
        {
            throw new ArgumentException("Unexpected ARBSC telemetry length.", nameof(values));
        }

        return new ArbscTelemetry(
            (values[0] << 8) | values[1],
            (values[2] << 8) | values[3],
            values[4],
            values[5]);
    }
}
