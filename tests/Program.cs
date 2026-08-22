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
            ("MTBSI firmware fingerprints", MtbsiFirmwareFingerprints),
            ("shared curve ownership layout", SharedCurveOwnershipLayout),
            ("telemetry decoding", TelemetryDecoding),
            ("PawnIO native slot sequences", PawnIoNativeSlotSequences),
            ("outer controller discovery", OuterControllerDiscovery),
            ("MTBSI startup gates", MtbsiStartupGates),
            ("backend transactions and bounds", BackendTransactionsAndBounds),
            ("M1 Lite successful transactions", M1LiteSuccessfulTransactions),
            ("backend verified recovery", BackendVerifiedRecovery),
            ("backend ownership drift", BackendOwnershipDrift),
            ("M1 Pro plugin compatibility", M1ProPluginCompatibility),
            ("M1 Lite plugin metadata and controls", M1LitePluginMetadataAndControls),
            ("plugin gates and lifecycle failures", PluginGatesAndLifecycleFailures),
            ("plugin update error retention", PluginUpdateErrorRetention),
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
        Equal("Minisforum M1 Lite (MTBSI)", M1ModelProfiles.M1Lite.PluginName);
        Equal("minisforum.m1lite.mtbsi", M1ModelProfiles.M1Lite.SensorIdPrefix);
        False(M1ModelProfiles.M1Lite.HardwareValidated);

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

    private static void MtbsiFirmwareFingerprints()
    {
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

        EcWrite[] systemManual = M1EcLayout.ManualWrites(M1Fan.System, 41);
        Equal(14, systemManual.Length);
        SequenceEqual(
            Enumerable.Range(0x0678, 7).Select(address => (ushort)address),
            systemManual.Take(7).Select(write => write.Address));

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
        M1Telemetry telemetry = M1TelemetryDecoder.Decode(
            [0x0b, 0xb8, 0x07, 0x6c, 63, 42]);
        Equal(3_000, telemetry.CpuFanRpm);
        Equal(1_900, telemetry.SystemFanRpm);
        Equal(63, telemetry.CpuTemperatureC);
        Equal(42, telemetry.SystemTemperatureC);
        Throws<ArgumentException>(() => M1TelemetryDecoder.Decode([0, 1, 2]));

        M1ModelProfiles.M1Lite.AssertTelemetry(telemetry);
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Lite.AssertTelemetry(new M1Telemetry(10_001, 0, 20, 20)));
        Throws<InvalidDataException>(() =>
            M1ModelProfiles.M1Lite.AssertTelemetry(new M1Telemetry(0, 0, 126, 20)));
    }

    private static void PawnIoNativeSlotSequences()
    {
        AssertPawnIoSlot(0, 0x2e);
        AssertPawnIoSlot(1, 0x4e);

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
                byte[] result = access.Read([0x1234]);
                access.Write([new EcWrite(0xabcd, 0xef)]);
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
                ("ioctl_superio_outb", [0x2f, 0x12], 0),
                ("ioctl_superio_outb", [0x2e, 0x10], 0),
                ("ioctl_superio_outb", [0x2f, 0x34], 0),
                ("ioctl_superio_outb", [0x2e, 0x12], 0),
                ("ioctl_superio_inb", [0x2f], 1),
                ("ioctl_superio_outb", [0x2e, 0x11], 0),
                ("ioctl_superio_outb", [0x2f, 0xab], 0),
                ("ioctl_superio_outb", [0x2e, 0x10], 0),
                ("ioctl_superio_outb", [0x2f, 0xcd], 0),
                ("ioctl_superio_outb", [0x2e, 0x12], 0),
                ("ioctl_superio_outb", [0x2f, 0xef], 0),
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

    private static void BackendTransactionsAndBounds()
    {
        M1ModelProfile pro = M1ModelProfiles.M1Pro;
        foreach (byte stockMode in new byte[] { 0xb1, 0xb2 })
        {
            FakeTransport stockTransport = FakeTransport.ForProfile(pro, 1, 0x07);
            stockTransport.SetStockMode(stockMode);
            PawnIoM1Backend stockBackend = CreateBackend(pro, stockTransport);
            stockBackend.Initialize();
            stockBackend.Dispose();
            Equal(0, stockTransport.WriteBatches.Count);
        }

        FakeTransport transport = FakeTransport.ForProfile(pro, 1, 0x07);
        byte[] baselineCurve = transport.CurveSnapshot();
        byte[] baselineOwned = M1EcLayout.CaptureOwned(baselineCurve);
        PawnIoM1Backend backend = CreateBackend(pro, transport);
        backend.Initialize();
        Equal(0, transport.WriteBatches.Count);
        False(backend.NeedsRestore);

        backend.Set(M1Fan.Cpu, 13);
        Equal<byte?>((byte)13, backend.CodeFor(M1Fan.Cpu));
        SequenceEqual(M1EcLayout.ManualWrites(M1Fan.Cpu, 13), transport.WriteBatches[0]);
        True(backend.NeedsRestore);
        backend.Set(M1Fan.Cpu, 13);
        Equal(1, transport.WriteBatches.Count);

        backend.Set(M1Fan.System, 51);
        Equal<byte?>((byte)51, backend.CodeFor(M1Fan.System));
        SequenceEqual(M1EcLayout.ManualWrites(M1Fan.System, 51), transport.WriteBatches[1]);

        backend.Reset(M1Fan.Cpu);
        SequenceEqual(
            M1EcLayout.RestoreWrites(M1Fan.Cpu, baselineOwned),
            transport.WriteBatches[2]);
        Equal<byte?>(null, backend.CodeFor(M1Fan.Cpu));
        True(backend.NeedsRestore);

        M1Telemetry telemetry = backend.ReadTelemetry();
        Equal(3_000, telemetry.CpuFanRpm);
        Equal(1_900, telemetry.SystemFanRpm);

        backend.Dispose();
        SequenceEqual(
            M1EcLayout.AllRestoreWrites(baselineOwned),
            transport.WriteBatches[3]);
        SequenceEqual(baselineCurve, transport.CurveSnapshot());
        True(transport.Disposed);

        FakeTransport noOpTransport = FakeTransport.ForProfile(pro, 1, 0x07);
        PawnIoM1Backend noOp = CreateBackend(pro, noOpTransport);
        noOp.Initialize();
        noOp.Dispose();
        Equal(0, noOpTransport.WriteBatches.Count);

        FakeTransport staleManual = FakeTransport.ForProfile(pro, 1, 0x07);
        foreach (EcWrite write in M1EcLayout.ManualWrites(M1Fan.Cpu, 20))
        {
            staleManual.SetByte(write.Address, write.Value);
        }
        PawnIoM1Backend staleBackend = CreateBackend(pro, staleManual);
        Throws<PlatformNotSupportedException>(staleBackend.Initialize);
        Equal(0, staleManual.WriteBatches.Count);
        True(staleManual.Disposed);

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
                M1EcLayout.ManualWrites(M1Fan.Cpu, 0),
                transport.WriteBatches[0]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 0),
                transport.WriteBatches[1]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.Cpu, 1),
                transport.WriteBatches[2]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 1),
                transport.WriteBatches[3]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.Cpu, 51),
                transport.WriteBatches[4]);
            SequenceEqual(
                M1EcLayout.ManualWrites(M1Fan.System, 51),
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
            Throws<IOException>(cpu.Reset);
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);
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
            Throws<IOException>(() => system.Set(50f));
            Equal<float?>(null, cpu.Value);
            Equal<float?>(null, system.Value);

            int calls = backend.SetCalls.Count;
            Throws<ArgumentOutOfRangeException>(() => cpu.Set(float.NaN));
            Throws<ArgumentOutOfRangeException>(() => system.Set(-1f));
            Equal(calls, backend.SetCalls.Count);
            Equal<float?>(null, cpu.Value);
            True(logger.Messages.Any(message => message.Contains("experimental")));

            system.Reset();
            SequenceEqual([M1Fan.System], backend.ResetCalls);
            Equal<float?>(null, system.Value);
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
        True(logger.Messages.Any(message => message.Contains("Restart Windows")));
    }

    private static void PluginUpdateErrorRetention()
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
            Equal<float?>(2_800f, FindFan(container, profile, "fan1").Value);
            Equal(confirmed, cpu.Value);
            True(logger.Messages.Any(message =>
                message.Contains("telemetry read failed", StringComparison.Ordinal)));

            plugin.Update();
            Equal<float?>(2_900f, FindFan(container, profile, "fan1").Value);
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
        Throws<PlatformNotSupportedException>(backend.Initialize);
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
        Equal($"{model} EC CPU Temperature", container.TempSensors[0].Name);
        Equal($"{model} EC System Temperature", container.TempSensors[1].Name);

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

        internal bool Disposed { get; private set; }

        private bool failedSelectedRead;
        private bool failedSelectSlot;
        private bool failedOuterRead;
        private bool failedNestedPark;
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
        private readonly Dictionary<ushort, byte> memory = [];
        private int readCalls;
        private int writeCalls;

        private FakeTransport(byte slot, byte[] outerIdentity)
        {
            Slot = slot;
            OuterIdentity = outerIdentity;
        }

        internal byte Slot { get; }

        internal byte[] OuterIdentity { get; }

        internal int OuterReadCalls { get; private set; }

        internal bool FailOuterRead { get; set; }

        internal List<ushort[]> ReadBatches { get; } = [];

        internal List<EcWrite[]> WriteBatches { get; } = [];

        internal Queue<byte[]> RuntimeSamples { get; } = [];

        internal HashSet<int> FailWriteCalls { get; } = [];

        internal Dictionary<int, int> FailWriteAfterCounts { get; } = [];

        internal HashSet<int> DropWriteCalls { get; } = [];

        internal HashSet<int> FailReadCalls { get; } = [];

        internal bool Disposed { get; private set; }

        internal static FakeTransport WrongController(byte slot) =>
            new(slot, [0x12, 0x34, 0x56]);

        internal static FakeTransport ForProfile(
            M1ModelProfile profile,
            byte slot,
            byte revision)
        {
            FakeTransport result = new(slot, [0x55, 0x71, revision]);
            result.SetBytes(0x2000, [0x55, 0x71, revision]);
            result.SetByte(0x200d, 0xcb);
            result.SetByte(M1EcLayout.ModeAddress, 0xb0);
            byte[] curve = M1ModelProfiles.M1Lite.StockCurve(0xb0) ??
                throw new InvalidOperationException();
            result.SetBytes(M1EcLayout.CurveBlockStart, curve);
            result.SetBytes(0x0420, [0x00, 0x00, 0x03, 0xe8]);
            result.SetBytes(0x0428, [10, 10]);
            result.SetBytes(0x0432, [0, 10]);
            result.SetBytes(0x0438, [0x0b, 0xb8, 0x07, 0x6c]);

            if (ReferenceEquals(profile, M1ModelProfiles.M1Lite))
            {
                result.SetBytes(0x0200, [0x26, 0x02, 0x03, 0x16, 0x38, 0x01]);
                result.SetBytes(0x0210, "2026/02/03$"u8);
                result.SetBytes(0x0220, "16:38:01$"u8);
            }
            return result;
        }

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
            return action(this);
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
            byte[] curve = M1ModelProfiles.M1Lite.StockCurve(mode) ??
                throw new ArgumentOutOfRangeException(nameof(mode));
            SetByte(M1EcLayout.ModeAddress, mode);
            SetBytes(M1EcLayout.CurveBlockStart, curve);
        }

        internal void EnqueueRuntimeSamples(params byte[][] samples)
        {
            foreach (byte[] sample in samples)
            {
                RuntimeSamples.Enqueue((byte[])sample.Clone());
            }
        }

        private void SetBytes(ushort start, ReadOnlySpan<byte> values)
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

        internal Exception? InitializeException { get; init; }

        internal Exception? DisposeException { get; init; }

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
