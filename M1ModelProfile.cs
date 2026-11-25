namespace FanControl.MinisforumM1Pro;

internal enum LowCodeAction
{
    Off,
    Clamp,
}

internal enum ReportedCodeMode
{
    Requested,
    Applied,
}

internal enum CurveStartupState
{
    Firmware,
    Recoverable,
    Unsupported,
}

internal sealed record CurveStartupClassification(
    CurveStartupState State,
    byte Mode,
    byte[]? CanonicalCurve,
    string Detail);

internal readonly record struct FanControlRequest(
    byte AppliedCode,
    float ReportedPercentage);

internal sealed class FanControlPolicy
{
    internal FanControlPolicy(
        byte maximumCode,
        byte minimumNonzeroCode,
        bool allowOff,
        LowCodeAction lowCodeAction,
        ReportedCodeMode reportedCodeMode)
    {
        if (maximumCode == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCode));
        }
        if (minimumNonzeroCode == 0 || minimumNonzeroCode > maximumCode)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumNonzeroCode));
        }

        MaximumCode = maximumCode;
        MinimumNonzeroCode = minimumNonzeroCode;
        AllowOff = allowOff;
        LowCodeAction = lowCodeAction;
        ReportedCodeMode = reportedCodeMode;
    }

    internal byte MaximumCode { get; }

    internal byte MinimumNonzeroCode { get; }

    internal bool AllowOff { get; }

    internal LowCodeAction LowCodeAction { get; }

    internal ReportedCodeMode ReportedCodeMode { get; }

    internal static bool IsValidPercentage(float percentage) =>
        float.IsFinite(percentage) && percentage is >= 0f and <= 100f;

    internal FanControlRequest Resolve(float percentage)
    {
        if (!IsValidPercentage(percentage))
        {
            throw new ArgumentOutOfRangeException(
                nameof(percentage),
                percentage,
                "Fan control percentage must be finite and between 0 and 100.");
        }

        byte requestedCode = (byte)Math.Round(
            percentage * MaximumCode / 100d,
            MidpointRounding.AwayFromZero);
        byte appliedCode = requestedCode;

        if (requestedCode == 0)
        {
            appliedCode = AllowOff ? (byte)0 : MinimumNonzeroCode;
        }
        else if (requestedCode < MinimumNonzeroCode)
        {
            appliedCode = LowCodeAction switch
            {
                LowCodeAction.Off when AllowOff => 0,
                LowCodeAction.Clamp => MinimumNonzeroCode,
                _ => throw new InvalidOperationException(
                    "The fan policy cannot apply its configured low-code action."),
            };
        }

        byte reported = ReportedCodeMode == ReportedCodeMode.Applied
            ? appliedCode
            : requestedCode;
        return new FanControlRequest(
            appliedCode,
            reported * 100f / MaximumCode);
    }

    internal bool AcceptsCode(byte code) =>
        code <= MaximumCode &&
        ((code == 0 && AllowOff) || code >= MinimumNonzeroCode);

    internal void AssertCode(byte code)
    {
        if (!AcceptsCode(code))
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                $"Allowed native codes are " +
                (AllowOff ? "0 or " : string.Empty) +
                $"{MinimumNonzeroCode}..{MaximumCode}.");
        }
    }

    internal bool AcceptsTargetRpm(int rpm)
    {
        if (rpm < 0 || rpm % 100 != 0)
        {
            return false;
        }

        int code = rpm / 100;
        return code <= byte.MaxValue && AcceptsCode((byte)code);
    }
}

internal readonly record struct EcExpectation(
    ushort Address,
    byte Expected,
    byte Mask = byte.MaxValue);

internal sealed class M1ModelProfile
{
    private enum ChannelStartupState
    {
        Firmware,
        Recoverable,
        Unsupported,
    }

    private readonly EcExpectation[] startupProbe;
    private readonly byte[] candidateSlots;
    private readonly byte[]? normalCurve;
    private readonly byte[]? alternateCurve;

