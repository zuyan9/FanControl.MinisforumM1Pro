using System.Security.Cryptography;
using FanControl.Plugins;

namespace FanControl.MinisforumM1Pro.Tests;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Body)[] tests =
        [
            ("exact model resolution", ExactModelResolution),
            ("controller identity policies", ControllerIdentityPolicies),
            ("model-specific control policies", ModelSpecificControlPolicies),
            ("known firmware curve fingerprints", KnownFirmwareCurveFingerprints),
            ("shared curve ownership layout", SharedCurveOwnershipLayout),
            ("telemetry decoding and tear retry", TelemetryDecoding),
            ("PawnIO native slot sequences and allowlists", PawnIoNativeSlotSequences),
            ("outer controller discovery", OuterControllerDiscovery),
            ("MTBSI startup gates", MtbsiStartupGates),
            ("startup curve recovery and diagnostics", StartupCurveRecoveryAndDiagnostics),
            ("backend transactions and bounds", BackendTransactionsAndBounds),
            ("M1 Lite successful transactions", M1LiteSuccessfulTransactions),
            ("backend verified recovery", BackendVerifiedRecovery),
            ("backend ownership drift and stock reset", BackendOwnershipDrift),
            ("plugin control failure shutdown and retry", PluginControlFailureShutdown),
            ("M1 Pro plugin compatibility", M1ProPluginCompatibility),
            ("M1 Lite plugin metadata and controls", M1LitePluginMetadataAndControls),
            ("plugin invalid commands", PluginInvalidCommands),
            ("plugin gates and lifecycle failures", PluginGatesAndLifecycleFailures),
            (
                "plugin telemetry invalidation and recovery",
                PluginTelemetryInvalidationAndRecovery),
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

    private static void ExactModelResolution()
    {
        Same(M1ModelProfiles.M1Pro, M1ModelProfiles.ResolveExact("ARBSC"));
        Same(M1ModelProfiles.M1Lite, M1ModelProfiles.ResolveExact("MTBSI"));
        Equal<M1ModelProfile?>(null, M1ModelProfiles.TryResolveExact("arbsc"));
        Equal<M1ModelProfile?>(null, M1ModelProfiles.TryResolveExact(" MTBSI "));
        Throws<PlatformNotSupportedException>(() => M1ModelProfiles.ResolveExact("Other"));

        Equal("Minisforum M1 Pro (ARBSC)", M1ModelProfiles.M1Pro.PluginName);
        Equal("minisforum.m1pro.arbsc", M1ModelProfiles.M1Pro.SensorIdPrefix);
        True(M1ModelProfiles.M1Pro.HardwareValidated);
        True(M1ModelProfiles.M1Pro.ValidateTelemetry);
        Equal("Minisforum M1 Lite (MTBSI)", M1ModelProfiles.M1Lite.PluginName);
        Equal("minisforum.m1lite.mtbsi", M1ModelProfiles.M1Lite.SensorIdPrefix);
        False(M1ModelProfiles.M1Lite.HardwareValidated);
        True(M1ModelProfiles.M1Lite.ValidateTelemetry);

        HostIdentityGate.AssertBoard(M1ModelProfiles.M1Pro, "ARBSC");
        HostIdentityGate.AssertBoard(M1ModelProfiles.M1Lite, "MTBSI");
        Throws<PlatformNotSupportedException>(() =>
            HostIdentityGate.AssertBoard(M1ModelProfiles.M1Lite, "ARBSC"));
    }

    private static void ControllerIdentityPolicies()
    {
        M1ModelProfiles.M1Pro.AssertOuterIdentity([0x55, 0x71, 0x07]);
        Throws<PlatformNotSupportedException>(() =>
            M1ModelProfiles.M1Pro.AssertOuterIdentity([0x55, 0x71, 0x08]));

        M1ModelProfiles.M1Lite.AssertOuterIdentity([0x55, 0x71, 0x01]);
        M1ModelProfiles.M1Lite.AssertOuterIdentity([0x55, 0x71, 0xfe]);
        Throws<PlatformNotSupportedException>(() =>
            M1ModelProfiles.M1Lite.AssertOuterIdentity([0x55, 0x71, 0x00]));
        Throws<PlatformNotSupportedException>(() =>
            M1ModelProfiles.M1Lite.AssertOuterIdentity([0x55, 0x71, 0xff]));
        Throws<PlatformNotSupportedException>(() =>
            M1ModelProfiles.M1Lite.AssertOuterIdentity([0x55, 0x72, 0x07]));

        M1ModelProfiles.M1Lite.AssertNativeIdentity(
            [0x55, 0x71, 0x09],
            [0x55, 0x71, 0x09]);
        Throws<PlatformNotSupportedException>(() =>
            M1ModelProfiles.M1Lite.AssertNativeIdentity(
                [0x55, 0x71, 0x09],
                [0x55, 0x71, 0x07]));
    }

    private static void ModelSpecificControlPolicies()
    {
        FanControlPolicy pro = M1ModelProfiles.M1Pro.CpuPolicy;
        AssertRequest(pro.Resolve(0f), 0, 0f);
        AssertRequest(pro.Resolve(1f), 0, 100f / 51f);
        AssertRequest(pro.Resolve(10f), 0, 5 * 100f / 51f);
        AssertRequest(pro.Resolve(13f), 7, 7 * 100f / 51f);
        AssertRequest(pro.Resolve(50f), 26, 26 * 100f / 51f);
        AssertRequest(pro.Resolve(100f), 51, 100f);
        True(pro.AcceptsCode(0));
        False(pro.AcceptsCode(6));
        True(pro.AcceptsCode(7));
        True(pro.AcceptsCode(51));
        False(pro.AcceptsCode(52));

        FanControlPolicy liteCpu = M1ModelProfiles.M1Lite.CpuPolicy;
        AssertRequest(liteCpu.Resolve(0f), 0, 0f);
        AssertRequest(liteCpu.Resolve(1f), 1, 100f / 51f);
        AssertRequest(liteCpu.Resolve(50f), 26, 26 * 100f / 51f);
        AssertRequest(liteCpu.Resolve(100f), 51, 100f);

        FanControlPolicy liteSystem = M1ModelProfiles.M1Lite.SystemPolicy;
        AssertRequest(liteSystem.Resolve(0f), 0, 0f);
        AssertRequest(liteSystem.Resolve(1f), 1, 100f / 51f);
        AssertRequest(liteSystem.Resolve(50f), 26, 26 * 100f / 51f);
        AssertRequest(liteSystem.Resolve(100f), 51, 100f);

        Throws<ArgumentOutOfRangeException>(() => pro.Resolve(float.NaN));
        Throws<ArgumentOutOfRangeException>(() => pro.Resolve(float.PositiveInfinity));
        Throws<ArgumentOutOfRangeException>(() => pro.Resolve(-0.01f));
        Throws<ArgumentOutOfRangeException>(() => pro.Resolve(100.01f));
        liteCpu.AssertCode(0);
        liteCpu.AssertCode(1);
        liteSystem.AssertCode(0);
        liteSystem.AssertCode(51);
        Throws<ArgumentOutOfRangeException>(() => liteCpu.AssertCode(52));
        Throws<ArgumentOutOfRangeException>(() => liteSystem.AssertCode(52));
    }

    private static void KnownFirmwareCurveFingerprints()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        M1ModelProfile lite = M1ModelProfiles.M1Lite;
        byte[] normal = lite.StockCurve(0xb0) ?? throw new InvalidOperationException();
        byte[] alternate = lite.StockCurve(0xb1) ?? throw new InvalidOperationException();
        Equal(64, normal.Length);
        Equal(64, alternate.Length);
        Equal(
            "82938edc4142056f6fb9f55f7183caec656ac4e699242a617f94230c28c70c38",
            Sha256(normal));
        Equal(
            "e62d75c1b13099b60d206be32a11189ee89dfab8cef9f8d6bd82acde50c1c7cb",
            Sha256(alternate));
        True(lite.MatchesCurve(0xb0, normal));
        True(lite.MatchesCurve(0xb2, normal));
        True(lite.MatchesCurve(0xb1, alternate));
        False(lite.MatchesCurve(0xb1, normal));
        False(lite.MatchesCurve(0xaf, normal));
        SequenceEqual(
            normal,
            pro.StockCurve(0xb0) ?? throw new InvalidOperationException());
        SequenceEqual(
            normal,
            pro.StockCurve(0xb2) ?? throw new InvalidOperationException());
        SequenceEqual(
            alternate,
            pro.StockCurve(0xb1) ?? throw new InvalidOperationException());
        normal[0] ^= 1;
        False(lite.MatchesCurve(0xb0, normal));

        FakeTransport transport = FakeTransport.ForProfile(lite, 1, 0x07);
        lite.AssertStartupProbe(transport.Read(lite.StartupProbeAddresses));
        transport.SetByte(0x200d, 0xcf);
        lite.AssertStartupProbe(transport.Read(lite.StartupProbeAddresses));
        transport.SetByte(0x200d, 0xca);
        Throws<PlatformNotSupportedException>(() =>
            lite.AssertStartupProbe(transport.Read(lite.StartupProbeAddresses)));
    }

    private static void SharedCurveOwnershipLayout()
    {
        byte[] curve = M1ModelProfiles.M1Lite.StockCurve(0xb0) ??
            throw new InvalidOperationException();
        byte[] baseline = M1EcLayout.CaptureOwned(curve);
        Equal(30, baseline.Length);

        EcWrite[] cpuManual = M1EcLayout.ManualWrites(M1Fan.Cpu, 17);
        SequenceEqual(
            Enumerable.Range(0x0670, 8).Select(address => (ushort)address),
            cpuManual.Take(8).Select(write => write.Address));
        True(cpuManual.Take(8).All(write => write.Value == 0));
        SequenceEqual(
            Enumerable.Range(0, 8).Select(row => (ushort)(0x0640 + (row * 3))),
            cpuManual.Skip(8).Select(write => write.Address));
        True(cpuManual.Skip(8).All(write => write.Value == 17));

        EcWrite[] cpuUpdateOnly = M1EcLayout.ManualWrites(
            M1Fan.Cpu,
            25,
            includeSlopes: false);
        Equal(8, cpuUpdateOnly.Length);
        SequenceEqual(
            Enumerable.Range(0, 8).Select(row => (ushort)(0x0640 + (row * 3))),
            cpuUpdateOnly.Select(write => write.Address));
        True(cpuUpdateOnly.All(write => write.Value == 25));

        EcWrite[] systemManual = M1EcLayout.ManualWrites(M1Fan.System, 41);
        Equal(14, systemManual.Length);
        SequenceEqual(
            Enumerable.Range(0x0678, 7).Select(address => (ushort)address),
            systemManual.Take(7).Select(write => write.Address));

        EcWrite[] systemUpdateOnly = M1EcLayout.ManualWrites(
            M1Fan.System,
            30,
            includeSlopes: false);
        Equal(7, systemUpdateOnly.Length);
        SequenceEqual(
            Enumerable.Range(0, 7).Select(row => (ushort)(0x0658 + (row * 3))),
            systemUpdateOnly.Select(write => write.Address));

        byte[] manualCurve = M1EcLayout.Apply(curve, cpuManual.Concat(systemManual));
        SequenceEqual(
            cpuManual.Concat(systemManual).Select(write => write.Value),
            cpuManual.Concat(systemManual)
                .Select(write => manualCurve[write.Address - M1EcLayout.CurveBlockStart]));

        EcWrite[] cpuRestore = M1EcLayout.RestoreWrites(M1Fan.Cpu, baseline);
        SequenceEqual(
            Enumerable.Range(0, 8).Select(row => (ushort)(0x0640 + (row * 3)))
                .Concat(Enumerable.Range(0x0670, 8).Select(address => (ushort)address)),
            cpuRestore.Select(write => write.Address));

        EcWrite[] allRestore = M1EcLayout.AllRestoreWrites(baseline);
        Equal(30, allRestore.Length);
        SequenceEqual(
            Enumerable.Range(0, 8).Select(row => (ushort)(0x0640 + (row * 3)))
                .Concat(Enumerable.Range(0, 7)
                    .Select(row => (ushort)(0x0658 + (row * 3))))
                .Concat(Enumerable.Range(0x0670, 8).Select(address => (ushort)address))
                .Concat(Enumerable.Range(0x0678, 7).Select(address => (ushort)address)),
            allRestore.Select(write => write.Address));
        SequenceEqual(curve, M1EcLayout.Apply(manualCurve, allRestore));

        M1EcLayout.AssertReadsAllowed(M1EcLayout.TelemetryAddresses);
        M1EcLayout.AssertReadsAllowed(M1EcLayout.CurveBlockAddresses);
        M1EcLayout.AssertReadsAllowed(M1ModelProfiles.M1Lite.StartupProbeAddresses);
        M1EcLayout.AssertWritesAllowed(cpuManual);
        M1EcLayout.AssertWritesAllowed(allRestore);
        M1EcLayout.AssertWritesAllowed([new EcWrite(0x0676, 169)]);
        Throws<InvalidOperationException>(() =>
            M1EcLayout.AssertReadsAllowed([0x1234]));
        Throws<InvalidOperationException>(() =>
            M1EcLayout.AssertWritesAllowed([new EcWrite(0x0641, 25)]));
        Throws<InvalidOperationException>(() =>
            M1EcLayout.AssertWritesAllowed([new EcWrite(0x0640, 52)]));
        Throws<InvalidOperationException>(() =>
            M1EcLayout.AssertWritesAllowed([new EcWrite(0x0671, 99)]));

        byte[] uniqueCurve = Enumerable.Range(0, M1EcLayout.CurveBlockLength)
            .Select(value => (byte)value)
            .ToArray();
        byte[] uniqueBaseline = M1EcLayout.CaptureOwned(uniqueCurve);
        foreach (EcWrite write in M1EcLayout.AllRestoreWrites(uniqueBaseline))
        {
            Equal(
                uniqueCurve[write.Address - M1EcLayout.CurveBlockStart],
                write.Value);
        }

        Throws<ArgumentException>(() => M1EcLayout.CaptureOwned([1, 2, 3]));
        Throws<ArgumentException>(() =>
            M1EcLayout.RestoreWrites(M1Fan.Cpu, [1, 2, 3]));
    }

    private static void TelemetryDecoding()
    {
        True(M1TelemetryDecoder.TryDecodeRpm([0x0b, 0xb8, 0x0b], out int decodedRpm));
        Equal(3_000, decodedRpm);
        False(M1TelemetryDecoder.TryDecodeRpm([0x0b, 0xff, 0x0c], out int tornRpm));
        Equal(0, tornRpm);
        Throws<ArgumentException>(() =>
            M1TelemetryDecoder.TryDecodeRpm([0x0b, 0xb8], out _));

        M1Telemetry telemetry = M1TelemetryDecoder.Decode(
            [0x0b, 0xb8, 0x0b, 0x07, 0x6c, 0x07, 63, 42]);
        Equal(3_000, telemetry.CpuFanRpm);
        Equal(1_900, telemetry.SystemFanRpm);
        Equal(63, telemetry.CpuTemperatureC);
        Equal(42, telemetry.SystemTemperatureC);
        Throws<ArgumentException>(() => M1TelemetryDecoder.Decode([0, 1, 2]));
        Throws<IOException>(() =>
            M1TelemetryDecoder.Decode([0x0b, 0xff, 0x0c, 0x07, 0x6c, 0x07, 63, 42]));

        M1ModelProfiles.M1Pro.AssertTelemetry(telemetry);
        M1ModelProfiles.M1Lite.AssertTelemetry(telemetry);
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Pro.AssertTelemetry(new M1Telemetry(10_001, 0, 20, 20)));
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Pro.AssertTelemetry(new M1Telemetry(0, 0, 126, 20)));
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Lite.AssertTelemetry(new M1Telemetry(10_001, 0, 20, 20)));
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Lite.AssertTelemetry(new M1Telemetry(0, 0, 126, 20)));

        FakeTransport rolloverOnce = FakeTransport.ForProfile(
            M1ModelProfiles.M1Pro,
            1,
            0x07);
        PawnIoM1Backend rolloverBackend = CreateBackend(
            M1ModelProfiles.M1Pro,
            rolloverOnce);
        rolloverBackend.Initialize();
        rolloverOnce.TelemetrySamples.Enqueue(
            [0x0b, 0xff, 0x0c, 0x07, 0x6c, 0x07, 52, 39]);
        rolloverOnce.CpuRpmSamples.Enqueue([0x0c, 0x00, 0x0c]);
        M1Telemetry retried = rolloverBackend.ReadTelemetry();
        Equal(3_072, retried.CpuFanRpm);
        Equal(1_900, retried.SystemFanRpm);
        Equal(52, retried.CpuTemperatureC);
        Equal(39, retried.SystemTemperatureC);

        rolloverOnce.TelemetrySamples.Enqueue(
            [0x0b, 0xff, 0x0c, 0x07, 0x6c, 0x07, 52, 39]);
        rolloverOnce.CpuRpmSamples.Enqueue([0x0c, 0xff, 0x0d]);
        rolloverOnce.CpuRpmSamples.Enqueue([0x0d, 0xff, 0x0e]);
        Throws<IOException>(() => rolloverBackend.ReadTelemetry());
        rolloverBackend.Dispose();
    }

    private static void PawnIoNativeSlotSequences()
    {
        AssertPawnIoSlot(0, 0x2e);
        AssertPawnIoSlot(1, 0x4e);

        FakePawnIoExecutor allowlistExecutor = new();
        using (PawnIoTransport allowlistTransport = new(1, allowlistExecutor, [0x42]))
        {
            Throws<InvalidOperationException>(() =>
                allowlistTransport.RunExclusive(access => access.Read([0x1234])));
            Throws<InvalidOperationException>(() =>
                allowlistTransport.RunExclusive(access =>
                {
                    access.Write([new EcWrite(0x0641, 0x20)]);
                    return 0;
                }));
            Throws<InvalidOperationException>(() =>
                allowlistTransport.RunExclusive(access =>
                {
                    access.Write([new EcWrite(0x0640, 52)]);
                    return 0;
                }));
        }

        FakePawnIoExecutor failing = new(0xcd)
        {
            FailSelectedReadOnce = true,
        };
        using (PawnIoTransport transport = new(1, failing, [0x42]))
        {
            Throws<IOException>(() => transport.RunExclusive(access =>
            {
                access.Read([0x0640]);
                return 0;
            }));
            byte[] recovered = transport.RunExclusive(access => access.Read([0x0640]));
            SequenceEqual<byte>([0xcd], recovered);
            Equal(2, failing.Calls.Count(call =>
                call.Name == "ioctl_select_slot"));
            Equal(2, failing.Calls.Count(call =>
                call.Name == "ioctl_pio_outb" &&
                call.Input.SequenceEqual([0x4eUL, 0x20UL])));
        }
        True(failing.Disposed);

        FakePawnIoExecutor combinedParkFailure = new()
        {
            FailSelectedReadOnce = true,
            FailNestedParkOnce = true,
        };
        using (PawnIoTransport transport = new(1, combinedParkFailure, [0x42]))
        {
            AggregateException combined = Throws<AggregateException>(() =>
                transport.RunExclusive(access => access.Read([0x0640])));
            Equal(2, combined.InnerExceptions.Count);
        }
        True(combinedParkFailure.Disposed);

        FakePawnIoExecutor outerParkFailure = new(0xaa)
        {
            FailOuterParkOnce = true,
        };
        using (PawnIoTransport transport = new(1, outerParkFailure, [0x42]))
        {
            InvalidOperationException poisoned = Throws<InvalidOperationException>(() =>
                transport.RunExclusive(access => access.Read([0x0640])));
            True(poisoned.Message.Contains("Super-I/O selector state is ambiguous"));
            int callCountAfterPoison = outerParkFailure.Calls.Count;
            Throws<InvalidOperationException>(() =>
                transport.RunExclusive(access => access.Read([0x0640])));
            Equal(callCountAfterPoison, outerParkFailure.Calls.Count);
        }
        True(outerParkFailure.Disposed);

        FakePawnIoExecutor selectFailure = new(0xce)
        {
            FailSelectSlotOnce = true,
        };
        using (PawnIoTransport transport = new(0, selectFailure, [0x42]))
        {
            Throws<IOException>(() => transport.RunExclusive(access =>
            {
                access.Read([0x0640]);
                return 0;
            }));
            Equal(1, selectFailure.Calls.Count(call =>
                call.Name == "ioctl_pio_outb" &&
                call.Input.SequenceEqual([0x2eUL, 0x20UL])));
            SequenceEqual<byte>(
                [0xce],
                transport.RunExclusive(access => access.Read([0x0640])));
        }
        True(selectFailure.Disposed);

        FakePawnIoExecutor outerFailure = new(0x55, 0x71, 0x09)
        {
            FailOuterReadOnce = true,
        };
        using (PawnIoTransport transport = new(1, outerFailure, [0x42]))
        {
            Throws<IOException>(() => transport.ReadOuterIdentity());
            Equal(1, outerFailure.Calls.Count(call =>
                call.Name == "ioctl_pio_outb" &&
                call.Input.SequenceEqual([0x4eUL, 0x20UL])));
            SequenceEqual<byte>(
                [0x55, 0x71, 0x09],
                transport.ReadOuterIdentity());
        }
        True(outerFailure.Disposed);

        FakePawnIoExecutor parkFailure = new(0xaa, 0xbb)
        {
            FailNestedParkOnce = true,
        };
        using (PawnIoTransport transport = new(1, parkFailure, [0x42]))
        {
            Throws<IOException>(() => transport.RunExclusive(access =>
                access.Read([0x0640])));
            Equal(1, parkFailure.Calls.Count(call =>
                call.Name == "ioctl_pio_outb" &&
                call.Input.SequenceEqual([0x4eUL, 0x20UL])));
            SequenceEqual<byte>(
                [0xbb],
                transport.RunExclusive(access => access.Read([0x0640])));
        }
        True(parkFailure.Disposed);

        FakePawnIoExecutor invalid = new();
        Throws<ArgumentOutOfRangeException>(() =>
            new PawnIoTransport(2, invalid, [0x42]));
        True(invalid.Disposed);
    }

    private static void AssertPawnIoSlot(byte slot, ulong outerIndexPort)
    {
        FakePawnIoExecutor executor = new(0x55, 0x71, 0x09, 0xa5);
        using (PawnIoTransport transport = new(slot, executor, [0x42, 0x43]))
        {
            SequenceEqual<byte>([0x42, 0x43], executor.LoadedModule);
            SequenceEqual<byte>([0x55, 0x71, 0x09], transport.ReadOuterIdentity());
            byte[] read = transport.RunExclusive(access =>
            {
                byte[] result = access.Read([0x0438]);
                access.Write([new EcWrite(0x0640, 0x33)]);
                return result;
            });
            SequenceEqual<byte>([0xa5], read);

            (string Name, ulong[] Input, int OutputCount)[] expected =
            [
                ("ioctl_select_slot", [slot], 0),
                ("ioctl_superio_inb", [0x20], 1),
                ("ioctl_superio_inb", [0x21], 1),
                ("ioctl_superio_inb", [0x22], 1),
                ("ioctl_pio_outb", [outerIndexPort, 0x20], 0),
                ("ioctl_select_slot", [slot], 0),
                ("ioctl_superio_outb", [0x2e, 0x11], 0),
                ("ioctl_superio_outb", [0x2f, 0x04], 0),
                ("ioctl_superio_outb", [0x2e, 0x10], 0),
                ("ioctl_superio_outb", [0x2f, 0x38], 0),
                ("ioctl_superio_outb", [0x2e, 0x12], 0),
                ("ioctl_superio_inb", [0x2f], 1),
                ("ioctl_superio_outb", [0x2e, 0x11], 0),
                ("ioctl_superio_outb", [0x2f, 0x06], 0),
                ("ioctl_superio_outb", [0x2e, 0x10], 0),
                ("ioctl_superio_outb", [0x2f, 0x40], 0),
                ("ioctl_superio_outb", [0x2e, 0x12], 0),
                ("ioctl_superio_outb", [0x2f, 0x33], 0),
                ("ioctl_superio_outb", [0x2e, 0x10], 0),
                ("ioctl_pio_outb", [outerIndexPort, 0x20], 0),
            ];
            Equal(expected.Length, executor.Calls.Count);
            for (int index = 0; index < expected.Length; index++)
            {
                Equal(expected[index].Name, executor.Calls[index].Name);
                SequenceEqual(expected[index].Input, executor.Calls[index].Input);
                Equal(expected[index].OutputCount, executor.Calls[index].OutputCount);
            }
        }
        True(executor.Disposed);
    }

    private static void OuterControllerDiscovery()
    {
        M1ModelProfile lite = M1ModelProfiles.M1Lite;

        FakeTransport slot1 = FakeTransport.ForProfile(lite, 1, 0x07);
        FakeTransport bad0 = FakeTransport.WrongController(0);
        PawnIoM1Backend backend1 = CreateBackend(lite, slot1, bad0);
        backend1.Initialize();
        Equal<byte?>((byte)1, backend1.ActiveSlot);
        Equal(1, slot1.OuterReadCalls);
        Equal(1, bad0.OuterReadCalls);
        Equal(0, bad0.ReadBatches.Count);
        True(bad0.Disposed);
        backend1.Dispose();
        Equal(0, slot1.WriteBatches.Count);

        FakeTransport bad1 = FakeTransport.WrongController(1);
        FakeTransport slot0 = FakeTransport.ForProfile(lite, 0, 0x09);
        PawnIoM1Backend backend0 = CreateBackend(lite, bad1, slot0);
        backend0.Initialize();
        Equal<byte?>((byte)0, backend0.ActiveSlot);
        Equal(0, bad1.ReadBatches.Count);
        True(bad1.Disposed);
        backend0.Dispose();

        FakeTransport none1 = FakeTransport.WrongController(1);
        FakeTransport none0 = FakeTransport.WrongController(0);
        PawnIoM1Backend none = CreateBackend(lite, none1, none0);
        Throws<PlatformNotSupportedException>(none.Initialize);
        Equal(0, none1.ReadBatches.Count);
        Equal(0, none0.ReadBatches.Count);
        True(none1.Disposed);
        True(none0.Disposed);

        FakeTransport both1 = FakeTransport.ForProfile(lite, 1, 0x07);
        FakeTransport both0 = FakeTransport.ForProfile(lite, 0, 0x08);
        PawnIoM1Backend ambiguous = CreateBackend(lite, both1, both0);
        Throws<PlatformNotSupportedException>(ambiguous.Initialize);
        Equal(0, both1.ReadBatches.Count);
        Equal(0, both0.ReadBatches.Count);
        True(both1.Disposed);
        True(both0.Disposed);

        FakeTransport readable1 = FakeTransport.ForProfile(lite, 1, 0x07);
        FakeTransport unreadable0 = FakeTransport.WrongController(0);
        unreadable0.FailOuterRead = true;
        PawnIoM1Backend unclassified = CreateBackend(lite, readable1, unreadable0);
        Throws<PlatformNotSupportedException>(unclassified.Initialize);
        Equal(0, readable1.ReadBatches.Count);
        Equal(0, unreadable0.ReadBatches.Count);
        True(readable1.Disposed);
        True(unreadable0.Disposed);

        int wrongBoardFactoryCalls = 0;
        PawnIoM1Backend wrongBoard = new(
            lite,
            static () => "ARBSC",
            _ =>
            {
                wrongBoardFactoryCalls++;
                return FakeTransport.WrongController(1);
            });
        Throws<PlatformNotSupportedException>(wrongBoard.Initialize);
        Equal(0, wrongBoardFactoryCalls);

        FakeTransport wrongRevision = FakeTransport.ForProfile(
            M1ModelProfiles.M1Pro,
            1,
            0x08);
        PawnIoM1Backend pro = CreateBackend(M1ModelProfiles.M1Pro, wrongRevision);
        Throws<PlatformNotSupportedException>(pro.Initialize);
        Equal(0, wrongRevision.ReadBatches.Count);
    }

    private static void MtbsiStartupGates()
    {
        M1ModelProfile lite = M1ModelProfiles.M1Lite;
        byte[] normalCurve = lite.StockCurve(0xb0) ??
            throw new InvalidOperationException();
        lite.AssertRuntimeSample(
            [0x00, 0x00, 0x03, 0xe8, 25, 25, 0, 10],
            normalCurve);
        lite.AssertRuntimeSample(
            [0x07, 0x08, 0x06, 0xa4, 25, 25, 18, 17],
            normalCurve);
        lite.AssertRuntimeSample(
            [0x13, 0xec, 0x10, 0x04, 100, 100, 51, 41],
            normalCurve);
        lite.AssertRuntimeSample(
            [0x09, 0xc4, 0x08, 0x34, 50, 50, 25, 21],
            normalCurve);
        byte[] alternateCurve = lite.StockCurve(0xb1) ??
            throw new InvalidOperationException();
        lite.AssertRuntimeSample(
            [0x0f, 0x3c, 0x10, 0x04, 92, 92, 39, 41],
            alternateCurve);
        Throws<PlatformNotSupportedException>(() =>
            lite.AssertRuntimeSample(
                [0x02, 0x58, 0x03, 0xe8, 25, 25, 6, 10],
                normalCurve));
        Throws<PlatformNotSupportedException>(() =>
            lite.AssertRuntimeSample(
                [0x00, 0x00, 0x10, 0x04, 100, 100, 0, 41],
                normalCurve));
        Throws<PlatformNotSupportedException>(() =>
            lite.AssertRuntimeSample(
                [0x13, 0xec, 0x03, 0xe8, 100, 100, 51, 10],
                normalCurve));

        FakeTransport good = FakeTransport.ForProfile(lite, 1, 0x09);
        PawnIoM1Backend goodBackend = CreateBackend(
            lite,
            good,
            FakeTransport.WrongController(0));
        goodBackend.Initialize();
        True(good.ReadBatches.Count(batch =>
            batch.SequenceEqual(M1EcLayout.CurveBlockAddresses)) >= 2);
        Equal(2, good.ReadBatches.Count(batch =>
            batch.SequenceEqual(M1EcLayout.RuntimeSampleAddresses)));
        goodBackend.Dispose();

        byte[] lowSample = [0x00, 0x00, 0x03, 0xe8, 10, 10, 0, 10];
        byte[] boundarySample = [0x07, 0x08, 0x06, 0xa4, 25, 25, 18, 17];
        byte[] invalidSample = [0x00, 0x00, 0x03, 0xe8, 10, 10, 0, 11];

        FakeTransport stableAfterChange = FakeTransport.ForProfile(lite, 1, 0x09);
        stableAfterChange.EnqueueRuntimeSamples(
            lowSample,
            boundarySample,
            boundarySample);
        PawnIoM1Backend changedBackend = CreateBackend(
            lite,
            stableAfterChange,
            FakeTransport.WrongController(0));
        changedBackend.Initialize();
        Equal(3, stableAfterChange.ReadBatches.Count(batch =>
            batch.SequenceEqual(M1EcLayout.RuntimeSampleAddresses)));
        changedBackend.Dispose();

        FakeTransport resetAfterInvalid = FakeTransport.ForProfile(lite, 1, 0x09);
        resetAfterInvalid.EnqueueRuntimeSamples(
            lowSample,
            invalidSample,
            lowSample,
            lowSample);
        PawnIoM1Backend resetBackend = CreateBackend(
            lite,
            resetAfterInvalid,
            FakeTransport.WrongController(0));
        resetBackend.Initialize();
        Equal(4, resetAfterInvalid.ReadBatches.Count(batch =>
            batch.SequenceEqual(M1EcLayout.RuntimeSampleAddresses)));
        resetBackend.Dispose();

        FakeTransport alternating = FakeTransport.ForProfile(lite, 1, 0x09);
        alternating.EnqueueRuntimeSamples(
            lowSample,
            boundarySample,
            lowSample,
            boundarySample,
            lowSample,
            boundarySample);
        PawnIoM1Backend alternatingBackend = CreateBackend(
            lite,
            alternating,
            FakeTransport.WrongController(0));
        Throws<PlatformNotSupportedException>(alternatingBackend.Initialize);
        Equal(6, alternating.ReadBatches.Count(batch =>
            batch.SequenceEqual(M1EcLayout.RuntimeSampleAddresses)));
        Equal(0, alternating.WriteBatches.Count);
        True(alternating.Disposed);

        FakeTransport reservedBit = FakeTransport.ForProfile(lite, 1, 0x09);
        reservedBit.SetByte(0x200d, 0xcf);
        PawnIoM1Backend reservedBackend = CreateBackend(
            lite,
            reservedBit,
            FakeTransport.WrongController(0));
        reservedBackend.Initialize();
        reservedBackend.Dispose();

        AssertLiteStartupRejected(transport => transport.SetByte(0x0200, 0x25));
        AssertLiteStartupRejected(transport => transport.SetByte(0x021a, 0x00));
        AssertLiteStartupRejected(transport => transport.SetByte(0x0228, 0x00));
        AssertLiteStartupRejected(transport => transport.SetByte(0x200d, 0xca));
        AssertLiteStartupRejected(transport => transport.SetByte(0x032e, 0xaf));
        AssertLiteStartupRejected(transport => transport.SetByte(0x0640, 0xff));
        AssertLiteStartupRejected(transport => transport.SetByte(0x2002, 0x08));
        AssertLiteStartupRejected(transport =>
        {
            transport.SetByte(0x0422, 0x00);
            transport.SetByte(0x0423, 0x00);
        });
        AssertLiteStartupRejected(transport => transport.SetByte(0x0428, 126));
        AssertLiteStartupRejected(transport => transport.SetByte(0x0433, 11));
    }

    private static void StartupCurveRecoveryAndDiagnostics()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        M1ModelProfile lite = M1ModelProfiles.M1Lite;

        // 1. Full leftover manual state on both fans across selectors 0xb0, 0xb1, 0xb2.
        foreach (byte mode in new byte[] { 0xb0, 0xb1, 0xb2 })
        {
            FakeTransport leftover = FakeTransport.ForProfile(pro, 1, 0x07);
            leftover.SetStockMode(mode);
            byte[] stock = leftover.CurveSnapshot();
            foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 20)
                .Concat(M1EcLayout.ManualWrites(M1Fan.System, 18)))
            {
                leftover.SetByte(write.Address, write.Value);
            }

            FakeLogger logger = new();
            M1ProPlugin plugin = new(
                pro,
                () => pro.Board,
                _ => CreateBackend(pro, leftover),
                logger);
            plugin.Initialize();
            Equal(1, leftover.WriteBatches.Count);
            SequenceEqual(stock, leftover.CurveSnapshot());
            True(logger.Messages.Any(message =>
                message.Contains("startup recovery restored firmware curve") &&
                message.Contains("CPU manual code 20") &&
                message.Contains("system manual code 18")));
            plugin.Close();
            Equal(1, leftover.WriteBatches.Count);
        }

        // 2. Interrupted slope zeroing, initial base write, base update, and multi-crash slope restore.
        AssertProStartupRecovers(transport =>
        {
            foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 25).Take(3))
            {
                transport.SetByte(write.Address, write.Value);
            }
        });
        AssertProStartupRecovers(transport =>
        {
            foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 25).Take(12))
            {
                transport.SetByte(write.Address, write.Value);
            }
        });
        AssertProStartupRecovers(transport =>
        {
            foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 20))
            {
                transport.SetByte(write.Address, write.Value);
            }
            foreach (EcWrite write in M1EcLayout.ManualWrites(
                M1Fan.Cpu,
                35,
                includeSlopes: false).Take(5))
            {
                transport.SetByte(write.Address, write.Value);
            }
        });
        AssertProStartupRecovers(transport =>
        {
            // Simulate slope-zeroing interrupted at k=5, then restore interrupted at m=2.
            transport.SetByte(0x0672, 0);
            transport.SetByte(0x0673, 0);
            transport.SetByte(0x0674, 0);
        });

        // 3. M1 Lite startup recovery with coherent manual runtime target sample.
        FakeTransport liteLeftover = FakeTransport.ForProfile(lite, 1, 0x09);
        byte[] liteStock = liteLeftover.CurveSnapshot();
        foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 25))
        {
            liteLeftover.SetByte(write.Address, write.Value);
        }
        liteLeftover.SetBytesRaw(0x0420, [0x09, 0xc4, 0x03, 0xe8]);
        liteLeftover.SetBytesRaw(0x0428, [30, 30]);
        liteLeftover.SetBytesRaw(0x0432, [25, 10]);
        PawnIoM1Backend liteBackend = CreateBackend(
            lite,
            liteLeftover,
            FakeTransport.WrongController(0));
        liteBackend.Initialize();
        Equal(1, liteLeftover.WriteBatches.Count);
        SequenceEqual(liteStock, liteLeftover.CurveSnapshot());
        True(liteBackend.StartupSummary?.Contains("CPU manual code 25") == true);
        liteBackend.Dispose();

        // 4. Actionable rejection diagnostics with zero EC writes.
        PlatformNotSupportedException unknownSelector = AssertProStartupRejected(
            transport => transport.SetByte(M1EcLayout.ModeAddress, 0xb3));
        True(unknownSelector.Message.Contains("unknown EC curve selector 0xb3"));

        PlatformNotSupportedException thresholdMismatch = AssertProStartupRejected(
            transport => transport.SetByte(0x0641, 0xfe));
        True(thresholdMismatch.Message.Contains("unowned EC curve mismatch at 0x0641"));

        PlatformNotSupportedException disallowedProCode = AssertProStartupRejected(
            transport =>
            {
                foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 6))
                {
                    transport.SetByte(write.Address, write.Value);
                }
            });
        True(disallowedProCode.Message.Contains("disallowed manual code 6"));

        PlatformNotSupportedException nonZeroSlopeWithManualBase = AssertProStartupRejected(
            transport => transport.SetByte(0x0640, 20));
        True(nonZeroSlopeWithManualBase.Message.Contains("non-zero slope"));

        PlatformNotSupportedException threeRunBases = AssertProStartupRejected(
            transport =>
            {
                foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 20))
                {
                    transport.SetByte(write.Address, write.Value);
                }
                transport.SetByte(0x0640, 25);
                transport.SetByte(0x0643, 30);
            });
        True(threeRunBases.Message.Contains("CPU curve mismatch at 0x0640"));
    }

    private static void AssertProStartupRecovers(Action<FakeTransport> mutate)
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        FakeTransport transport = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] stock = transport.CurveSnapshot();
        mutate(transport);
        PawnIoM1Backend backend = CreateBackend(pro, transport);
        backend.Initialize();
        Equal(1, transport.WriteBatches.Count);
        SequenceEqual(stock, transport.CurveSnapshot());
        False(backend.NeedsRestore);
        backend.Dispose();
        Equal(1, transport.WriteBatches.Count);
    }

    private static PlatformNotSupportedException AssertProStartupRejected(
        Action<FakeTransport> mutate)
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        FakeTransport transport = FakeTransport.ForProfile(pro, 1, 0x07);
        mutate(transport);
        PawnIoM1Backend backend = CreateBackend(pro, transport);
        PlatformNotSupportedException exception =
            Throws<PlatformNotSupportedException>(backend.Initialize);
        Equal(0, transport.WriteBatches.Count);
        True(transport.Disposed);
        return exception;
    }

    private static void BackendTransactionsAndBounds()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        AssertProLifecycle(0xb0);
        AssertProLifecycle(0xb1);

        FakeTransport thirdSelector = FakeTransport.ForProfile(pro, 1, 0x07);
        thirdSelector.SetStockMode(0xb2);
        PawnIoM1Backend thirdSelectorBackend = CreateBackend(
            pro,
            thirdSelector);
        thirdSelectorBackend.Initialize();
        thirdSelectorBackend.Dispose();
        Equal(0, thirdSelector.WriteBatches.Count);

        FakeTransport noOpTransport = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend noOp = CreateBackend(pro, noOpTransport);
        noOp.Initialize();
        noOp.Dispose();
        Equal(0, noOpTransport.WriteBatches.Count);

        FakeTransport boundsTransport = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend bounds = CreateBackend(pro, boundsTransport);
        bounds.Initialize();
        Throws<ArgumentOutOfRangeException>(() => bounds.Set(M1Fan.Cpu, 6));
        Throws<ArgumentOutOfRangeException>(() => bounds.Set(M1Fan.System, 52));
        Equal(0, boundsTransport.WriteBatches.Count);
        bounds.Dispose();

        FakeTransport liteTransport = FakeTransport.ForProfile(
            M1ModelProfiles.M1Lite,
            1,
            0x09);
        PawnIoM1Backend liteBackend = CreateBackend(
            M1ModelProfiles.M1Lite,
            liteTransport,
            FakeTransport.WrongController(0));
        liteBackend.Initialize();
        Throws<ArgumentOutOfRangeException>(() => liteBackend.Set(M1Fan.Cpu, 52));
        Throws<ArgumentOutOfRangeException>(() => liteBackend.Set(M1Fan.System, 52));
        Equal(0, liteTransport.WriteBatches.Count);
        liteBackend.Dispose();
    }

    private static void AssertProLifecycle(byte stockMode)
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        FakeTransport transport = FakeTransport.ForProfile(pro, 1, 0x07);
        transport.SetStockMode(stockMode);
        byte[] baselineCurve = transport.CurveSnapshot();
        byte[] baselineOwned = M1EcLayout.CaptureOwned(baselineCurve);
        PawnIoM1Backend backend = CreateBackend(pro, transport);
        backend.Initialize();
        Equal(0, transport.WriteBatches.Count);
        False(backend.NeedsRestore);

        backend.Set(M1Fan.Cpu, 13);
        Equal<byte?>((byte)13, backend.CodeFor(M1Fan.Cpu));
        SequenceEqual(
            M1EcLayout.ManualWrites(M1Fan.Cpu, 13, includeSlopes: true),
            transport.WriteBatches[0]);
        True(backend.NeedsRestore);
        backend.Set(M1Fan.Cpu, 13);
        Equal(1, transport.WriteBatches.Count);

        backend.Set(M1Fan.Cpu, 20);
        Equal<byte?>((byte)20, backend.CodeFor(M1Fan.Cpu));
        SequenceEqual(
            M1EcLayout.ManualWrites(M1Fan.Cpu, 20, includeSlopes: false),
            transport.WriteBatches[1]);

        backend.Set(M1Fan.System, 51);
        Equal<byte?>((byte)51, backend.CodeFor(M1Fan.System));
        SequenceEqual(
            M1EcLayout.ManualWrites(M1Fan.System, 51, includeSlopes: true),
            transport.WriteBatches[2]);

        backend.Reset(M1Fan.Cpu);
        SequenceEqual(
            M1EcLayout.RestoreWrites(M1Fan.Cpu, baselineOwned),
            transport.WriteBatches[3]);
        Equal<byte?>(null, backend.CodeFor(M1Fan.Cpu));
        True(backend.NeedsRestore);

        backend.Set(M1Fan.Cpu, 15);
        SequenceEqual(
            M1EcLayout.ManualWrites(M1Fan.Cpu, 15, includeSlopes: true),
            transport.WriteBatches[4]);

        M1Telemetry telemetry = backend.ReadTelemetry();
        Equal(3_000, telemetry.CpuFanRpm);
        Equal(1_900, telemetry.SystemFanRpm);

        backend.Dispose();
        SequenceEqual(
            M1EcLayout.AllRestoreWrites(baselineOwned),
            transport.WriteBatches[5]);
        SequenceEqual(baselineCurve, transport.CurveSnapshot());
        True(transport.Disposed);
    }

    private static void M1LiteSuccessfulTransactions()
    {
        M1ModelProfile lite = M1ModelProfiles.M1Lite;
        foreach (byte mode in new byte[] { 0xb0, 0xb1, 0xb2 })
        {
            FakeTransport transport = FakeTransport.ForProfile(lite, 1, 0x09);
            transport.SetStockMode(mode);
            byte[] baselineCurve = transport.CurveSnapshot();
            byte[] baselineOwned = M1EcLayout.CaptureOwned(baselineCurve);
            PawnIoM1Backend backend = CreateBackend(
                lite,
                transport,
                FakeTransport.WrongController(0));
            backend.Initialize();

            backend.Set(M1Fan.Cpu, 0);
            backend.Set(M1Fan.System, 0);
            backend.Set(M1Fan.Cpu, 1);
            backend.Set(M1Fan.System, 1);
            backend.Set(M1Fan.Cpu, 51);
            backend.Set(M1Fan.System, 51);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.Cpu, 0, includeSlopes: true),
                transport.WriteBatches[0]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 0, includeSlopes: true),
                transport.WriteBatches[1]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.Cpu, 1, includeSlopes: false),
                transport.WriteBatches[2]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 1, includeSlopes: false),
                transport.WriteBatches[3]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.Cpu, 51, includeSlopes: false),
                transport.WriteBatches[4]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 51, includeSlopes: false),
                transport.WriteBatches[5]);

            backend.Reset(M1Fan.Cpu);
            backend.Reset(M1Fan.System);
            SequenceEqual(
                M1EcLayout.RestoreWrites(M1Fan.Cpu, baselineOwned),
                transport.WriteBatches[6]);
            SequenceEqual(
                M1EcLayout.RestoreWrites(M1Fan.System, baselineOwned),
                transport.WriteBatches[7]);
            False(backend.NeedsRestore);
            SequenceEqual(baselineCurve, transport.CurveSnapshot());
            backend.Dispose();
            Equal(8, transport.WriteBatches.Count);
        }
    }

    private static void BackendVerifiedRecovery()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        FakeTransport setFailure = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] baseline = setFailure.CurveSnapshot();
        PawnIoM1Backend setBackend = CreateBackend(pro, setFailure);
        setBackend.Initialize();
        setFailure.FailWriteCalls.Add(1);
        Throws<IOException>(() => setBackend.Set(M1Fan.Cpu, 31));
        Equal(2, setFailure.WriteBatches.Count);
        SequenceEqual(baseline, setFailure.CurveSnapshot());
        Equal<byte?>(null, setBackend.CodeFor(M1Fan.Cpu));
        False(setBackend.NeedsRestore);
        setBackend.Dispose();
        Equal(2, setFailure.WriteBatches.Count);

        FakeTransport disposeFailure = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] disposeBaseline = disposeFailure.CurveSnapshot();
        PawnIoM1Backend disposeBackend = CreateBackend(pro, disposeFailure);
        disposeBackend.Initialize();
        disposeBackend.Set(M1Fan.Cpu, 20);
        disposeFailure.FailWriteCalls.Add(2);
        disposeBackend.Dispose();
        Equal(3, disposeFailure.WriteBatches.Count);
        SequenceEqual(disposeBaseline, disposeFailure.CurveSnapshot());
        True(disposeFailure.Disposed);

        FakeTransport retryFailure = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] retryBaseline = retryFailure.CurveSnapshot();
        PawnIoM1Backend retryBackend = CreateBackend(pro, retryFailure);
        retryBackend.Initialize();
        retryBackend.Set(M1Fan.System, 30);
        retryFailure.FailWriteCalls.Add(2);
        retryFailure.FailWriteCalls.Add(3);
        Throws<IOException>(() => retryBackend.Set(M1Fan.Cpu, 20));
        True(retryBackend.NeedsRestore);
        retryBackend.Dispose();
        Equal(4, retryFailure.WriteBatches.Count);
        SequenceEqual(retryBaseline, retryFailure.CurveSnapshot());

        FakeTransport combinedFailure = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] combinedBaseline = combinedFailure.CurveSnapshot();
        PawnIoM1Backend combinedBackend = CreateBackend(pro, combinedFailure);
        combinedBackend.Initialize();
        combinedFailure.FailWriteAfterCounts[1] = 3;
        combinedFailure.FailWriteAfterCounts[2] = 1;
        Throws<IOException>(() => combinedBackend.Set(M1Fan.Cpu, 20));
        True(combinedBackend.NeedsRestore);
        False(combinedFailure.CurveSnapshot().SequenceEqual(combinedBaseline));
        combinedFailure.FailWriteAfterCounts[3] = 1;
        combinedBackend.Dispose();
        Equal(4, combinedFailure.WriteBatches.Count);
        SequenceEqual(combinedBaseline, combinedFailure.CurveSnapshot());
        True(combinedFailure.Disposed);

        FakeTransport partialSet = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] partialBaseline = partialSet.CurveSnapshot();
        PawnIoM1Backend partialBackend = CreateBackend(pro, partialSet);
        partialBackend.Initialize();
        partialSet.FailWriteAfterCounts[1] = 3;
        Throws<IOException>(() => partialBackend.Set(M1Fan.Cpu, 20));
        Equal(2, partialSet.WriteBatches.Count);
        SequenceEqual(partialBaseline, partialSet.CurveSnapshot());
        False(partialBackend.NeedsRestore);
        partialBackend.Dispose();

        FakeTransport droppedSet = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] droppedBaseline = droppedSet.CurveSnapshot();
        PawnIoM1Backend droppedBackend = CreateBackend(pro, droppedSet);
        droppedBackend.Initialize();
        droppedSet.DropWriteCalls.Add(1);
        Throws<IOException>(() => droppedBackend.Set(M1Fan.Cpu, 20));
        Equal(2, droppedSet.WriteBatches.Count);
        SequenceEqual(droppedBaseline, droppedSet.CurveSnapshot());
        droppedBackend.Dispose();

        FakeTransport partialReset = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] resetBaseline = partialReset.CurveSnapshot();
        PawnIoM1Backend partialResetBackend = CreateBackend(pro, partialReset);
        partialResetBackend.Initialize();
        partialResetBackend.Set(M1Fan.Cpu, 20);
        partialResetBackend.Set(M1Fan.System, 30);
        partialReset.FailWriteAfterCounts[3] = 4;
        Throws<IOException>(() => partialResetBackend.Reset(M1Fan.Cpu));
        Equal<byte?>(null, partialResetBackend.CodeFor(M1Fan.Cpu));
        Equal<byte?>(null, partialResetBackend.CodeFor(M1Fan.System));
        False(partialResetBackend.NeedsRestore);
        SequenceEqual(resetBaseline, partialReset.CurveSnapshot());
        partialResetBackend.Dispose();
        Equal(4, partialReset.WriteBatches.Count);

        FakeTransport partialDispose = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] partialDisposeBaseline = partialDispose.CurveSnapshot();
        PawnIoM1Backend partialDisposeBackend = CreateBackend(pro, partialDispose);
        partialDisposeBackend.Initialize();
        partialDisposeBackend.Set(M1Fan.Cpu, 20);
        partialDispose.FailWriteAfterCounts[2] = 5;
        partialDisposeBackend.Dispose();
        Equal(3, partialDispose.WriteBatches.Count);
        SequenceEqual(partialDisposeBaseline, partialDispose.CurveSnapshot());
        True(partialDispose.Disposed);

        FakeTransport permanentDispose = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] permanentBaseline = permanentDispose.CurveSnapshot();
        PawnIoM1Backend permanentDisposeBackend = CreateBackend(pro, permanentDispose);
        permanentDisposeBackend.Initialize();
        permanentDisposeBackend.Set(M1Fan.Cpu, 20);
        permanentDispose.FailWriteAfterCounts[2] = 5;
        permanentDispose.DropWriteCalls.Add(3);
        Throws<IOException>(permanentDisposeBackend.Dispose);
        Equal(3, permanentDispose.WriteBatches.Count);
        False(permanentDispose.CurveSnapshot().SequenceEqual(permanentBaseline));
        True(permanentDispose.Disposed);
        True(permanentDisposeBackend.NeedsRestore);
    }

    private static void BackendOwnershipDrift()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        FakeTransport thresholdDrift = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend thresholdBackend = CreateBackend(pro, thresholdDrift);
        thresholdBackend.Initialize();
        thresholdDrift.SetByte(0x0641, 0xfe);
        Throws<InvalidOperationException>(() => thresholdBackend.Set(M1Fan.Cpu, 20));
        Equal(0, thresholdDrift.WriteBatches.Count);
        thresholdBackend.Dispose();
        Equal(0, thresholdDrift.WriteBatches.Count);

        FakeTransport ownedDrift = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend ownedBackend = CreateBackend(pro, ownedDrift);
        ownedBackend.Initialize();
        ownedBackend.Set(M1Fan.Cpu, 20);
        ownedDrift.SetByte(0x0640, 0xee);
        Throws<InvalidOperationException>(ownedBackend.Dispose);
        Equal(1, ownedDrift.WriteBatches.Count);
        True(ownedDrift.Disposed);

        // Firmware stock reset across Modern Standby / mode switch (0xb0 -> 0xb1)
        // is accepted cleanly during Set and during direct Dispose without overwriting.
        FakeTransport firmwareReset = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend resetBackend = CreateBackend(pro, firmwareReset);
        resetBackend.Initialize();
        resetBackend.Set(M1Fan.Cpu, 20);
        firmwareReset.SetStockMode(0xb1);
        byte[] alternateStock = firmwareReset.CurveSnapshot();
        Throws<InvalidOperationException>(() => resetBackend.Set(M1Fan.Cpu, 25));
        Equal(1, firmwareReset.WriteBatches.Count);
        False(resetBackend.NeedsRestore);
        resetBackend.Dispose();
        Equal(1, firmwareReset.WriteBatches.Count);
        SequenceEqual(alternateStock, firmwareReset.CurveSnapshot());

        FakeTransport directDisposeReset = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend directDisposeBackend = CreateBackend(pro, directDisposeReset);
        directDisposeBackend.Initialize();
        directDisposeBackend.Set(M1Fan.Cpu, 20);
        directDisposeReset.SetStockMode(0xb1);
        directDisposeBackend.Dispose();
        Equal(1, directDisposeReset.WriteBatches.Count);
        SequenceEqual(alternateStock, directDisposeReset.CurveSnapshot());
        True(directDisposeReset.Disposed);
    }

    private static void PluginControlFailureShutdown()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Pro;
        FakeTransport transport = FakeTransport.ForProfile(profile, 1, 0x07);
        byte[] baseline = transport.CurveSnapshot();
        PawnIoM1Backend backend = CreateBackend(profile, transport);
        FakeLogger logger = new();
        M1ProPlugin plugin = new(
            profile,
            () => profile.Board,
            _ => backend,
            logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            IPluginControlSensor2 cpu = FindControl(
                container,
                "minisforum.m1pro.arbsc.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container,
                "minisforum.m1pro.arbsc.system-control");
            transport.FailExclusiveCleanupCalls.Add(transport.ExclusiveCalls + 1);

            cpu.Set(50f);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
            Equal<float?>(null, FindFan(container, profile, "fan1").Value);
            Equal<float?>(null, FindFan(container, profile, "fan2").Value);
            Equal<float?>(null, FindTemperature(
                container,
                profile,
                "cpu-temperature").Value);
            Equal<float?>(null, FindTemperature(
                container,
                profile,
                "system-temperature").Value);
            Equal(2, transport.WriteBatches.Count);
            SequenceEqual(baseline, transport.CurveSnapshot());
            True(transport.Disposed);
            True(logger.Messages.Any(message => message.Contains(
                "M1 Pro CPU Fan Control failed: Expected exclusive cleanup failure")));
            True(logger.Messages.Any(message => message.Contains("Refresh FanControl")));
            False(logger.Messages.Any(message => message.Contains("unplug AC power")));

            int writes = transport.WriteBatches.Count;
            int logged = logger.Messages.Count;
            system.Set(50f);
            cpu.Set(float.NaN);
            system.Reset();
            Equal(writes, transport.WriteBatches.Count);
            Equal(logged, logger.Messages.Count);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
        }
        finally
        {
            plugin.Close();
        }

        FakeBackend failedRestoration = new(new M1Telemetry(3_000, 1_900, 60, 40))
        {
            DisposeException = new InvalidOperationException(
                "expected restoration failure"),
        };
        failedRestoration.FailSetCalls.Add(1);
        FakeLogger failedLogger = new();
        M1ProPlugin failedPlugin = CreatePlugin(profile, failedRestoration, failedLogger);
        FakeContainer failedContainer = new();
        try
        {
            failedPlugin.Initialize();
            failedPlugin.Load(failedContainer);
            IPluginControlSensor2 cpu = FindControl(
                failedContainer,
                "minisforum.m1pro.arbsc.cpu-control");
            cpu.Set(50f);
            Equal<float?>(null, cpu.Value);
            Equal(1, failedRestoration.DisposeCalls);
            True(failedLogger.Messages.Any(message =>
                message.Contains("failed: Expected fake set failure 1.")));
            True(failedLogger.Messages.Any(message => message.Contains("unplug AC power")));
            False(failedLogger.Messages.Any(message =>
                message.Contains("Refresh FanControl to resume control")));
            Throws<InvalidOperationException>(failedPlugin.Initialize);
            Equal(1, failedRestoration.InitializeCalls);
            Equal(2, failedRestoration.DisposeCalls);
        }
        finally
        {
            failedPlugin.Close();
        }

        // Transient restoration failure recovers on subsequent Initialize() retry.
        FakeTransport sharedEc = FakeTransport.ForProfile(profile, 1, 0x07);
        byte[] sharedBaseline = sharedEc.CurveSnapshot();
        bool failNextOpen = true;
        FakeLogger retryLogger = new();
        M1ProPlugin retryPlugin = new(
            profile,
            () => profile.Board,
            selected => new PawnIoM1Backend(
                selected,
                () => selected.Board,
                slot =>
                {
                    FakeTransport view = sharedEc.ShareMemory(slot);
                    if (failNextOpen)
                    {
                        failNextOpen = false;
                        // Fail both restore attempts during first Close().
                        view.DropWriteCalls.Add(2);
                        view.DropWriteCalls.Add(3);
                    }
                    return view;
                }),
            retryLogger);
        FakeContainer retryContainer = new();
        try
        {
            retryPlugin.Initialize();
            retryPlugin.Load(retryContainer);
            IPluginControlSensor2 cpu = FindControl(
                retryContainer,
                "minisforum.m1pro.arbsc.cpu-control");
            cpu.Set(50f);
            retryPlugin.Close();
            False(sharedEc.CurveSnapshot().SequenceEqual(sharedBaseline));
            True(retryLogger.Messages.Any(message =>
                message.Contains("baseline restoration failed")));

            retryPlugin.Initialize();
            SequenceEqual(sharedBaseline, sharedEc.CurveSnapshot());
            True(retryLogger.Messages.Any(message =>
                message.Contains("pending baseline restoration succeeded")));
        }
        finally
        {
            retryPlugin.Close();
        }

        // Graceful ProcessExit hook restores active manual curve and is idempotent.
        FakeTransport exitTransport = FakeTransport.ForProfile(profile, 1, 0x07);
        byte[] exitBaseline = exitTransport.CurveSnapshot();
        M1ProPlugin exitPlugin = new(
            profile,
            () => profile.Board,
            _ => CreateBackend(profile, exitTransport));
        FakeContainer exitContainer = new();
        exitPlugin.Initialize();
        exitPlugin.Load(exitContainer);
        FindControl(exitContainer, "minisforum.m1pro.arbsc.cpu-control").Set(50f);
        False(exitTransport.CurveSnapshot().SequenceEqual(exitBaseline));
        exitPlugin.HandleProcessExit();
        SequenceEqual(exitBaseline, exitTransport.CurveSnapshot());
        True(exitTransport.Disposed);
        exitPlugin.HandleProcessExit();

        FakeBackend racing = new(new M1Telemetry(3_000, 1_900, 60, 40));
        racing.FailSetCalls.Add(1);
        FakeLogger racingLogger = new();
        M1ProPlugin racingPlugin = CreatePlugin(profile, racing, racingLogger);
        FakeContainer racingContainer = new();
        try
        {
            racingPlugin.Initialize();
            racingPlugin.Load(racingContainer);
            racing.BeforeSet = racingPlugin.Close;
            IPluginControlSensor2 cpu = FindControl(
                racingContainer,
                "minisforum.m1pro.arbsc.cpu-control");
            cpu.Set(50f);
            Equal<float?>(null, cpu.Value);
            Equal(1, racing.DisposeCalls);
            False(racingLogger.Messages.Any(message => message.Contains("failed")));
            False(racingLogger.Messages.Any(message => message.Contains("Refresh FanControl")));
        }
        finally
        {
            racingPlugin.Close();
        }
    }

    private static void M1ProPluginCompatibility()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Pro;
        FakeBackend backend = new(
            new M1Telemetry(3_000, 1_900, 62, 41),
            new M1Telemetry(3_100, 2_000, 63, 42));
        FakeLogger logger = new();
        M1ProPlugin plugin = CreatePlugin(profile, backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            AssertPluginMetadata(
                plugin,
                container,
                "Minisforum M1 Pro (ARBSC)",
                "minisforum.m1pro.arbsc",
                "M1 Pro");

            IPluginControlSensor2 cpu = FindControl(
                container,
                "minisforum.m1pro.arbsc.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container,
                "minisforum.m1pro.arbsc.system-control");
            cpu.Set(10f);
            system.Set(12f);
            SequenceEqual(
                [(M1Fan.Cpu, (byte)0), (M1Fan.System, (byte)0)],
                backend.SetCalls);
            Equal<float?>(5 * 100f / 51f, cpu.Value);
            Equal<float?>(6 * 100f / 51f, system.Value);

            cpu.Set(50f);
            Equal((M1Fan.Cpu, (byte)26), backend.SetCalls[^1]);
            Equal<float?>(26 * 100f / 51f, cpu.Value);
            plugin.Update();
            Equal<float?>(3_100f, FindFan(container, profile, "fan1").Value);
            Equal<float?>(42f, FindTemperature(container, profile, "system-temperature").Value);
            True(logger.Messages.Any(message => message.Contains("initialized")));
            False(logger.Messages.Any(message => message.Contains("experimental")));

            backend.FailResetCalls.Add(1);
            cpu.Reset();
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
            Equal(1, backend.DisposeCalls);
            True(logger.Messages.Any(message => message.Contains(
                "M1 Pro CPU Fan Control failed: Expected fake reset failure 1.")));
        }
        finally
        {
            plugin.Close();
        }
        Equal(1, backend.DisposeCalls);
    }

    private static void M1LitePluginMetadataAndControls()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Lite;
        FakeBackend backend = new(new M1Telemetry(2_500, 1_700, 55, 38));
        FakeLogger logger = new();
        M1ProPlugin plugin = CreatePlugin(profile, backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            AssertPluginMetadata(
                plugin,
                container,
                "Minisforum M1 Lite (MTBSI)",
                "minisforum.m1lite.mtbsi",
                "M1 Lite");
            Equal(2, container.FanSensors.Count);
            Equal(2, container.TempSensors.Count);
            Equal(2, container.ControlSensors.Count);

            IPluginControlSensor2 cpu = FindControl(
                container,
                "minisforum.m1lite.mtbsi.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container,
                "minisforum.m1lite.mtbsi.system-control");
            cpu.Set(1f);
            system.Set(0f);
            Equal<float?>(100f / 51f, cpu.Value);
            Equal<float?>(0f, system.Value);
            cpu.Set(100f);
            system.Set(100f);
            SequenceEqual(
                [
                    (M1Fan.Cpu, (byte)1),
                    (M1Fan.System, (byte)0),
                    (M1Fan.Cpu, (byte)51),
                    (M1Fan.System, (byte)51),
                ],
                backend.SetCalls);
            Equal<float?>(100f, cpu.Value);
            Equal<float?>(100f, system.Value);

            backend.FailSetCalls.Add(5);
            system.Set(50f);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
            Equal(1, backend.DisposeCalls);
            True(logger.Messages.Any(message => message.Contains(
                "M1 Lite System Fan Control failed: Expected fake set failure 5.")));

            int calls = backend.SetCalls.Count;
            int logged = logger.Messages.Count;
            cpu.Set(100f);
            cpu.Set(float.NaN);
            system.Set(-1f);
            Equal(calls, backend.SetCalls.Count);
            Equal(logged, logger.Messages.Count);
            Equal<float?>(null, cpu.Value);
            True(logger.Messages.Any(message => message.Contains("experimental")));

            system.Reset();
            SequenceEqual<M1Fan>([], backend.ResetCalls);
            Equal<float?>(null, system.Value);
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void PluginInvalidCommands()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Pro;
        FakeBackend backend = new(new M1Telemetry(3_000, 1_900, 60, 40));
        FakeLogger logger = new();
        M1ProPlugin plugin = CreatePlugin(profile, backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            IPluginControlSensor2 cpu = FindControl(
                container,
                "minisforum.m1pro.arbsc.cpu-control");
            IPluginControlSensor2 system = FindControl(
                container,
                "minisforum.m1pro.arbsc.system-control");

            cpu.Set(50f);
            system.Set(50f);
            cpu.Set(float.NaN);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(26 * 100f / 51f, system.Value);
            SequenceEqual([M1Fan.Cpu], backend.ResetCalls);

            cpu.Set(float.PositiveInfinity);
            cpu.Set(-0.01f);
            Equal(1, logger.Messages.Count(message => message.Contains(
                "M1 Pro CPU Fan Control received invalid command NaN")));

            cpu.Set(100f);
            Equal<float?>(100f, cpu.Value);
            cpu.Set(100.01f);
            Equal<float?>(null, cpu.Value);
            Equal(2, logger.Messages.Count(message => message.Contains("invalid command")));
            True(backend.ResetCalls.All(fan => fan == M1Fan.Cpu));
            SequenceEqual(
                [(M1Fan.Cpu, (byte)26), (M1Fan.System, (byte)26), (M1Fan.Cpu, (byte)51)],
                backend.SetCalls);
            Equal(0, backend.DisposeCalls);

            plugin.Update();
            Equal<float?>(3_000f, FindFan(container, profile, "fan1").Value);
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void PluginGatesAndLifecycleFailures()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Lite;
        FakeBackend unused = new(new M1Telemetry(1, 2, 3, 4));
        int factoryCalls = 0;
        M1ProPlugin wrongBoard = new(
            profile,
            static () => "ARBSC",
            _ =>
            {
                factoryCalls++;
                return unused;
            });
        Throws<PlatformNotSupportedException>(wrongBoard.Initialize);
        Equal(0, factoryCalls);
        Equal(0, unused.InitializeCalls);

        FakeBackend initializeFailure = new(new M1Telemetry(1, 2, 3, 4))
        {
            InitializeException = new IOException("expected initialization failure"),
        };
        M1ProPlugin initializePlugin = CreatePlugin(profile, initializeFailure);
        Throws<IOException>(initializePlugin.Initialize);
        Equal(1, initializeFailure.DisposeCalls);

        FakeBackend readFailure = new(new M1Telemetry(1, 2, 3, 4));
        readFailure.FailReadCalls.Add(1);
        M1ProPlugin readPlugin = CreatePlugin(profile, readFailure);
        Throws<IOException>(readPlugin.Initialize);
        Equal(1, readFailure.DisposeCalls);

        FakeBackend restoreFailure = new(new M1Telemetry(1, 2, 3, 4))
        {
            DisposeException = new IOException("expected permanent restore failure"),
        };
        FakeLogger logger = new();
        M1ProPlugin restorePlugin = CreatePlugin(profile, restoreFailure, logger);
        restorePlugin.Initialize();
        restorePlugin.Close();
        Equal(1, restoreFailure.DisposeCalls);
        Throws<InvalidOperationException>(restorePlugin.Initialize);
        Equal(1, restoreFailure.InitializeCalls);
        Equal(2, restoreFailure.DisposeCalls);
        True(logger.Messages.Any(message => message.Contains("unplug AC power")));
    }

    private static void PluginTelemetryInvalidationAndRecovery()
    {
        M1ModelProfile profile = M1ModelProfiles.M1Lite;
        FakeBackend backend = new(
            new M1Telemetry(2_800, 1_700, 60, 39),
            new M1Telemetry(2_900, 1_800, 61, 40));
        backend.FailReadCalls.Add(2);
        FakeLogger logger = new();
        M1ProPlugin plugin = CreatePlugin(profile, backend, logger);
        FakeContainer container = new();

        try
        {
            plugin.Initialize();
            plugin.Load(container);
            IPluginControlSensor2 cpu = FindControl(
                container,
                "minisforum.m1lite.mtbsi.cpu-control");
            cpu.Set(50f);
            float? confirmed = cpu.Value;

            plugin.Update();
            Equal<float?>(null, FindFan(container, profile, "fan1").Value);
            Equal<float?>(null, FindFan(container, profile, "fan2").Value);
            Equal<float?>(null, FindTemperature(
                container,
                profile,
                "cpu-temperature").Value);
            Equal<float?>(null, FindTemperature(
                container,
                profile,
                "system-temperature").Value);
            Equal(confirmed, cpu.Value);
            True(logger.Messages.Any(message =>
                message.Contains("telemetry read failed", StringComparison.Ordinal)));

            plugin.Update();
            Equal<float?>(2_900f, FindFan(container, profile, "fan1").Value);
            Equal<float?>(1_800f, FindFan(container, profile, "fan2").Value);
            Equal<float?>(61f, FindTemperature(
                container,
                profile,
                "cpu-temperature").Value);
            Equal<float?>(40f, FindTemperature(
                container,
                profile,
                "system-temperature").Value);
            Equal(confirmed, cpu.Value);
        }
        finally
        {
            plugin.Close();
        }
    }

    private static void AssertLiteStartupRejected(Action<FakeTransport> mutate)
    {
        M1ModelProfile lite = M1ModelProfiles.M1Lite;
        FakeTransport selected = FakeTransport.ForProfile(lite, 1, 0x09);
        mutate(selected);
        FakeTransport other = FakeTransport.WrongController(0);
        PawnIoM1Backend backend = CreateBackend(lite, selected, other);
        Throws<Exception>(backend.Initialize);
        Equal(0, selected.WriteBatches.Count);
        Equal(0, other.WriteBatches.Count);
        True(selected.Disposed);
        True(other.Disposed);
    }

    private static PawnIoM1Backend CreateBackend(
        M1ModelProfile profile,
        params FakeTransport[] transports)
    {
        Dictionary<byte, FakeTransport> bySlot = transports.ToDictionary(
            transport => transport.Slot);
        return new PawnIoM1Backend(
            profile,
            () => profile.Board,
            slot => bySlot.TryGetValue(slot, out FakeTransport? transport)
                ? transport
                : throw new IOException($"No fake transport for slot {slot}."));
    }

    private static M1ProPlugin CreatePlugin(
        M1ModelProfile profile,
        FakeBackend backend,
        FakeLogger? logger = null) => new(
            profile,
            () => profile.Board,
            _ => backend,
            logger);

    private static void AssertPluginMetadata(
        M1ProPlugin plugin,
        FakeContainer container,
        string expectedName,
        string prefix,
        string model)
    {
        Equal(expectedName, plugin.Name);
        SequenceEqual(
            [$"{prefix}.fan1", $"{prefix}.fan2"],
            container.FanSensors.Select(sensor => sensor.Id));
        SequenceEqual(
            [$"{prefix}.cpu-temperature", $"{prefix}.system-temperature"],
            container.TempSensors.Select(sensor => sensor.Id));
        SequenceEqual(
            [$"{prefix}.cpu-control", $"{prefix}.system-control"],
            container.ControlSensors.Select(sensor => sensor.Id));
        Equal($"{model} CPU Fan", container.FanSensors[0].Name);
        Equal($"{model} System Fan", container.FanSensors[1].Name);
        Equal($"{model} EC CPU", container.TempSensors[0].Name);
        Equal($"{model} EC System", container.TempSensors[1].Name);

        IPluginControlSensor2 cpu = (IPluginControlSensor2)container.ControlSensors[0];
        IPluginControlSensor2 system = (IPluginControlSensor2)container.ControlSensors[1];
        Equal($"{model} CPU Fan Control", cpu.Name);
        Equal($"{model} System Fan Control", system.Name);
        Equal($"{expectedName}/{prefix}.fan1", cpu.PairedFanSensorId);
        Equal($"{expectedName}/{prefix}.fan2", system.PairedFanSensorId);
    }

    private static void AssertRequest(
        FanControlRequest request,
        byte expectedCode,
        float expectedPercentage)
    {
        Equal(expectedCode, request.AppliedCode);
        Equal(expectedPercentage, request.ReportedPercentage);
    }

    private static string Sha256(byte[] values) =>
        Convert.ToHexString(SHA256.HashData(values)).ToLowerInvariant();

    private static IPluginControlSensor2 FindControl(
        FakeContainer container,
        string id) =>
        (IPluginControlSensor2)container.ControlSensors.Single(sensor => sensor.Id == id);

    private static IPluginSensor FindFan(
        FakeContainer container,
        M1ModelProfile profile,
        string suffix) =>
        container.FanSensors.Single(sensor =>
            sensor.Id == $"{profile.SensorIdPrefix}.{suffix}");

    private static IPluginSensor FindTemperature(
        FakeContainer container,
        M1ModelProfile profile,
        string suffix) =>
        container.TempSensors.Single(sensor =>
            sensor.Id == $"{profile.SensorIdPrefix}.{suffix}");

    private static void True(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool condition) => True(!condition);

    private static void Same(object expected, object actual)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new InvalidOperationException("Expected the same object instance.");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected {expected}; found {actual}.");
        }
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
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

    private static T Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T exception)
        {
            return exception;
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

    private sealed class FakePawnIoExecutor(params ulong[] readValues) : IPawnIoExecutor
    {
        private readonly Queue<ulong> readValues = new(readValues);

        internal List<PawnIoCall> Calls { get; } = [];

        internal byte[] LoadedModule { get; private set; } = [];

        internal bool FailSelectedReadOnce { get; init; }

        internal bool FailSelectSlotOnce { get; init; }

        internal bool FailOuterReadOnce { get; init; }

        internal bool FailNestedParkOnce { get; init; }

        internal bool FailOuterParkOnce { get; init; }

        internal bool Disposed { get; private set; }

        private bool failedSelectedRead;
        private bool failedSelectSlot;
        private bool failedOuterRead;
        private bool failedNestedPark;
        private bool failedOuterPark;
        private bool completedSelectedRead;

        public void OpenAndLoad(byte[] module)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            LoadedModule = (byte[])module.Clone();
        }

        public ulong[] Execute(string name, ulong[] input, int outputCount)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            Calls.Add(new PawnIoCall(name, (ulong[])input.Clone(), outputCount));
            if (FailSelectSlotOnce &&
                !failedSelectSlot &&
                name == "ioctl_select_slot")
            {
                failedSelectSlot = true;
                throw new IOException("Expected slot-selection failure.");
            }
            if (FailOuterReadOnce &&
                !failedOuterRead &&
                name == "ioctl_superio_inb" &&
                input.SequenceEqual([0x20UL]))
            {
                failedOuterRead = true;
                throw new IOException("Expected outer-identity read failure.");
            }
            if (FailSelectedReadOnce &&
                !failedSelectedRead &&
                name == "ioctl_superio_inb" &&
                input.SequenceEqual([0x2fUL]))
            {
                failedSelectedRead = true;
                completedSelectedRead = true;
                throw new IOException("Expected selected-register read failure.");
            }
            if (FailNestedParkOnce &&
                !failedNestedPark &&
                completedSelectedRead &&
                name == "ioctl_superio_outb" &&
                input.SequenceEqual([0x2eUL, 0x10UL]))
            {
                failedNestedPark = true;
                throw new IOException("Expected nested-index park failure.");
            }
            if (FailOuterParkOnce &&
                !failedOuterPark &&
                name == "ioctl_pio_outb")
            {
                failedOuterPark = true;
                throw new IOException("Expected outer-index park failure.");
            }
            if (outputCount == 0)
            {
                return [];
            }
            if (outputCount != 1 || !readValues.TryDequeue(out ulong value))
            {
                throw new InvalidOperationException("Unexpected fake PawnIO output request.");
            }
            if (name == "ioctl_superio_inb" && input.SequenceEqual([0x2fUL]))
            {
                completedSelectedRead = true;
            }
            return [value];
        }

        public void Dispose() => Disposed = true;
    }

    private sealed record PawnIoCall(string Name, ulong[] Input, int OutputCount);

    private sealed class FakeTransport : IM1Transport, IM1TransportAccess
    {
        private readonly Dictionary<ushort, byte> memory;
        private readonly M1ModelProfile? profile;
        private int readCalls;
        private int writeCalls;

        private FakeTransport(
            byte slot,
            byte[] outerIdentity,
            M1ModelProfile? profile = null,
            Dictionary<ushort, byte>? sharedMemory = null)
        {
            Slot = slot;
            OuterIdentity = outerIdentity;
            this.profile = profile;
            memory = sharedMemory ?? [];
        }

        internal byte Slot { get; }

        internal byte[] OuterIdentity { get; }

        internal int OuterReadCalls { get; private set; }

        internal bool FailOuterRead { get; set; }

        internal List<ushort[]> ReadBatches { get; } = [];

        internal List<EcWrite[]> WriteBatches { get; } = [];

        internal Queue<byte[]> RuntimeSamples { get; } = [];

        internal Queue<byte[]> TelemetrySamples { get; } = [];

        internal Queue<byte[]> CpuRpmSamples { get; } = [];

        internal Queue<byte[]> SystemRpmSamples { get; } = [];

        internal HashSet<int> FailWriteCalls { get; } = [];

        internal Dictionary<int, int> FailWriteAfterCounts { get; } = [];

        internal HashSet<int> DropWriteCalls { get; } = [];

        internal HashSet<int> FailReadCalls { get; } = [];

        internal HashSet<int> FailExclusiveCleanupCalls { get; } = [];

        internal int ExclusiveCalls { get; private set; }

        internal bool Disposed { get; private set; }

        internal static FakeTransport WrongController(byte slot) =>
            new(slot, [0x12, 0x34, 0x56]);

        internal static FakeTransport ForProfile(
            M1ModelProfile profile,
            byte slot,
            byte revision)
        {
            FakeTransport result = new(slot, [0x55, 0x71, revision], profile);
            result.SetBytesRaw(0x2000, [0x55, 0x71, revision]);
            result.SetByte(0x200d, 0xcb);
            result.SetByte(M1EcLayout.ModeAddress, 0xb0);
            byte[] curve = profile.StockCurve(0xb0) ??
                throw new InvalidOperationException();
            result.SetBytesRaw(M1EcLayout.CurveBlockStart, curve);
            result.SetBytesRaw(0x0420, [0x00, 0x00, 0x03, 0xe8]);
            result.SetBytesRaw(0x0428, [10, 10]);
            result.SetBytesRaw(0x0432, [0, 10]);
            result.SetBytesRaw(0x0438, [0x0b, 0xb8, 0x07, 0x6c]);

            if (ReferenceEquals(profile, M1ModelProfiles.M1Lite))
            {
                result.SetBytesRaw(0x0200, [0x26, 0x02, 0x03, 0x16, 0x38, 0x01]);
                result.SetBytesRaw(0x0210, "2026/02/03$"u8);
                result.SetBytesRaw(0x0220, "16:38:01$"u8);
            }
            return result;
        }

        internal FakeTransport ShareMemory(byte slot) =>
            new(slot, (byte[])OuterIdentity.Clone(), profile, memory);

        public byte[] ReadOuterIdentity()
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            OuterReadCalls++;
            if (FailOuterRead)
            {
                throw new IOException("Expected fake outer-identity read failure.");
            }
            return (byte[])OuterIdentity.Clone();
        }

        public T RunExclusive<T>(Func<IM1TransportAccess, T> action)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            ExclusiveCalls++;
            T result = action(this);
            if (FailExclusiveCleanupCalls.Contains(ExclusiveCalls))
            {
                throw new IOException(
                    $"Expected exclusive cleanup failure {ExclusiveCalls}.");
            }
            return result;
        }

        public byte[] Read(ushort[] addresses)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            readCalls++;
            ReadBatches.Add((ushort[])addresses.Clone());
            if (FailReadCalls.Contains(readCalls))
            {
                throw new IOException($"Expected fake read failure {readCalls}.");
            }
            if (addresses.SequenceEqual(M1EcLayout.RuntimeSampleAddresses) &&
                RuntimeSamples.TryDequeue(out byte[]? sample))
            {
                return (byte[])sample.Clone();
            }
            if (addresses.SequenceEqual(M1EcLayout.TelemetryAddresses) &&
                TelemetrySamples.TryDequeue(out byte[]? telemetry))
            {
                return (byte[])telemetry.Clone();
            }
            if (addresses.SequenceEqual(M1EcLayout.CpuRpmAddresses) &&
                CpuRpmSamples.TryDequeue(out byte[]? cpuRpm))
            {
                return (byte[])cpuRpm.Clone();
            }
            if (addresses.SequenceEqual(M1EcLayout.SystemRpmAddresses) &&
                SystemRpmSamples.TryDequeue(out byte[]? systemRpm))
            {
                return (byte[])systemRpm.Clone();
            }
            return addresses.Select(ByteAt).ToArray();
        }

        public void Write(EcWrite[] writes)
        {
            ObjectDisposedException.ThrowIf(Disposed, this);
            writeCalls++;
            EcWrite[] copy = (EcWrite[])writes.Clone();
            WriteBatches.Add(copy);
            if (DropWriteCalls.Contains(writeCalls))
            {
                return;
            }
            int applyCount = FailWriteAfterCounts.TryGetValue(
                writeCalls,
                out int partialCount)
                    ? partialCount
                    : copy.Length;
            foreach (EcWrite write in copy.Take(applyCount))
            {
                memory[write.Address] = write.Value;
            }
            if (FailWriteAfterCounts.ContainsKey(writeCalls))
            {
                throw new IOException(
                    $"Expected partial fake write failure {writeCalls} after " +
                    $"{applyCount} values.");
            }
            if (FailWriteCalls.Contains(writeCalls))
            {
                throw new IOException($"Expected fake write failure {writeCalls}.");
            }
        }

        public void Dispose() => Disposed = true;

        internal byte[] CurveSnapshot() => M1EcLayout.CurveBlockAddresses
            .Select(ByteAt)
            .ToArray();

        internal byte ByteAt(ushort address) => memory.TryGetValue(address, out byte value)
            ? value
            : (byte)0;

        internal void SetByte(ushort address, byte value) => memory[address] = value;

        internal void SetStockMode(byte mode)
        {
            byte[] curve = profile?.StockCurve(mode) ??
                throw new ArgumentOutOfRangeException(nameof(mode));
            SetByte(M1EcLayout.ModeAddress, mode);
            SetBytesRaw(M1EcLayout.CurveBlockStart, curve);
        }

        internal void EnqueueRuntimeSamples(params byte[][] samples)
        {
            foreach (byte[] sample in samples)
            {
                RuntimeSamples.Enqueue((byte[])sample.Clone());
            }
        }

        internal void SetBytesRaw(ushort start, ReadOnlySpan<byte> values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                memory[(ushort)(start + index)] = values[index];
            }
        }
    }

    private sealed class FakeBackend(params M1Telemetry[] samples) : IM1Backend
    {
        private readonly Queue<M1Telemetry> samples = new(samples);
        private M1Telemetry? lastSample;

        public string? StartupSummary { get; init; }

        internal Exception? InitializeException { get; init; }

        internal Exception? DisposeException { get; init; }

        internal Action? BeforeSet { get; set; }

        internal HashSet<int> FailReadCalls { get; } = [];

        internal HashSet<int> FailSetCalls { get; } = [];

        internal HashSet<int> FailResetCalls { get; } = [];

        internal int InitializeCalls { get; private set; }

        internal int ReadCalls { get; private set; }

        internal List<(M1Fan Fan, byte Code)> SetCalls { get; } = [];

        internal List<M1Fan> ResetCalls { get; } = [];

        internal int DisposeCalls { get; private set; }

        public void Initialize()
        {
            InitializeCalls++;
            if (InitializeException is not null)
            {
                throw InitializeException;
            }
        }

        public M1Telemetry ReadTelemetry()
        {
            ReadCalls++;
            if (FailReadCalls.Contains(ReadCalls))
            {
                throw new IOException($"Expected fake read failure {ReadCalls}.");
            }
            if (samples.TryDequeue(out M1Telemetry? sample))
            {
                lastSample = sample;
            }
            return lastSample ?? throw new InvalidOperationException("No fake telemetry.");
        }

        public void Set(M1Fan fan, byte code)
        {
            BeforeSet?.Invoke();
            int call = SetCalls.Count + 1;
            if (FailSetCalls.Contains(call))
            {
                throw new IOException($"Expected fake set failure {call}.");
            }
            SetCalls.Add((fan, code));
        }

        public void Reset(M1Fan fan)
        {
            int call = ResetCalls.Count + 1;
            if (FailResetCalls.Contains(call))
            {
                throw new IOException($"Expected fake reset failure {call}.");
            }
            ResetCalls.Add(fan);
        }

        public void Dispose()
        {
            DisposeCalls++;
            if (DisposeException is not null)
            {
                throw DisposeException;
            }
        }
    }
}
