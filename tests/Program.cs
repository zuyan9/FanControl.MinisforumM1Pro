using FanControl.Plugins;

namespace FanControl.MinisforumM1Pro.Tests;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Body)[] tests =
        [
            ("profile percentage mapping", ProfilePercentageMapping),
            ("profile deadzone normalization", ProfileDeadzoneNormalization),
            ("profile write and restore order", ProfileWriteOrder),
            ("telemetry decoding", TelemetryDecoding),
            ("board identity gate", BoardIdentityGateTest),
            ("backend initialization gates", BackendInitializationGates),
            ("backend transactions", BackendTransactions),
            ("backend failure restoration", BackendFailureRestoration),
            ("plugin lifecycle and IDs", PluginLifecycleAndIds),
            ("plugin deadzone controls", PluginDeadzoneControls),
            ("plugin update error retention", PluginUpdateErrorRetention),
            ("plugin initialization failure", PluginInitializationFailure),
        ];

        int failures = 0;
        foreach ((string name, Action body) in tests)
        {
            try
            {
                body();
                Console.WriteLine($"PASS {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {name}: {exception}");
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void ProfilePercentageMapping()
    {
        Equal((byte)0, ArbscProfile.ToCode(0f));
        Equal((byte)1, ArbscProfile.ToCode(1f));
        Equal((byte)13, ArbscProfile.ToCode(25f));
        Equal((byte)26, ArbscProfile.ToCode(50f));
        Equal((byte)38, ArbscProfile.ToCode(75f));
        Equal((byte)51, ArbscProfile.ToCode(100f));
        Equal(0f, ArbscProfile.ToPercentage(0));
        Equal(26 * 100f / 51f, ArbscProfile.ToPercentage(26));
        Equal(100f, ArbscProfile.ToPercentage(51));
    }

    private static void ProfileDeadzoneNormalization()
    {
        Equal((byte)0, ArbscProfile.NormalizeCode(0));
        Equal((byte)0, ArbscProfile.NormalizeCode(1));
        Equal((byte)0, ArbscProfile.NormalizeCode(5));
        Equal((byte)0, ArbscProfile.NormalizeCode(6));
        Equal((byte)7, ArbscProfile.NormalizeCode(7));
        Equal((byte)8, ArbscProfile.NormalizeCode(8));
        Equal((byte)51, ArbscProfile.NormalizeCode(51));

        Equal((byte)5, ArbscProfile.ToCode(10f));
        Equal((byte)0, ArbscProfile.NormalizeCode(ArbscProfile.ToCode(10f)));
        Equal((byte)6, ArbscProfile.ToCode(12f));
        Equal((byte)0, ArbscProfile.NormalizeCode(ArbscProfile.ToCode(12f)));
        Equal((byte)7, ArbscProfile.ToCode(13f));
        Equal((byte)7, ArbscProfile.NormalizeCode(ArbscProfile.ToCode(13f)));
    }

    private static void ProfileWriteOrder()
    {
        byte[] baseline = ArbitraryBaseline();
        EcWrite[] expectedCpuManual = Enumerable.Range(0x0670, 8)
            .Select(address => new EcWrite((ushort)address, 0))
            .Concat(Enumerable.Range(0, 8).Select(row =>
                new EcWrite((ushort)(0x0640 + (row * 3)), 17)))
            .ToArray();
        SequenceEqual(expectedCpuManual, ArbscProfile.ManualWrites(ArbscFan.Cpu, 17));

        EcWrite[] expectedSystemManual = Enumerable.Range(0x0678, 7)
            .Select(address => new EcWrite((ushort)address, 0))
            .Concat(Enumerable.Range(0, 7).Select(row =>
                new EcWrite((ushort)(0x0658 + (row * 3)), 44)))
            .ToArray();
        SequenceEqual(
            expectedSystemManual,
            ArbscProfile.ManualWrites(ArbscFan.System, 44));

        ushort[] expectedCpuRestoreAddresses = Enumerable.Range(0, 8)
            .Select(row => (ushort)(0x0640 + (row * 3)))
            .Concat(Enumerable.Range(0x0670, 8).Select(address => (ushort)address))
            .ToArray();
        SequenceEqual(
            expectedCpuRestoreAddresses,
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, baseline)
                .Select(write => write.Address));

        ushort[] expectedSystemRestoreAddresses = Enumerable.Range(0, 7)
            .Select(row => (ushort)(0x0658 + (row * 3)))
            .Concat(Enumerable.Range(0x0678, 7).Select(address => (ushort)address))
            .ToArray();
        SequenceEqual(
            expectedSystemRestoreAddresses,
            ArbscProfile.RestoreWrites(ArbscFan.System, baseline)
                .Select(write => write.Address));

        EcWrite[] allRestore = ArbscProfile.AllRestoreWrites(baseline);
        Equal(30, allRestore.Length);
        SequenceEqual(
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, baseline),
            allRestore.Take(16));
        SequenceEqual(
            ArbscProfile.RestoreWrites(ArbscFan.System, baseline),
            allRestore.Skip(16));
        SequenceEqual(ArbscProfile.OwnedAddresses, allRestore.Select(write => write.Address));
        SequenceEqual(baseline, allRestore.Select(write => write.Value));
    }

    private static void TelemetryDecoding()
    {
        ArbscTelemetry telemetry = ArbscTelemetryDecoder.Decode(
            [0x0b, 0xb8, 0x07, 0x6c, 63, 42]);
        Equal(3_000, telemetry.Fan1Rpm);
        Equal(1_900, telemetry.Fan2Rpm);
        Equal(63, telemetry.CpuTemperatureC);
        Equal(42, telemetry.SystemTemperatureC);

        ArbscTelemetry extremes = ArbscTelemetryDecoder.Decode(
            [0x00, 0x00, 0xff, 0xff, 0, 255]);
        Equal(0, extremes.Fan1Rpm);
        Equal(65_535, extremes.Fan2Rpm);
        Equal(0, extremes.CpuTemperatureC);
        Equal(255, extremes.SystemTemperatureC);

        Throws<ArgumentException>(() => ArbscTelemetryDecoder.Decode([0, 1, 2]));
    }

    private static void BoardIdentityGateTest()
    {
        HostIdentityGate.AssertBoard(ArbscProfile.Board);
        Throws<PlatformNotSupportedException>(() => HostIdentityGate.AssertBoard("Other"));
    }

    private static void BackendInitializationGates()
    {
        FakeTransport good = new();
        PawnIoArbscBackend backend = CreateBackend(good);
        backend.Initialize();
        backend.Initialize();
        SequenceEqual(ArbscProfile.ControllerProfileAddresses, good.ReadBatches[0]);
        SequenceEqual(ArbscProfile.OwnedAddresses, good.ReadBatches[1]);
        Equal(2, good.ReadBatches.Count);
        Equal(0, good.WriteBatches.Count);
        backend.Dispose();
        True(good.Disposed);

        FakeTransport wrongChip = new();
        wrongChip.SetByte(ArbscProfile.ControllerProfileAddresses[0], 0);
        PawnIoArbscBackend chipBackend = CreateBackend(wrongChip);
        Throws<PlatformNotSupportedException>(chipBackend.Initialize);
        True(wrongChip.Disposed);
        Equal(1, wrongChip.ReadBatches.Count);

        FakeTransport baselineReadFailure = new();
        baselineReadFailure.FailReadCalls.Add(2);
        PawnIoArbscBackend baselineBackend = CreateBackend(baselineReadFailure);
        Throws<IOException>(baselineBackend.Initialize);
        True(baselineReadFailure.Disposed);

        int transportFactoryCalls = 0;
        FakeTransport unused = new();
        PawnIoArbscBackend wrongHost = new(
            static () => "Other",
            () =>
            {
                transportFactoryCalls++;
                return unused;
            });
        Throws<PlatformNotSupportedException>(wrongHost.Initialize);
        Equal(0, transportFactoryCalls);
        False(unused.Disposed);
    }

    private static void BackendTransactions()
    {
        FakeTransport transport = new();
        PawnIoArbscBackend backend = CreateBackend(transport);
        backend.Initialize();

        backend.Set(ArbscFan.Cpu, 13);
        Equal<byte?>((byte)13, backend.CodeFor(ArbscFan.Cpu));
        SequenceEqual(
            ArbscProfile.ManualWrites(ArbscFan.Cpu, 13),
            transport.WriteBatches[0]);
        AssertMemory(transport, ArbscProfile.ManualWrites(ArbscFan.Cpu, 13));

        backend.Set(ArbscFan.Cpu, 13);
        Equal(1, transport.WriteBatches.Count);

        backend.Set(ArbscFan.System, 51);
        Equal<byte?>((byte)51, backend.CodeFor(ArbscFan.System));
        SequenceEqual(
            ArbscProfile.ManualWrites(ArbscFan.System, 51),
            transport.WriteBatches[1]);

        backend.Reset(ArbscFan.Cpu);
        SequenceEqual(
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, transport.InitialBaseline),
            transport.WriteBatches[2]);
        Equal<byte?>(null, backend.CodeFor(ArbscFan.Cpu));
        Equal<byte?>((byte)51, backend.CodeFor(ArbscFan.System));
        AssertMemory(
            transport,
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, transport.InitialBaseline));
        AssertMemory(transport, ArbscProfile.ManualWrites(ArbscFan.System, 51));

        backend.Reset(ArbscFan.Cpu);
        Equal(3, transport.WriteBatches.Count);

        ArbscTelemetry telemetry = backend.ReadTelemetry();
        Equal(3_000, telemetry.Fan1Rpm);
        Equal(1_900, telemetry.Fan2Rpm);
        SequenceEqual(
            ArbscProfile.TelemetryAddresses,
            transport.ReadBatches[^1]);

        backend.Dispose();
        SequenceEqual(
            ArbscProfile.AllRestoreWrites(transport.InitialBaseline),
            transport.WriteBatches[^1]);
        AssertMemory(transport, ArbscProfile.AllRestoreWrites(transport.InitialBaseline));
        Equal<byte?>(null, backend.CodeFor(ArbscFan.Cpu));
        Equal<byte?>(null, backend.CodeFor(ArbscFan.System));
        True(transport.Disposed);

        FakeTransport offTransport = new();
        PawnIoArbscBackend offBackend = CreateBackend(offTransport);
        offBackend.Initialize();
        offBackend.Set(ArbscFan.Cpu, 0);
        Equal<byte?>((byte)0, offBackend.CodeFor(ArbscFan.Cpu));
        SequenceEqual(
            ArbscProfile.ManualWrites(ArbscFan.Cpu, 0),
            offTransport.WriteBatches[0]);
        offBackend.Set(ArbscFan.Cpu, 0);
        Equal(1, offTransport.WriteBatches.Count);
        offBackend.Reset(ArbscFan.Cpu);
        SequenceEqual(
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, offTransport.InitialBaseline),
            offTransport.WriteBatches[1]);
        Equal<byte?>(null, offBackend.CodeFor(ArbscFan.Cpu));
        offBackend.Dispose();
    }

    private static void BackendFailureRestoration()
    {
        FakeTransport setFailure = new();
        PawnIoArbscBackend setBackend = CreateBackend(setFailure);
        setBackend.Initialize();
        setFailure.FailWriteCalls.Add(1);
        Throws<IOException>(() => setBackend.Set(ArbscFan.Cpu, 31));
        Equal(2, setFailure.WriteBatches.Count);
        SequenceEqual(
            ArbscProfile.ManualWrites(ArbscFan.Cpu, 31),
            setFailure.WriteBatches[0]);
        SequenceEqual(
            ArbscProfile.AllRestoreWrites(setFailure.InitialBaseline),
            setFailure.WriteBatches[1]);
        AssertMemory(
            setFailure,
            ArbscProfile.AllRestoreWrites(setFailure.InitialBaseline));
        Equal<byte?>(null, setBackend.CodeFor(ArbscFan.Cpu));
        Equal<byte?>(null, setBackend.CodeFor(ArbscFan.System));
        setBackend.Dispose();

        FakeTransport resetFailure = new();
        PawnIoArbscBackend resetBackend = CreateBackend(resetFailure);
        resetBackend.Initialize();
        resetBackend.Set(ArbscFan.Cpu, 9);
        resetBackend.Set(ArbscFan.System, 27);
        resetFailure.FailWriteCalls.Add(3);
        Throws<IOException>(() => resetBackend.Reset(ArbscFan.Cpu));
        Equal(4, resetFailure.WriteBatches.Count);
        SequenceEqual(
            ArbscProfile.RestoreWrites(ArbscFan.Cpu, resetFailure.InitialBaseline),
            resetFailure.WriteBatches[2]);
        SequenceEqual(
            ArbscProfile.AllRestoreWrites(resetFailure.InitialBaseline),
            resetFailure.WriteBatches[3]);
        AssertMemory(
            resetFailure,
            ArbscProfile.AllRestoreWrites(resetFailure.InitialBaseline));
        Equal<byte?>(null, resetBackend.CodeFor(ArbscFan.Cpu));
        Equal<byte?>(null, resetBackend.CodeFor(ArbscFan.System));
        resetBackend.Dispose();

        FakeTransport disposeFailure = new();
        PawnIoArbscBackend disposeBackend = CreateBackend(disposeFailure);
        disposeBackend.Initialize();
        disposeFailure.FailWriteCalls.Add(1);
        Throws<IOException>(disposeBackend.Dispose);
        True(disposeFailure.Disposed);
    }

    private static void PluginLifecycleAndIds()
    {
        FakeBackend backend = new(
            new ArbscTelemetry(3_000, 1_900, 62, 41),
            new ArbscTelemetry(3_100, 2_000, 63, 42));
        FakeLogger logger = new();
        M1ProPlugin plugin = new(() => backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            Equal("Minisforum M1 Pro (ARBSC)", plugin.Name);
            SequenceEqual(
                [
                    "minisforum.m1pro.arbsc.fan1",
                    "minisforum.m1pro.arbsc.fan2",
                ],
                container.FanSensors.Select(sensor => sensor.Id));
            SequenceEqual(
                [
                    "minisforum.m1pro.arbsc.cpu-temperature",
                    "minisforum.m1pro.arbsc.system-temperature",
                ],
                container.TempSensors.Select(sensor => sensor.Id));
            SequenceEqual(
                [
                    "minisforum.m1pro.arbsc.cpu-control",
                    "minisforum.m1pro.arbsc.system-control",
                ],
                container.ControlSensors.Select(sensor => sensor.Id));

            IPluginControlSensor2 cpu = FindControl(
                container.ControlSensors,
                "minisforum.m1pro.arbsc.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container.ControlSensors,
                "minisforum.m1pro.arbsc.system-control");
            Equal("M1 Pro CPU Fan Control", cpu.Name);
            Equal("M1 Pro System Fan Control", system.Name);
            Equal(
                $"{plugin.Name}/minisforum.m1pro.arbsc.fan1",
                cpu.PairedFanSensorId);
            Equal(
                $"{plugin.Name}/minisforum.m1pro.arbsc.fan2",
                system.PairedFanSensorId);
            Equal(3_000f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan1").Value);

            cpu.Set(50f);
            system.Set(100f);
            SequenceEqual(
                [(ArbscFan.Cpu, (byte)26), (ArbscFan.System, (byte)51)],
                backend.SetCalls);
            Equal<float?>(ArbscProfile.ToPercentage(26), cpu.Value);
            Equal<float?>(100f, system.Value);

            cpu.Reset();
            SequenceEqual([ArbscFan.Cpu], backend.ResetCalls);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(100f, system.Value);

            plugin.Update();
            Equal(3_100f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan1").Value);
            Equal(2_000f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan2").Value);
            Equal(63f, Find(
                container.TempSensors,
                "minisforum.m1pro.arbsc.cpu-temperature").Value);
            Equal(42f, Find(
                container.TempSensors,
                "minisforum.m1pro.arbsc.system-temperature").Value);

            plugin.Close();
            Equal(1, backend.DisposeCalls);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
            Equal(3_100f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan1").Value);
            plugin.Close();
            Equal(1, backend.DisposeCalls);
            True(logger.Messages.Any(message => message.Contains("initialized")));
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void PluginDeadzoneControls()
    {
        FakeBackend backend = new(new ArbscTelemetry(2_500, 1_800, 55, 38));
        M1ProPlugin plugin = new(() => backend);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            IPluginControlSensor2 cpu = FindControl(
                container.ControlSensors,
                "minisforum.m1pro.arbsc.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container.ControlSensors,
                "minisforum.m1pro.arbsc.system-control");

            cpu.Set(10f);
            system.Set(12f);
            SequenceEqual(
                [(ArbscFan.Cpu, (byte)0), (ArbscFan.System, (byte)0)],
                backend.SetCalls);
            Equal<float?>(
                ArbscProfile.ToPercentage(ArbscProfile.ToCode(10f)),
                cpu.Value);
            Equal<float?>(
                ArbscProfile.ToPercentage(ArbscProfile.ToCode(12f)),
                system.Value);
            True(cpu.Value > 0f);
            True(system.Value > 0f);

            cpu.Set(12f);
            SequenceEqual(
                [
                    (ArbscFan.Cpu, (byte)0),
                    (ArbscFan.System, (byte)0),
                    (ArbscFan.Cpu, (byte)0),
                ],
                backend.SetCalls);
            Equal<float?>(
                ArbscProfile.ToPercentage(ArbscProfile.ToCode(12f)),
                cpu.Value);

            cpu.Reset();
            system.Reset();
            SequenceEqual(
                [ArbscFan.Cpu, ArbscFan.System],
                backend.ResetCalls);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);

            cpu.Set(13f);
            system.Set(13f);
            SequenceEqual(
                [
                    (ArbscFan.Cpu, (byte)0),
                    (ArbscFan.System, (byte)0),
                    (ArbscFan.Cpu, (byte)0),
                    (ArbscFan.Cpu, (byte)7),
                    (ArbscFan.System, (byte)7),
                ],
                backend.SetCalls);
            Equal<float?>(
                ArbscProfile.ToPercentage(ArbscProfile.ToCode(13f)),
                cpu.Value);
            Equal<float?>(
                ArbscProfile.ToPercentage(ArbscProfile.ToCode(13f)),
                system.Value);
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void PluginUpdateErrorRetention()
    {
        FakeBackend backend = new(
            new ArbscTelemetry(2_800, 1_700, 60, 39),
            new ArbscTelemetry(2_900, 1_800, 61, 40));
        backend.FailReadCalls.Add(2);
        FakeLogger logger = new();
        M1ProPlugin plugin = new(() => backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            IPluginControlSensor2 cpu = FindControl(
                container.ControlSensors,
                "minisforum.m1pro.arbsc.cpu-control");
            cpu.Set(25f);
            float? confirmedControl = cpu.Value;

            plugin.Update();
            Equal(2_800f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan1").Value);
            Equal(39f, Find(
                container.TempSensors,
                "minisforum.m1pro.arbsc.system-temperature").Value);
            Equal(confirmedControl, cpu.Value);
            Equal(0, backend.DisposeCalls);
            True(logger.Messages.Any(message =>
                message.Contains("telemetry read failed", StringComparison.Ordinal)));

            plugin.Update();
            Equal(2_900f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan1").Value);
            Equal(1_800f, Find(
                container.FanSensors,
                "minisforum.m1pro.arbsc.fan2").Value);
            Equal(61f, Find(
                container.TempSensors,
                "minisforum.m1pro.arbsc.cpu-temperature").Value);
            Equal(confirmedControl, cpu.Value);
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void PluginInitializationFailure()
    {
        FakeBackend initializeFailure = new(
            new ArbscTelemetry(1, 2, 3, 4))
        {
            InitializeException = new IOException("expected initialize failure"),
        };
        M1ProPlugin initializePlugin = new(() => initializeFailure);
        Throws<IOException>(initializePlugin.Initialize);
        Equal(1, initializeFailure.DisposeCalls);

        FakeBackend readFailure = new(new ArbscTelemetry(1, 2, 3, 4));
        readFailure.FailReadCalls.Add(1);
        M1ProPlugin readPlugin = new(() => readFailure);
        Throws<IOException>(readPlugin.Initialize);
        Equal(1, readFailure.DisposeCalls);
    }

    private static PawnIoArbscBackend CreateBackend(FakeTransport transport) => new(
        static () => ArbscProfile.Board,
        () => transport);

    private static byte[] ArbitraryBaseline() => Enumerable.Range(0, 30)
        .Select(index => (byte)(0x40 + index))
        .ToArray();

    private static void AssertMemory(FakeTransport transport, IEnumerable<EcWrite> writes)
    {
        foreach (EcWrite write in writes)
        {
            Equal(write.Value, transport.ByteAt(write.Address));
        }
    }

    private static IPluginSensor Find(List<IPluginSensor> sensors, string id) =>
        sensors.Single(sensor => sensor.Id == id);

    private static IPluginControlSensor2 FindControl(
        List<IPluginControlSensor> sensors,
        string id) =>
        (IPluginControlSensor2)sensors.Single(sensor => sensor.Id == id);

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool condition) => True(!condition);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; found {actual}.");
        }
    }

    private static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual)
    {
        T[] expectedArray = expected.ToArray();
        T[] actualArray = actual.ToArray();
        if (!expectedArray.SequenceEqual(actualArray))
        {
            throw new InvalidOperationException(
                "Expected [" + string.Join(", ", expectedArray) + "]; found [" +
                string.Join(", ", actualArray) + "].");
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class FakeContainer : IPluginSensorsContainer
    {
        public List<IPluginControlSensor> ControlSensors { get; } = [];
        public List<IPluginSensor> FanSensors { get; } = [];
        public List<IPluginSensor> TempSensors { get; } = [];
    }

    private sealed class FakeLogger : IPluginLogger
    {
        internal List<string> Messages { get; } = [];

        public void Log(string message) => Messages.Add(message);
    }

    private sealed class FakeTransport : IArbscTransport
    {
        private readonly Dictionary<ushort, byte> memory = [];
        private int readCalls;
        private int writeCalls;

        internal FakeTransport()
        {
            InitialBaseline = ArbitraryBaseline();
            for (int index = 0; index < ArbscProfile.OwnedAddresses.Length; index++)
            {
                memory[ArbscProfile.OwnedAddresses[index]] = InitialBaseline[index];
            }
            for (int index = 0; index < ArbscProfile.ExpectedControllerProfile.Length;
                index++)
            {
                memory[ArbscProfile.ControllerProfileAddresses[index]] =
                    ArbscProfile.ExpectedControllerProfile[index];
            }
            byte[] telemetry = [0x0b, 0xb8, 0x07, 0x6c, 62, 41];
            for (int index = 0; index < telemetry.Length; index++)
            {
                memory[ArbscProfile.TelemetryAddresses[index]] = telemetry[index];
            }
        }

        internal List<ushort[]> ReadBatches { get; } = [];

        internal byte[] InitialBaseline { get; }

        internal List<EcWrite[]> WriteBatches { get; } = [];

        internal HashSet<int> FailWriteCalls { get; } = [];

        internal HashSet<int> FailReadCalls { get; } = [];

        internal bool Disposed { get; private set; }

        public byte[] Read(ushort[] addresses)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            readCalls++;
            ReadBatches.Add((ushort[])addresses.Clone());
            if (FailReadCalls.Contains(readCalls))
            {
                throw new IOException($"Expected fake read failure {readCalls}.");
            }
            return addresses.Select(ByteAt).ToArray();
        }

        public void Write(EcWrite[] writes)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            writeCalls++;
            EcWrite[] copy = (EcWrite[])writes.Clone();
            WriteBatches.Add(copy);
            foreach (EcWrite write in copy)
            {
                memory[write.Address] = write.Value;
            }
            if (FailWriteCalls.Contains(writeCalls))
            {
                throw new IOException($"Expected fake write failure {writeCalls}.");
            }
        }

        public void Dispose() => Disposed = true;

        internal byte ByteAt(ushort address) => memory.TryGetValue(address, out byte value)
            ? value
            : (byte)0;

        internal void SetByte(ushort address, byte value) => memory[address] = value;
    }

    private sealed class FakeBackend(params ArbscTelemetry[] samples) : IArbscBackend
    {
        private readonly Queue<ArbscTelemetry> samples = new(samples);
        private ArbscTelemetry? lastSample;

        internal Exception? InitializeException { get; init; }

        internal HashSet<int> FailReadCalls { get; } = [];

        internal int InitializeCalls { get; private set; }

        internal int ReadCalls { get; private set; }

        internal List<(ArbscFan Fan, byte Code)> SetCalls { get; } = [];

        internal List<ArbscFan> ResetCalls { get; } = [];

        internal int DisposeCalls { get; private set; }

        public void Initialize()
        {
            InitializeCalls++;
            if (InitializeException is not null)
            {
                throw InitializeException;
            }
        }

        public ArbscTelemetry ReadTelemetry()
        {
            ReadCalls++;
            if (FailReadCalls.Contains(ReadCalls))
            {
                throw new IOException($"Expected fake read failure {ReadCalls}.");
            }
            if (samples.TryDequeue(out ArbscTelemetry? sample))
            {
                lastSample = sample;
            }
            return lastSample ?? throw new InvalidOperationException("No fake telemetry.");
        }

        public void Set(ArbscFan fan, byte code) => SetCalls.Add((fan, code));

        public void Reset(ArbscFan fan) => ResetCalls.Add(fan);

        public void Dispose() => DisposeCalls++;
    }
}