    internal M1ModelProfile(
        string key,
        string board,
        string modelName,
        string pluginName,
        string sensorIdPrefix,
        bool hardwareValidated,
        FanControlPolicy cpuPolicy,
        FanControlPolicy systemPolicy,
        IEnumerable<byte> candidateSlots,
        IEnumerable<EcExpectation> startupProbe,
        byte? requiredControllerRevision = null,
        byte[]? normalCurve = null,
        byte[]? alternateCurve = null,
        bool validateTelemetry = false,
        bool validateTargets = false)
    {
        Key = key;
        Board = board;
        ModelName = modelName;
        PluginName = pluginName;
        SensorIdPrefix = sensorIdPrefix;
        HardwareValidated = hardwareValidated;
        CpuPolicy = cpuPolicy;
        SystemPolicy = systemPolicy;
        this.candidateSlots = candidateSlots.Distinct().ToArray();
        this.startupProbe = startupProbe.ToArray();
        RequiredControllerRevision = requiredControllerRevision;
        this.normalCurve = normalCurve is null ? null : (byte[])normalCurve.Clone();
        this.alternateCurve = alternateCurve is null
            ? null
            : (byte[])alternateCurve.Clone();
        ValidateTelemetry = validateTelemetry;
        ValidateTargets = validateTargets;

        if (this.candidateSlots.Length == 0 ||
            this.candidateSlots.Any(slot => slot > 1))
        {
            throw new ArgumentException("PawnIO slots must be 0 or 1.", nameof(candidateSlots));
        }
        if (this.startupProbe.Length == 0)
        {
            throw new ArgumentException("A startup probe is required.", nameof(startupProbe));
        }
        if ((this.normalCurve is null) != (this.alternateCurve is null))
        {
            throw new ArgumentException("Both known curve variants must be supplied together.");
        }
        if (this.normalCurve is not null &&
            (this.normalCurve.Length != M1EcLayout.CurveBlockLength ||
             this.alternateCurve!.Length != M1EcLayout.CurveBlockLength))
        {
            throw new ArgumentException(
                $"Curve fingerprints must contain {M1EcLayout.CurveBlockLength} bytes.");
        }
    }

    internal string Key { get; }

    internal string Board { get; }

    internal string ModelName { get; }

    internal string PluginName { get; }

    internal string SensorIdPrefix { get; }

    internal bool HardwareValidated { get; }

    internal FanControlPolicy CpuPolicy { get; }

    internal FanControlPolicy SystemPolicy { get; }

    internal bool ValidateTelemetry { get; }

    internal bool ValidateTargets { get; }

    internal byte? RequiredControllerRevision { get; }

    internal bool RequiresCurveFingerprint => normalCurve is not null;

    internal byte[] CandidateSlots => (byte[])candidateSlots.Clone();

    internal byte[]? StockCurve(byte mode)
    {
        byte[]? selected = mode switch
        {
            0xb0 or 0xb2 => normalCurve,
            0xb1 => alternateCurve,
            _ => null,
        };
        return selected is null ? null : (byte[])selected.Clone();
    }

    internal ushort[] StartupProbeAddresses => startupProbe
        .Select(item => item.Address)
        .ToArray();

    internal void AssertOuterIdentity(ReadOnlySpan<byte> identity)
    {
        if (identity.Length != 3 || identity[0] != 0x55 || identity[1] != 0x71 ||
            identity[2] is 0x00 or 0xff ||
            (RequiredControllerRevision is byte revision && identity[2] != revision))
        {
            string actual = Convert.ToHexString(identity).ToLowerInvariant();
            throw new PlatformNotSupportedException(
                $"{ModelName} outer controller identity is not an accepted IT5571 " +
                $"profile: {actual}.");
        }
    }

    internal void AssertNativeIdentity(
        ReadOnlySpan<byte> outerIdentity,
        ReadOnlySpan<byte> nativeIdentity)
    {
        if (outerIdentity.Length != 3 || !nativeIdentity.SequenceEqual(outerIdentity))
        {
            throw new PlatformNotSupportedException(
                $"{ModelName} native and outer controller identities do not match.");
        }
    }

