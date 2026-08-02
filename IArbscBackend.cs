namespace FanControl.MinisforumM1Pro;

internal interface IArbscBackend : IDisposable
{
    void Initialize();

    ArbscTelemetry ReadTelemetry();

    void Set(ArbscFan fan, byte code);

    void Reset(ArbscFan fan);
}
