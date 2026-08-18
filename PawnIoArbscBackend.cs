using System.Runtime.ExceptionServices;

namespace FanControl.MinisforumM1Pro;

internal sealed class PawnIoM1Backend : IM1Backend
{
    private const int CurveSnapshotAttempts = 6;
    private const int RuntimeSampleAttempts = 6;
    private readonly object sync = new();
    private readonly M1ModelProfile profile;
    private readonly Func<string> boardReader;
    private readonly Func<byte, IM1Transport> transportFactory;
    private IM1Transport? transport;
    private byte[]? outerIdentity;
    private byte[]? baselineCurve;
    private byte[]? baselineOwned;
    private byte[]? expectedCurve;
    private byte[]? recoveryCurve;
    private byte baselineMode;
    private byte? cpuCode;
    private byte? systemCode;
    private bool needsRestore;

    internal PawnIoM1Backend(M1ModelProfile profile)
        : this(
            profile,
            HostIdentity.ReadBoard,
            static slot => new PawnIoTransport(slot))
    {
    }

    internal PawnIoM1Backend(
        M1ModelProfile profile,
        Func<string> boardReader,
        Func<byte, IM1Transport> transportFactory)
    {
        this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        this.boardReader = boardReader ?? throw new ArgumentNullException(nameof(boardReader));
        this.transportFactory = transportFactory ??
            throw new ArgumentNullException(nameof(transportFactory));
    }

    internal byte? CodeFor(M1Fan fan)
    {
        lock (sync)
        {
            return GetCode(fan);
        }
    }

    internal bool NeedsRestore
    {
        get
        {
            lock (sync)
            {
                return needsRestore;
            }
        }
    }

    internal byte? ActiveSlot { get; private set; }

    public void Initialize()
    {
        lock (sync)
        {
            if (transport is not null)
            {
                return;
            }

            HostIdentityGate.AssertBoard(profile, boardReader());
            ControllerCandidate selected = DiscoverController();
            try
            {
                CurveState startup = selected.Transport.RunExclusive(access =>
                    ProbeNativeProfile(access, selected.OuterIdentity));
                transport = selected.Transport;
                outerIdentity = selected.OuterIdentity;
                baselineMode = startup.Mode;
                baselineCurve = startup.Curve;
                expectedCurve = (byte[])startup.Curve.Clone();
                baselineOwned = M1EcLayout.CaptureOwned(startup.Curve);
                recoveryCurve = null;
                cpuCode = null;
                systemCode = null;
                needsRestore = false;
                ActiveSlot = selected.Slot;
            }
            catch
            {
                selected.Transport.Dispose();
                throw;
            }
        }
    }

    public M1Telemetry ReadTelemetry()
    {
        lock (sync)
        {
            return ActiveTransport().RunExclusive(access =>
            {
                M1Telemetry telemetry = M1TelemetryDecoder.Decode(
                    access.Read(M1EcLayout.TelemetryAddresses));
                profile.AssertTelemetry(telemetry);
                return telemetry;
            });
        }
    }

    public void Set(M1Fan fan, byte code)
    {
        lock (sync)
        {
            profile.Policy(fan).AssertCode(code);
            ActiveTransport().RunExclusive(active =>
            {
                AssertTransactionReady(active);
                if (GetCode(fan) != code)
                {
                    ExecuteTransaction(active, M1EcLayout.ManualWrites(fan, code));
                    SetCode(fan, code);
                    needsRestore = true;
                }
                return 0;
            });
        }
    }