    internal FanControlPolicy Policy(M1Fan fan) => fan switch
    {
        M1Fan.Cpu => CpuPolicy,
        M1Fan.System => SystemPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    internal void AssertStartupProbe(ReadOnlySpan<byte> values)
    {
        if (values.Length != startupProbe.Length)
        {
            throw new PlatformNotSupportedException(
                $"{ModelName} EC startup probe returned {values.Length} bytes; " +
                $"expected {startupProbe.Length}.");
        }

        for (int index = 0; index < startupProbe.Length; index++)
        {
            EcExpectation item = startupProbe[index];
            if ((values[index] & item.Mask) != (item.Expected & item.Mask))
            {
                throw new PlatformNotSupportedException(
                    $"{ModelName} EC startup probe mismatch at 0x{item.Address:x4}: " +
                    $"read 0x{values[index]:x2}, expected 0x{item.Expected:x2} " +
                    $"with mask 0x{item.Mask:x2}.");
            }
        }
    }

    internal bool MatchesCurve(byte mode, ReadOnlySpan<byte> curve)
    {
        if (normalCurve is null || curve.Length != M1EcLayout.CurveBlockLength)
        {
            return false;
        }

        return mode switch
        {
            0xb0 or 0xb2 => curve.SequenceEqual(normalCurve),
            0xb1 => curve.SequenceEqual(alternateCurve),
            _ => false,
        };
    }

    internal CurveStartupClassification ClassifyStartupCurve(
        byte mode,
        ReadOnlySpan<byte> curve)
    {
        if (curve.Length != M1EcLayout.CurveBlockLength)
        {
            throw new ArgumentException(
                $"Expected a {M1EcLayout.CurveBlockLength}-byte EC curve block.",
                nameof(curve));
        }

        byte[]? canonical = StockCurve(mode);
        if (canonical is null)
        {
            return new CurveStartupClassification(
                CurveStartupState.Unsupported,
                mode,
                null,
                $"unknown EC curve selector 0x{mode:x2}");
        }

        for (int offset = 0; offset < M1EcLayout.CurveBlockLength; offset++)
        {
            if (!M1EcLayout.IsOwnedOffset(offset) && curve[offset] != canonical[offset])
            {
                ushort address = (ushort)(M1EcLayout.CurveBlockStart + offset);
                return new CurveStartupClassification(
                    CurveStartupState.Unsupported,
                    mode,
                    null,
                    $"unowned EC curve mismatch at 0x{address:x4} " +
                    $"(read 0x{curve[offset]:x2}, expected 0x{canonical[offset]:x2} " +
                    $"for selector 0x{mode:x2})");
            }
        }

        if (curve.SequenceEqual(canonical))
        {
            return new CurveStartupClassification(
                CurveStartupState.Firmware,
                mode,
                canonical,
                $"selector 0x{mode:x2}");
        }

        (ChannelStartupState cpuState, string cpuDetail) = ClassifyChannel(
            M1Fan.Cpu,
            CpuPolicy,
            curve,
            canonical,
            mode);
        if (cpuState == ChannelStartupState.Unsupported)
        {
            return new CurveStartupClassification(
                CurveStartupState.Unsupported,
                mode,
                null,
                cpuDetail);
        }

        (ChannelStartupState systemState, string systemDetail) = ClassifyChannel(
            M1Fan.System,
            SystemPolicy,
            curve,
            canonical,
            mode);
        if (systemState == ChannelStartupState.Unsupported)
        {
            return new CurveStartupClassification(
                CurveStartupState.Unsupported,
                mode,
                null,
                systemDetail);
        }

        List<string> details = [];
        if (cpuState == ChannelStartupState.Recoverable)
        {
            details.Add(cpuDetail);
        }
        if (systemState == ChannelStartupState.Recoverable)
        {
            details.Add(systemDetail);
        }
        return new CurveStartupClassification(
            CurveStartupState.Recoverable,
            mode,
            canonical,
            string.Join(", ", details));
    }

    internal void AssertRuntimeSample(
        ReadOnlySpan<byte> values,
        ReadOnlySpan<byte> curve,
        bool checkCurveHysteresis = true)
    {
        if (!ValidateTargets)
        {
            return;
        }
        if (values.Length != M1EcLayout.RuntimeSampleAddresses.Length ||
            curve.Length != M1EcLayout.CurveBlockLength)
        {
            throw new PlatformNotSupportedException(
                $"{ModelName} runtime-consistency probe returned an unexpected length.");
        }

        int cpuRpm = (values[0] << 8) | values[1];
        int systemRpm = (values[2] << 8) | values[3];
        byte cpuTemperature = values[4];
        byte systemTemperature = values[5];
        byte cpuCode = values[6];
        byte systemCode = values[7];
        bool hysteresisCoherent = !checkCurveHysteresis ||
            (IsHysteresisValidCode(M1Fan.Cpu, cpuTemperature, cpuCode, curve) &&
             IsHysteresisValidCode(M1Fan.System, systemTemperature, systemCode, curve));
        if (!CpuPolicy.AcceptsTargetRpm(cpuRpm) ||
            !SystemPolicy.AcceptsTargetRpm(systemRpm) ||
            cpuRpm != cpuCode * 100 ||
            systemRpm != systemCode * 100 ||
            cpuTemperature > 125 ||
            systemTemperature > 125 ||
            !hysteresisCoherent)
        {
            throw new PlatformNotSupportedException(
                $"{ModelName} EC targets, final codes, temperatures, and stock " +
                $"curve are not coherent: CPU {cpuRpm} RPM/code {cpuCode} at " +
                $"{cpuTemperature} C, system {systemRpm} RPM/code {systemCode} " +
                $"at {systemTemperature} C.");
        }
    }

    private static (ChannelStartupState State, string Detail) ClassifyChannel(
        M1Fan fan,
        FanControlPolicy policy,
        ReadOnlySpan<byte> curve,
        ReadOnlySpan<byte> canonical,
        byte mode)
    {
        string label = fan == M1Fan.Cpu ? "CPU" : "system";
        ReadOnlySpan<ushort> baseAddresses = M1EcLayout.BaseAddresses(fan);
        ReadOnlySpan<ushort> slopeAddresses = M1EcLayout.SlopeAddresses(fan);
        Span<byte> observedBases = stackalloc byte[baseAddresses.Length];
        Span<byte> canonicalBases = stackalloc byte[baseAddresses.Length];
        Span<byte> observedSlopes = stackalloc byte[slopeAddresses.Length];
        Span<byte> canonicalSlopes = stackalloc byte[slopeAddresses.Length];

        for (int index = 0; index < baseAddresses.Length; index++)
        {
            int baseOffset = baseAddresses[index] - M1EcLayout.CurveBlockStart;
            int slopeOffset = slopeAddresses[index] - M1EcLayout.CurveBlockStart;
            observedBases[index] = curve[baseOffset];
            canonicalBases[index] = canonical[baseOffset];
            observedSlopes[index] = curve[slopeOffset];
            canonicalSlopes[index] = canonical[slopeOffset];
        }

        if (observedBases.SequenceEqual(canonicalBases) &&
            observedSlopes.SequenceEqual(canonicalSlopes))
        {
            return (ChannelStartupState.Firmware, string.Empty);
        }

        if (observedBases.SequenceEqual(canonicalBases))
        {
            for (int index = 0; index < observedSlopes.Length; index++)
            {
                if (observedSlopes[index] != 0 &&
                    observedSlopes[index] != canonicalSlopes[index])
                {
                    return (
                        ChannelStartupState.Unsupported,
                        $"{label} slope mismatch at 0x{slopeAddresses[index]:x4} " +
                        $"(read 0x{observedSlopes[index]:x2}, expected 0x00 or " +
                        $"0x{canonicalSlopes[index]:x2} for selector 0x{mode:x2})");
                }
            }
            return (ChannelStartupState.Recoverable, $"{label} interrupted slope state");
        }

        for (int index = 0; index < observedSlopes.Length; index++)
        {
            if (observedSlopes[index] != 0)
            {
                return (
                    ChannelStartupState.Unsupported,
                    $"{label} curve has non-zero slope 0x{observedSlopes[index]:x2} " +
                    $"at 0x{slopeAddresses[index]:x4} with non-canonical bases for " +
                    $"selector 0x{mode:x2}");
            }
        }

        byte uniformCode = observedBases[0];
        if (!observedBases.ContainsAnyExcept(uniformCode))
        {
            return policy.AcceptsCode(uniformCode)
                ? (ChannelStartupState.Recoverable, $"{label} manual code {uniformCode}")
                : (
                    ChannelStartupState.Unsupported,
                    $"{label} curve at 0x{baseAddresses[0]:x4} has disallowed manual " +
                    $"code {uniformCode} for selector 0x{mode:x2}");
        }

        for (int restored = 0; restored < observedBases.Length; restored++)
        {
            if (!observedBases[..restored].SequenceEqual(canonicalBases[..restored]))
            {
                continue;
            }
            for (int written = restored + 1; written <= observedBases.Length; written++)
            {
                byte code = observedBases[restored];
                if (policy.AcceptsCode(code) &&
                    !observedBases.Slice(restored, written - restored).ContainsAnyExcept(code) &&
                    observedBases[written..].SequenceEqual(canonicalBases[written..]))
                {
                    return (
                        ChannelStartupState.Recoverable,
                        $"{label} interrupted manual code {code}");
                }
            }
        }

        for (int restored = 0; restored < observedBases.Length; restored++)
        {
            if (!observedBases[..restored].SequenceEqual(canonicalBases[..restored]))
            {
                continue;
            }
            ReadOnlySpan<byte> suffix = observedBases[restored..];
            byte first = suffix[0];
            if (!policy.AcceptsCode(first))
            {
                continue;
            }
            int transition = suffix.IndexOfAnyExcept(first);
            if (transition < 0)
            {
                return (
                    ChannelStartupState.Recoverable,
                    $"{label} interrupted restore from code {first}");
            }
            byte second = suffix[transition];
            if (policy.AcceptsCode(second) &&
                !suffix[transition..].ContainsAnyExcept(second))
            {
                return (
                    ChannelStartupState.Recoverable,
                    $"{label} interrupted manual transition {first}->{second}");
            }
        }

        for (int index = 0; index < observedBases.Length; index++)
        {
            if (observedBases[index] != canonicalBases[index])
            {
                return (
                    ChannelStartupState.Unsupported,
                    $"{label} curve mismatch at 0x{baseAddresses[index]:x4} " +
                    $"(read 0x{observedBases[index]:x2}, expected " +
                    $"0x{canonicalBases[index]:x2} for selector 0x{mode:x2})");
            }
        }

        return (ChannelStartupState.Unsupported, $"{label} curve state is unsupported");
    }

    private static bool IsHysteresisValidCode(
        M1Fan fan,
        byte temperature,
        byte actualCode,
        ReadOnlySpan<byte> curve)
    {
        (int tableOffset, int slopeOffset, int rows) = fan switch
        {
            M1Fan.Cpu => (0x00, 0x30, 8),
            M1Fan.System => (0x18, 0x38, 7),
            _ => throw new ArgumentOutOfRangeException(nameof(fan)),
        };

        for (int row = 0; row < rows; row++)
        {
            int tuple = tableOffset + (row * 3);
            byte upper = curve[tuple + 1];
            byte lower = curve[tuple + 2];
            bool aboveLower = row == 0 || temperature >= lower;
            bool belowUpper = row == rows - 1 || temperature <= upper;
            if (!aboveLower || !belowUpper)
            {
                continue;
            }

            int delta = Math.Max(0, temperature - lower);
            int code = curve[tuple] + ((curve[slopeOffset + row] * delta) / 100);
            if (code == actualCode)
            {
                return true;
            }
        }
        return false;
    }

    internal void AssertTelemetry(M1Telemetry telemetry)
    {
        if (!ValidateTelemetry)
        {
            return;
        }

        if (telemetry.CpuFanRpm is < 0 or > 10_000 ||
            telemetry.SystemFanRpm is < 0 or > 10_000 ||
            telemetry.CpuTemperatureC is < 0 or > 125 ||
            telemetry.SystemTemperatureC is < 0 or > 125)
        {
            throw new InvalidDataException(
                $"{ModelName} EC telemetry is outside the accepted envelope.");
        }
    }
}

internal static class M1ModelProfiles
{
    private static readonly byte[] KnownNormalCurve =
    [
        0, 25, 0, 18, 45, 25, 22, 54, 45, 28, 66, 54,
        33, 80, 66, 37, 92, 80, 40, 96, 92, 51, 100, 96,
        10, 25, 0, 17, 60, 25, 23, 91, 60,
        41, 100, 90, 41, 100, 90, 41, 100, 90, 41, 100, 90,
        0, 0, 0,
        0, 20, 66, 41, 28, 25, 19, 0,
        0, 17, 58, 0, 0, 0, 0, 0,
    ];

