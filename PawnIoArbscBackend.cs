namespace FanControl.MinisforumM1Pro;

internal sealed class PawnIoArbscBackend : IArbscBackend
{
    private static readonly byte[] ExpectedChip = [0x55, 0x71, 0x07, 0xcb];

    private readonly object sync = new();
    private readonly IHostIdentityReader hostIdentityReader;
    private readonly Func<IArbscTransport> transportFactory;
    private IArbscTransport? transport;
    private byte? cpuCode;
    private byte? systemCode;

    internal PawnIoArbscBackend()
        : this(new WmiHostIdentityReader(), static () => new PawnIoTransport())
    {
    }

    internal PawnIoArbscBackend(
        IHostIdentityReader hostIdentityReader,
        Func<IArbscTransport> transportFactory)
    {
        this.hostIdentityReader = hostIdentityReader;
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

            HostIdentityGate.AssertExact(hostIdentityReader.Read());
            IArbscTransport candidate = transportFactory();
            try
            {
                if (!candidate.Read(ArbscProfile.ChipAddresses).SequenceEqual(ExpectedChip))
                {
                    throw new PlatformNotSupportedException(
                        "The live controller is not the M1 Pro ARBSC IT5571 profile.");
                }
                if (!candidate.Read(ArbscProfile.CurveAddresses)
                        .SequenceEqual(ArbscProfile.StockCurve))
                {
                    throw new InvalidOperationException(
                        "The ARBSC fan curve is not stock; reboot before loading the plugin.");
                }

                transport = candidate;
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
                active.Write(ArbscProfile.StockWrites(fan));
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
            transport = null;
            if (old is null)
            {
                return;
            }

            try
            {
                old.Write(ArbscProfile.AllStockWrites());
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
            active.Write(ArbscProfile.AllStockWrites());
            cpuCode = null;
            systemCode = null;
        }
        catch
        {
            // Normal Close gets one final restoration attempt.
        }
    }
}