    public void Reset(M1Fan fan)
    {
        lock (sync)
        {
            if (GetCode(fan) is null)
            {
                return;
            }

            ActiveTransport().RunExclusive(active =>
            {
                AssertTransactionReady(active);
                ExecuteTransaction(
                    active,
                    M1EcLayout.RestoreWrites(fan, ActiveBaselineOwned()));
                SetCode(fan, null);
                needsRestore = cpuCode is not null || systemCode is not null;
                return 0;
            });
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            IM1Transport? old = transport;
            if (old is null)
            {
                return;
            }

            Exception? failure = null;
            try
            {
                if (needsRestore)
                {
                    old.RunExclusive(active =>
                    {
                        RestoreForDispose(active);
                        return 0;
                    });
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                old.Dispose();
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }

            ClearState();
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }

    private ControllerCandidate DiscoverController()
    {
        List<ControllerCandidate> matches = [];
        List<Exception> failures = [];
        byte[] candidateSlots = profile.CandidateSlots;
        int classified = 0;
        foreach (byte slot in candidateSlots)
        {
            IM1Transport? candidate = null;
            try
            {
                candidate = transportFactory(slot);
                byte[] identity = candidate.ReadOuterIdentity();
                classified++;
                try
                {
                    profile.AssertOuterIdentity(identity);
                }
                catch (PlatformNotSupportedException)
                {
                    continue;
                }
                matches.Add(new ControllerCandidate(slot, candidate, identity));
                candidate = null;
            }
            catch (Exception exception)
            {
                failures.Add(new InvalidOperationException(
                    $"PawnIO slot {slot} outer-identity probe failed.",
                    exception));
            }
            finally
            {
                candidate?.Dispose();
            }
        }

        if (classified == candidateSlots.Length &&
            failures.Count == 0 &&
            matches.Count == 1)
        {
            return matches[0];
        }

        foreach (ControllerCandidate match in matches)
        {
            match.Transport.Dispose();
        }

        Exception? inner = failures.Count == 0 ? null : new AggregateException(failures);
        string message = failures.Count != 0 || classified != candidateSlots.Length
            ? $"Not every {profile.ModelName} controller slot could be classified safely."
            : matches.Count == 0
                ? $"No unique IT5571 controller was found for {profile.ModelName}."
                : $"Multiple IT5571 controllers matched {profile.ModelName}; " +
                  "refusing XRAM access.";
        throw inner is null
            ? new PlatformNotSupportedException(message)
            : new PlatformNotSupportedException(message, inner);
    }

    private CurveState ProbeNativeProfile(
        IM1TransportAccess candidate,
        ReadOnlySpan<byte> discoveredIdentity)
    {
        profile.AssertNativeIdentity(
            discoveredIdentity,
            candidate.Read(M1EcLayout.NativeIdentityAddresses));
        AssertStartupProbe(candidate);

        CurveState startup = profile.RequiresCurveFingerprint
            ? CaptureStockCurve(candidate)
            : ReadStableCurve(candidate);

        if (profile.ValidateTargets)
        {
            AssertStableRuntimeSample(candidate, startup.Curve);
        }
        M1Telemetry telemetry = M1TelemetryDecoder.Decode(
            candidate.Read(M1EcLayout.TelemetryAddresses));
        profile.AssertTelemetry(telemetry);

        profile.AssertNativeIdentity(
            discoveredIdentity,
            candidate.Read(M1EcLayout.NativeIdentityAddresses));
        AssertStartupProbe(candidate);
        return startup;
    }

    private CurveState CaptureStockCurve(IM1TransportAccess candidate)
    {
        CurveState? previous = null;
        Exception? lastFailure = null;
        for (int attempt = 0; attempt < CurveSnapshotAttempts; attempt++)
        {
            try
            {
                CurveState current = ReadCurveOnce(candidate);
                if (!profile.MatchesCurve(current.Mode, current.Curve))
                {
                    previous = null;
                    continue;
                }
                if (previous is not null &&
                    previous.Mode == current.Mode &&
                    previous.Curve.SequenceEqual(current.Curve))
                {
                    return current;
                }
                previous = current;
            }
            catch (Exception exception)
            {
                previous = null;
                lastFailure = exception;
            }
        }

        throw new PlatformNotSupportedException(
            $"{profile.ModelName} did not present two stable, exact EC stock-curve snapshots.",
            lastFailure);
    }

    private static CurveState ReadStableCurve(IM1TransportAccess candidate)
    {
        Exception? lastFailure = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return ReadCurveOnce(candidate);
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }
        }
        throw new IOException("The EC curve selector did not remain stable.", lastFailure);
    }