    private static readonly byte[] KnownAlternateCurve =
    [
        0, 25, 0, 18, 45, 25, 22, 54, 45, 25, 72, 54,
        28, 80, 72, 32, 89, 80, 34, 93, 89, 51, 100, 93,
        10, 25, 0, 16, 60, 25, 22, 91, 60,
        41, 100, 90, 41, 100, 90, 41, 100, 90, 41, 100, 90,
        0, 0, 0,
        0, 20, 33, 16, 50, 22, 169, 0,
        0, 17, 61, 0, 0, 0, 0, 0,
    ];

    private static readonly FanControlPolicy M1ProPolicy = new(
        maximumCode: 51,
        minimumNonzeroCode: 7,
        allowOff: true,
        lowCodeAction: LowCodeAction.Off,
        reportedCodeMode: ReportedCodeMode.Requested);

    private static readonly FanControlPolicy M1LitePolicy = new(
        maximumCode: 51,
        minimumNonzeroCode: 1,
        allowOff: true,
        lowCodeAction: LowCodeAction.Clamp,
        reportedCodeMode: ReportedCodeMode.Applied);

    internal static readonly M1ModelProfile M1Pro = new(
        key: "m1pro-arbsc",
        board: "ARBSC",
        modelName: "M1 Pro",
        pluginName: "Minisforum M1 Pro (ARBSC)",
        sensorIdPrefix: "minisforum.m1pro.arbsc",
        hardwareValidated: true,
        cpuPolicy: M1ProPolicy,
        systemPolicy: M1ProPolicy,
        candidateSlots: [1],
        requiredControllerRevision: 0x07,
        startupProbe:
        [
            new EcExpectation(0x200d, 0xcb),
        ],
        normalCurve: KnownNormalCurve,
        alternateCurve: KnownAlternateCurve,
        validateTelemetry: true);

