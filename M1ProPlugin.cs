using FanControl.Plugins;

namespace FanControl.MinisforumM1Pro;

/// <summary>Exposes supported Minisforum M1 fan telemetry and controls.</summary>
public sealed class M1ProPlugin : IPlugin2
{
    private readonly M1ModelProfile profile;
    private readonly Func<string> boardReader;
    private readonly Func<M1ModelProfile, IM1Backend> backendFactory;
    private readonly IPluginLogger? logger;
    private readonly Sensor cpuFan;
    private readonly Sensor systemFan;
    private readonly Sensor cpuTemperature;
    private readonly Sensor systemTemperature;
    private readonly ControlSensor cpuControl;
    private readonly ControlSensor systemControl;
    private IM1Backend? backend;
    private bool recoveryFailed;

    /// <summary>Creates the plugin with the native PawnIO backend.</summary>
    public M1ProPlugin()
        : this(DetectProfile(), null)
    {
    }

    /// <summary>Creates the plugin with FanControl logging.</summary>
    public M1ProPlugin(IPluginLogger logger)
        : this(DetectProfile(), logger)
    {
    }

    private M1ProPlugin(M1ModelProfile profile, IPluginLogger? logger)
        : this(
            profile,
            HostIdentity.ReadBoard,
            static selected => new PawnIoM1Backend(selected),
            logger)
    {
    }

    internal M1ProPlugin(
        M1ModelProfile profile,
        Func<string> boardReader,
        Func<M1ModelProfile, IM1Backend> backendFactory,
        IPluginLogger? logger = null)
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.boardReader = boardReader ?? throw new ArgumentNullException(nameof(boardReader));
        this.backendFactory = backendFactory ??
            throw new ArgumentNullException(nameof(backendFactory));
        this.logger = logger;

        string prefix = profile.SensorIdPrefix;
        cpuFan = new Sensor($"{prefix}.fan1", $"{profile.ModelName} CPU Fan");
        systemFan = new Sensor($"{prefix}.fan2", $"{profile.ModelName} System Fan");
        cpuTemperature = new Sensor(
            $"{prefix}.cpu-temperature",
            $"{profile.ModelName} EC CPU");
        systemTemperature = new Sensor(
            $"{prefix}.system-temperature",
            $"{profile.ModelName} EC System");
        cpuControl = new ControlSensor(
            $"{prefix}.cpu-control",
            $"{profile.ModelName} CPU Fan Control",
            $"{Name}/{cpuFan.Id}",
            value => Set(M1Fan.Cpu, value),
            () => Reset(M1Fan.Cpu));
        systemControl = new ControlSensor(
            $"{prefix}.system-control",
            $"{profile.ModelName} System Fan Control",
            $"{Name}/{systemFan.Id}",
            value => Set(M1Fan.System, value),
            () => Reset(M1Fan.System));
    }

    /// <inheritdoc />
    public string Name => profile.PluginName;

    /// <inheritdoc />
    public void Initialize()
    {
        Close();
        if (recoveryFailed)
        {
            throw new InvalidOperationException(
                $"{profile.ModelName} baseline restoration previously failed. " +
                "Restart Windows before reinitializing the plugin.");
        }
        HostIdentityGate.AssertBoard(profile, boardReader());
        IM1Backend candidate = backendFactory(profile);
        try
        {
            candidate.Initialize();
            Apply(candidate.ReadTelemetry());
            backend = candidate;
            Log($"{profile.ModelName} {profile.Board} backend initialized.");
            if (!profile.HardwareValidated)
            {
                Log(
                    $"{profile.ModelName} support is analysis-derived and experimental; " +
                    "its transport and physical control range have not been validated on hardware.");
            }
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
        Log($"{profile.ModelName} loaded paired CPU and system fan controls.");
    }

    /// <inheritdoc />
    public void Update()
    {
        try
        {
            IM1Backend? active = Volatile.Read(ref backend);
            if (active is not null)
            {
                Apply(active.ReadTelemetry());
            }
        }
        catch (Exception exception)
        {
            ClearTelemetry();
            Log($"{profile.ModelName} telemetry read failed: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public void Close()
    {
        cpuControl.Clear();
        systemControl.Clear();
        IM1Backend? old = Interlocked.Exchange(ref backend, null);
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
            recoveryFailed = true;
            Log($"{profile.ModelName} baseline restoration failed: {exception.Message}");
            Log($"Restart Windows before reinitializing {profile.ModelName}.");
        }
    }

    private float Set(M1Fan fan, float percentage)
    {
        FanControlRequest request = profile.Policy(fan).Resolve(percentage);
        try
        {
            (Volatile.Read(ref backend) ??
                throw new InvalidOperationException(
                    $"The {profile.ModelName} backend is unavailable."))
                .Set(fan, request.AppliedCode);
        }
        catch
        {
            cpuControl.Clear();
            systemControl.Clear();
            throw;
        }
        return request.ReportedPercentage;
    }

    private void Reset(M1Fan fan)
    {
        try
        {
            Volatile.Read(ref backend)?.Reset(fan);
        }
        catch
        {
            cpuControl.Clear();
            systemControl.Clear();
            throw;
        }
    }

    private void Apply(M1Telemetry telemetry)
    {
        cpuFan.Value = telemetry.CpuFanRpm;
        systemFan.Value = telemetry.SystemFanRpm;
        cpuTemperature.Value = telemetry.CpuTemperatureC;
        systemTemperature.Value = telemetry.SystemTemperatureC;
    }

    private void ClearTelemetry()
    {
        cpuFan.Value = null;
        systemFan.Value = null;
        cpuTemperature.Value = null;
        systemTemperature.Value = null;
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

    private static M1ModelProfile DetectProfile()
    {
        try
        {
            return M1ModelProfiles.TryResolveExact(HostIdentity.ReadBoard()) ??
                M1ModelProfiles.M1Pro;
        }
        catch
        {
            // Preserve the legacy plugin identity until Initialize can report the host gate.
            return M1ModelProfiles.M1Pro;
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
        Func<float, float> set,
        Action reset) : IPluginControlSensor2
    {
        public string Id { get; } = id;

        public string Name { get; } = name;

        public string PairedFanSensorId { get; } = pairedFanSensorId;

        public float? Value { get; private set; }

        public void Set(float value)
        {
            Value = set(value);
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