    private static CurveState ReadCurveOnce(IM1TransportAccess candidate)
    {
        byte before = ReadSingle(candidate, M1EcLayout.ModeAddress);
        byte[] curve = candidate.Read(M1EcLayout.CurveBlockAddresses);
        byte after = ReadSingle(candidate, M1EcLayout.ModeAddress);
        if (curve.Length != M1EcLayout.CurveBlockLength)
        {
            throw new IOException(
                $"EC curve read returned {curve.Length} bytes; " +
                $"expected {M1EcLayout.CurveBlockLength}.");
        }
        if (before != after)
        {
            throw new IOException(
                $"EC curve selector changed from 0x{before:x2} to 0x{after:x2}.");
        }
        return new CurveState(before, curve);
    }

    private static byte ReadSingle(IM1TransportAccess candidate, ushort address)
    {
        byte[] value = candidate.Read([address]);
        if (value.Length != 1)
        {
            throw new IOException(
                $"EC read at 0x{address:x4} returned {value.Length} bytes.");
        }
        return value[0];
    }

    private void AssertStartupProbe(IM1TransportAccess candidate) =>
        profile.AssertStartupProbe(candidate.Read(profile.StartupProbeAddresses));

    private void AssertStableRuntimeSample(
        IM1TransportAccess candidate,
        ReadOnlySpan<byte> curve)
    {
        byte[]? previous = null;
        Exception? lastFailure = null;
        for (int attempt = 0; attempt < RuntimeSampleAttempts; attempt++)
        {
            try
            {
                byte[] current = candidate.Read(M1EcLayout.RuntimeSampleAddresses);
                profile.AssertRuntimeSample(current, curve);
                if (previous is not null && previous.SequenceEqual(current))
                {
                    return;
                }
                previous = current;
            }
            catch (PlatformNotSupportedException exception)
            {
                previous = null;
                lastFailure = exception;
            }
        }
        throw new PlatformNotSupportedException(
            $"{profile.ModelName} EC runtime sample did not become stable and coherent.",
            lastFailure);
    }

    private void AssertTransactionReady(IM1TransportAccess active)
    {
        if (recoveryCurve is not null)
        {
            throw new InvalidOperationException(
                $"{profile.ModelName} has an unresolved EC write; only shutdown recovery is allowed.");
        }

        CurveState current = ReadRuntimeState(active);
        if (current.Mode != baselineMode ||
            !current.Curve.SequenceEqual(ActiveExpectedCurve()))
        {
            throw new InvalidOperationException(
                $"{profile.ModelName} EC curve ownership changed outside the plugin; " +
                "refusing to write.");
        }
    }

    private void ExecuteTransaction(IM1TransportAccess active, EcWrite[] writes)
    {
        byte[] previous = (byte[])ActiveExpectedCurve().Clone();
        byte[] attempted = M1EcLayout.Apply(previous, writes);
        needsRestore = true;
        recoveryCurve = attempted;
        try
        {
            active.Write(writes);
            VerifyCurve(active, attempted);
            expectedCurve = attempted;
            recoveryCurve = null;
        }
        catch
        {
            TryRestoreAfterFailure(active, previous, attempted);
            throw;
        }
    }

    private void VerifyCurve(IM1TransportAccess active, ReadOnlySpan<byte> expected)
    {
        CurveState current = ReadRuntimeState(active);
        if (current.Mode != baselineMode || !current.Curve.SequenceEqual(expected))
        {
            throw new IOException(
                $"{profile.ModelName} EC curve write did not verify exactly.");
        }
    }

    private CurveState ReadRuntimeState(IM1TransportAccess active)
    {
        profile.AssertNativeIdentity(
            ActiveOuterIdentity(),
            active.Read(M1EcLayout.NativeIdentityAddresses));
        AssertStartupProbe(active);
        return ReadStableCurve(active);
    }