    internal static readonly M1ModelProfile M1Lite = new(
        key: "m1lite-mtbsi",
        board: "MTBSI",
        modelName: "M1 Lite",
        pluginName: "Minisforum M1 Lite (MTBSI)",
        sensorIdPrefix: "minisforum.m1lite.mtbsi",
        hardwareValidated: false,
        cpuPolicy: M1LitePolicy,
        systemPolicy: M1LitePolicy,
        candidateSlots: [1, 0],
        startupProbe: MtbsiStartupProbe(),
        normalCurve: KnownNormalCurve,
        alternateCurve: KnownAlternateCurve,
        validateTelemetry: true,
        validateTargets: true);

    internal static bool IsAllowedCanonicalSlope(ushort address, byte value)
    {
        if (!M1EcLayout.IsSlopeAddress(address))
        {
            return false;
        }

        int offset = address - M1EcLayout.CurveBlockStart;
        return KnownNormalCurve[offset] == value ||
            KnownAlternateCurve[offset] == value;
    }

    internal static M1ModelProfile ResolveExact(string board) =>
        TryResolveExact(board) ?? throw new PlatformNotSupportedException(
            $"Expected baseboard {M1Pro.Board} or {M1Lite.Board}; found {board}.");

    internal static M1ModelProfile? TryResolveExact(string board)
    {
        if (string.Equals(board, M1Pro.Board, StringComparison.Ordinal))
        {
            return M1Pro;
        }
        if (string.Equals(board, M1Lite.Board, StringComparison.Ordinal))
        {
            return M1Lite;
        }
        return null;
    }

    private static EcExpectation[] MtbsiStartupProbe()
    {
        List<EcExpectation> result =
        [
            new EcExpectation(0x200d, 0xcb, 0xcb),
        ];

        byte[] packedBuild = [0x26, 0x02, 0x03, 0x16, 0x38, 0x01];
        byte[] buildDate = "2026/02/03$"u8.ToArray();
        byte[] buildTime = "16:38:01$"u8.ToArray();
        AddSequence(result, 0x0200, packedBuild);
        AddSequence(result, 0x0210, buildDate);
        AddSequence(result, 0x0220, buildTime);
        return result.ToArray();
    }

    private static void AddSequence(
        ICollection<EcExpectation> destination,
        ushort start,
        IEnumerable<byte> values)
    {
        int offset = 0;
        foreach (byte value in values)
        {
            destination.Add(new EcExpectation((ushort)(start + offset), value));
            offset++;
        }
    }
}
