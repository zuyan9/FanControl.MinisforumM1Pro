using FanControl.Plugins;

namespace FanControl.MinisforumM1Pro;

/// <summary>Exposes ARBSC fan telemetry and controls to FanControl.</summary>
public sealed class M1ProPlugin : IPlugin2
{
    private readonly Func<IArbscBackend> backendFactory;
    private readonly IPluginLogger? logger;
    private readonly Sensor cpuFan = new(
        "minisforum.m1pro.arbsc.fan1",
        "M1 Pro CPU Fan");
    private readonly Sensor systemFan = new(
        "minisforum.m1pro.arbsc.fan2",
        "M1 Pro System Fan");
    private readonly Sensor cpuTemperature = new(
        "minisforum.m1pro.arbsc.cpu-temperature",
        "M1 Pro EC CPU Temperature");
    private readonly Sensor systemTemperature = new(
        "minisforum.m1pro.arbsc.system-temperature",
        "M1 Pro EC System Temperature");
    private readonly ControlSensor cpuControl;
    private readonly ControlSensor systemControl;
    private IArbscBackend? backend;

    /// <summary>Creates the plugin with the native PawnIO backend.</summary>
    public M1ProPlugin()
        : this(static () => new PawnIoArbscBackend(), null)
    {
    }

    /// <summary>Creates the plugin with FanControl logging.</summary>
    public M1ProPlugin(IPluginLogger logger)
        : this(static () => new PawnIoArbscBackend(), logger)
    {
    }

    internal M1ProPlugin(
        Func<IArbscBackend> backendFactory,
        IPluginLogger? logger = null)
    {
        this.backendFactory = backendFactory;
        this.logger = logger;
        cpuControl = new ControlSensor(
            "minisforum.m1pro.arbsc.cpu-control",
            "M1 Pro CPU Fan Control",
            $"{Name}/{cpuFan.Id}",
            value => Set(ArbscFan.Cpu, value),
            () => Reset(ArbscFan.Cpu));
        systemControl = new ControlSensor(
            "minisforum.m1pro.arbsc.system-control",
            "M1 Pro System Fan Control",
            $"{Name}/{systemFan.Id}",
            value => Set(ArbscFan.System, value),
            () => Reset(ArbscFan.System));
    }

    /// <inheritdoc />
    public string Name => "Minisforum M1 Pro (ARBSC)";

    /// <inheritdoc />
    public void Initialize()
    {
        Close();
        IArbscBackend candidate = backendFactory();
        try
        {
            candidate.Initialize();
            Apply(candidate.ReadTelemetry());
            backend = candidate;
            Log("Minisforum M1 Pro lean ARBSC backend initialized.");
        }
        catch
        {
            candidate.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Load(IPluginSensorsContainer container)
    {
        container.FanSensors.AddRange([cpuFan, systemFan]);
        container.TempSensors.AddRange([cpuTemperature, systemTemperature]);
        container.ControlSensors.AddRange([cpuControl, systemControl]);
        Log("Minisforum M1 Pro loaded paired CPU and system fan controls.");
    }

    /// <inheritdoc />
    public void Update()
    {
        try
        {
            IArbscBackend? active = Volatile.Read(ref backend);
            if (active is not null)
            {
                Apply(active.ReadTelemetry());
            }
        }
        catch (Exception exception)
        {
            Log($"Minisforum M1 Pro telemetry read failed: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        cpuControl.Clear();
        systemControl.Clear();
        IArbscBackend? old = Interlocked.Exchange(ref backend, null);
        if (old is null)
        {
            return;
        }

        try
        {
            old.Dispose();
        }
        catch (Exception exception)
        {
            Log($"Minisforum M1 Pro baseline restoration failed: {exception.Message}");
        }
    }

    private byte Set(ArbscFan fan, float percentage)
    {
        byte requestedCode = ArbscProfile.ToCode(percentage);
        (Volatile.Read(ref backend) ??
            throw new InvalidOperationException("The ARBSC backend is unavailable."))
            .Set(fan, ArbscProfile.NormalizeCode(requestedCode));
        return requestedCode;
    }

    private void Reset(ArbscFan fan) => Volatile.Read(ref backend)?.Reset(fan);

    private void Apply(ArbscTelemetry telemetry)
    {
        cpuFan.Value = telemetry.Fan1Rpm;
        systemFan.Value = telemetry.Fan2Rpm;
        cpuTemperature.Value = telemetry.CpuTemperatureC;
        systemTemperature.Value = telemetry.SystemTemperatureC;
    }

    private void Log(string message)
    {
        try
        {
            logger?.Log(message);
        }
        catch
        {
        }
    }

    private sealed class Sensor(string id, string name) : IPluginSensor
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public float? Value { get; internal set; }

        public void Update()
        {
        }
    }

    private sealed class ControlSensor(
        string id,
        string name,
        string pairedFanSensorId,
        Func<float, byte> set,
        Action reset) : IPluginControlSensor2
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string PairedFanSensorId { get; } = pairedFanSensorId;

        public float? Value { get; private set; }

        public void Set(float value)
        {
            Value = ArbscProfile.ToPercentage(set(value));
        }

        public void Reset()
        {
            reset();
            Value = null;
        }

        public void Update()
        {
        }

        internal void Clear() => Value = null;
    }
}