    private bool TryRestoreAfterFailure(
        IM1TransportAccess active,
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> attempted)
    {
        try
        {
            CurveState current = ReadRuntimeState(active);
            if (!CanRecover(current, previous, attempted))
            {
                return false;
            }

            active.Write(M1EcLayout.AllRestoreWrites(ActiveBaselineOwned()));
            VerifyCurve(active, ActiveBaselineCurve());
            ResetToBaseline();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private bool CanRecover(
        CurveState current,
        ReadOnlySpan<byte> previous,
        ReadOnlySpan<byte> attempted)
    {
        if (current.Mode != baselineMode ||
            current.Curve.Length != M1EcLayout.CurveBlockLength ||
            previous.Length != current.Curve.Length ||
            attempted.Length != current.Curve.Length)
        {
            return false;
        }

        HashSet<ushort> owned = M1EcLayout.OwnedAddresses.ToHashSet();
        for (int offset = 0; offset < current.Curve.Length; offset++)
        {
            byte value = current.Curve[offset];
            if (owned.Contains((ushort)(M1EcLayout.CurveBlockStart + offset)))
            {
                if (value != previous[offset] &&
                    value != attempted[offset] &&
                    value != ActiveBaselineCurve()[offset])
                {
                    return false;
                }
            }
            else if (value != previous[offset])
            {
                return false;
            }
        }
        return true;
    }

    private void RestoreForDispose(IM1TransportAccess active)
    {
        byte[] previous = (byte[])ActiveExpectedCurve().Clone();
        byte[] attempted = recoveryCurve ?? ActiveBaselineCurve();
        CurveState current = ReadRuntimeState(active);
        if (!CanRecover(current, previous, attempted))
        {
            throw new InvalidOperationException(
                $"{profile.ModelName} EC ownership changed; baseline restoration was not attempted.");
        }

        try
        {
            active.Write(M1EcLayout.AllRestoreWrites(ActiveBaselineOwned()));
            VerifyCurve(active, ActiveBaselineCurve());
            ResetToBaseline();
        }
        catch
        {
            if (!TryRestoreAfterFailure(active, previous, ActiveBaselineCurve()))
            {
                throw;
            }
        }
    }

    private IM1Transport ActiveTransport() => transport ??
        throw new InvalidOperationException($"{profile.ModelName} backend is not initialized.");

    private byte[] ActiveOuterIdentity() => outerIdentity ??
        throw new InvalidOperationException("The startup controller identity is unavailable.");

    private byte[] ActiveBaselineCurve() => baselineCurve ??
        throw new InvalidOperationException("The startup EC curve is unavailable.");

    private byte[] ActiveBaselineOwned() => baselineOwned ??
        throw new InvalidOperationException("The startup EC ownership baseline is unavailable.");

    private byte[] ActiveExpectedCurve() => expectedCurve ??
        throw new InvalidOperationException("The expected EC ownership state is unavailable.");

    private byte? GetCode(M1Fan fan) => fan switch
    {
        M1Fan.Cpu => cpuCode,
        M1Fan.System => systemCode,
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    private void SetCode(M1Fan fan, byte? code)
    {
        if (fan == M1Fan.Cpu)
        {
            cpuCode = code;
        }
        else if (fan == M1Fan.System)
        {
            systemCode = code;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(fan));
        }
    }

    private void ResetToBaseline()
    {
        expectedCurve = (byte[])ActiveBaselineCurve().Clone();
        recoveryCurve = null;
        cpuCode = null;
        systemCode = null;
        needsRestore = false;
    }

    private void ClearState()
    {
        transport = null;
        outerIdentity = null;
        baselineCurve = null;
        baselineOwned = null;
        expectedCurve = null;
        recoveryCurve = null;
        cpuCode = null;
        systemCode = null;
        needsRestore = false;
        ActiveSlot = null;
    }

    private sealed record ControllerCandidate(
        byte Slot,
        IM1Transport Transport,
        byte[] OuterIdentity);

    private sealed record CurveState(byte Mode, byte[] Curve);
}
