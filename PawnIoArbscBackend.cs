namespace FanControl.MinisforumM1Pro;

internal sealed class PawnIoArbscBackend : IArbscBackend
{
    private readonly object sync = new();
    private readonly Func<string> boardReader;
    private readonly Func<IArbscTransport> transportFactory;
    private IArbscTransport? transport;
    private byte[]? baseline;
    private byte? cpuCode;
    private byte? systemCode;

    internal PawnIoArbscBackend()
        : this(HostIdentity.ReadBoard, static () => new PawnIoTransport())
    {
    }

    internal PawnIoArbscBackend(
        Func<string> boardReader,
        Func<IArbscTransport> transportFactory)
    {
        this.boardReader = boardReader;
        this.transportFactory = transportFactory;
    }

    internal byte? CodeFor(ArbscFan fan) => fan switch
    {
        ArbscFan.Cpu => cpuCode,
        ArbscFan.System => systemCode,
        _ => throw new ArgumentOutOfRangeException(nameof(fan)),
    };

    public void Initialize()
    {
        lock (sync)
        {
            if (transport is not null)
            {
                return;
            }

            HostIdentityGate.AssertBoard(boardReader());
            IArbscTransport candidate = transportFactory();
            try
            {
                if (!candidate.Read(ArbscProfile.ControllerProfileAddresses)
                        .SequenceEqual(ArbscProfile.ExpectedControllerProfile))
                {
                    throw new PlatformNotSupportedException(
                        "The live controller is not the M1 Pro ARBSC IT5571 profile.");
                }
                byte[] captured = candidate.Read(ArbscProfile.OwnedAddresses);

                transport = candidate;
                baseline = captured;
                cpuCode = null;
                systemCode = null;
            }
            catch
            {
                candidate.Dispose();
                throw;
            }
        }
    }

    public ArbscTelemetry ReadTelemetry()
    {
        lock (sync)
        {
            return ArbscTelemetryDecoder.Decode(
                ActiveTransport().Read(ArbscProfile.TelemetryAddresses));
        }
    }

    public void Set(ArbscFan fan, byte code)
    {
        lock (sync)
        {
            if (CodeFor(fan) == code)
            {
                return;
            }

            IArbscTransport active = ActiveTransport();
            try
            {
                active.Write(ArbscProfile.ManualWrites(fan, code));
                SetCode(fan, code);
            }
            catch
            {
                RestoreAfterFailure(active);
                throw;
            }
        }
    }

    public void Reset(ArbscFan fan)
    {
        lock (sync)
        {
            if (CodeFor(fan) is null)
            {
                return;
            }

            IArbscTransport active = ActiveTransport();
            try
            {
                active.Write(ArbscProfile.RestoreWrites(fan, ActiveBaseline()));
                SetCode(fan, null);
            }
            catch
            {
                RestoreAfterFailure(active);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            IArbscTransport? old = transport;
            byte[]? restore = baseline;
            transport = null;
            baseline = null;
            if (old is null)
            {
                return;
            }

            try
            {
                old.Write(ArbscProfile.AllRestoreWrites(restore ??
                    throw new InvalidOperationException(
                        "The ARBSC startup baseline is unavailable.")));
            }
            finally
            {
                cpuCode = null;
                systemCode = null;
                old.Dispose();
            }
        }
    }

    private IArbscTransport ActiveTransport() => transport ??
        throw new InvalidOperationException("The ARBSC backend is not initialized.");

    private byte[] ActiveBaseline() => baseline ??
        throw new InvalidOperationException("The ARBSC startup baseline is unavailable.");

    private void SetCode(ArbscFan fan, byte? code)
    {
        if (fan == ArbscFan.Cpu)
        {
            cpuCode = code;
        }
        else
        {
            systemCode = code;
        }
    }

    private void RestoreAfterFailure(IArbscTransport active)
    {
        try
        {
            active.Write(ArbscProfile.AllRestoreWrites(ActiveBaseline()));
            cpuCode = null;
            systemCode = null;
        }
        catch
        {
            // Normal Close gets one final restoration attempt.
        }
    }
}
