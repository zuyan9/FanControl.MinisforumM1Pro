namespace FanControl.MinisforumM1Pro;

internal interface IM1Backend : IDisposable
{
    string? StartupSummary { get; }

    void Initialize();

    M1Telemetry ReadTelemetry();

    void Set(M1Fan fan, byte code);

    void Reset(M1Fan fan);
}
