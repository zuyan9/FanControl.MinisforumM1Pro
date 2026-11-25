using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace FanControl.MinisforumM1Pro;

internal readonly record struct EcWrite(ushort Address, byte Value);

internal interface IPawnIoExecutor : IDisposable
{
    void OpenAndLoad(byte[] module);

    ulong[] Execute(string name, ulong[] input, int outputCount);
}

internal interface IM1TransportAccess
{
    byte[] Read(ushort[] addresses);

    void Write(EcWrite[] writes);
}

internal interface IM1Transport : IDisposable
{
    byte[] ReadOuterIdentity();

    T RunExclusive<T>(Func<IM1TransportAccess, T> action);
}

internal sealed class PawnIoTransport : IM1Transport
{
    private static readonly TimeSpan IsaTimeout = TimeSpan.FromSeconds(5);
    private readonly Mutex isaMutex = new(false, M1EcLayout.IsaMutexName);
    private readonly IPawnIoExecutor native;
    private readonly byte slot;
    private readonly ulong outerIndexPort;
    private readonly SelectedAccess selectedAccess;
    private bool disposed;
    private Exception? poisonCause;

    internal PawnIoTransport(byte slot)
        : this(slot, CreateResources())
    {
    }

    private PawnIoTransport(
        byte slot,
        (IPawnIoExecutor Native, byte[] Module) resources)
        : this(slot, resources.Native, resources.Module)
    {
    }

    internal PawnIoTransport(
        byte slot,
        IPawnIoExecutor native,
        byte[] module)
    {
        this.native = native ?? throw new ArgumentNullException(nameof(native));
        try
        {
            if (slot > 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(slot),
                    "PawnIO slot must be 0 or 1.");
            }
            ArgumentNullException.ThrowIfNull(module);
            this.slot = slot;
            outerIndexPort = slot == 0 ? 0x2eUL : 0x4eUL;
            selectedAccess = new SelectedAccess(this);
            native.OpenAndLoad(module);
        }
        catch (Exception exception)
        {
            Exception? failure = exception;
            failure = CaptureFailure(failure, native.Dispose);
            failure = CaptureFailure(failure, isaMutex.Dispose);
            ExceptionDispatchInfo.Capture(failure!).Throw();
        }
    }

    public T RunExclusive<T>(Func<IM1TransportAccess, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunIsa(() =>
        {
            bool selected = false;
            T result = default!;
            Exception? bodyFailure = null;
            try
            {
                SelectSlot();
                selected = true;
                result = action(selectedAccess);
            }
            catch (Exception exception)
            {
                bodyFailure = exception;
            }

            Exception? nestedParkFailure = selected
                ? CaptureFailure(null, () => Out(0x2e, 0x10))
                : null;
            Exception? outerParkFailure = CaptureFailure(null, ParkOuterIndex);
            if (outerParkFailure is not null)
            {
                Exception combined = Combine(
                    Combine(bodyFailure, nestedParkFailure),
                    outerParkFailure)!;
                Poison(combined);
                throw AmbiguousSelectorState(
                    "EC access did not park the Super-I/O index port.",
                    combined);
            }

            Exception? totalFailure = Combine(bodyFailure, nestedParkFailure);
            if (totalFailure is not null)
            {
                ExceptionDispatchInfo.Capture(totalFailure).Throw();
            }
            return result;
        });
    }

    public byte[] ReadOuterIdentity() => RunIsa(() =>
    {
        byte[] result = null!;
        Exception? bodyFailure = null;
        try
        {
            SelectSlot();
            result =
            [
                checked((byte)In(0x20)),
                checked((byte)In(0x21)),
                checked((byte)In(0x22)),
            ];
        }
        catch (Exception exception)
        {
            bodyFailure = exception;
        }

        Exception? outerParkFailure = CaptureFailure(null, ParkOuterIndex);
        if (outerParkFailure is not null)
        {
            Exception combined = Combine(bodyFailure, outerParkFailure)!;
            Poison(combined);
            throw AmbiguousSelectorState(
                "Outer controller probe did not park the Super-I/O index port.",
                combined);
        }

        if (bodyFailure is not null)
        {
            ExceptionDispatchInfo.Capture(bodyFailure).Throw();
        }
        return result;
    });

    private byte[] ReadSelected(ushort[] addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        M1EcLayout.AssertReadsAllowed(addresses);
        byte[] values = new byte[addresses.Length];
        for (int index = 0; index < addresses.Length; index++)
        {
            SetAddress(addresses[index]);
            Out(0x2e, 0x12);
            values[index] = checked((byte)In(0x2f));
        }
        return values;
    }

    private void WriteSelected(EcWrite[] writes)
    {
        ArgumentNullException.ThrowIfNull(writes);
        M1EcLayout.AssertWritesAllowed(writes);
        foreach (EcWrite write in writes)
        {
            SetAddress(write.Address);
            Out(0x2e, 0x12);
            Out(0x2f, write.Value);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Exception? failure = CaptureFailure(null, native.Dispose);
        failure = CaptureFailure(failure, isaMutex.Dispose);
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private T RunIsa<T>(Func<T> action)
    {
        EnsureUsable();
        bool held = false;
        T result = default!;
        Exception? failure = null;
        try
        {
            try
            {
                held = isaMutex.WaitOne(IsaTimeout);
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }

            if (!held)
            {
                throw new TimeoutException("Timed out acquiring the ISA mutex.");
            }
            result = action();
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (held)
            {
                Exception? releaseFailure = CaptureFailure(null, isaMutex.ReleaseMutex);
                if (releaseFailure is not null)
                {
                    Poison(releaseFailure);
                    failure = Combine(
                        failure,
                        AmbiguousSelectorState(
                            "Releasing the ISA mutex failed.",
                            releaseFailure));
                }
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result;
    }

    private void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (poisonCause is not null)
        {
            throw AmbiguousSelectorState(
                "PawnIO transport was poisoned by an earlier Super-I/O cleanup failure.",
                poisonCause);
        }
    }

    private void Poison(Exception exception) => poisonCause ??= exception;

    private static InvalidOperationException AmbiguousSelectorState(
        string detail,
        Exception? inner) => new(
            detail + " Super-I/O selector state is ambiguous; shut down Windows and " +
            "briefly unplug AC power before accessing the controller again.",
            inner);

    private static Exception? Combine(Exception? first, Exception? second) =>
        first is null
            ? second
            : second is null
                ? first
                : new AggregateException(first, second);

    private static Exception? CaptureFailure(Exception? failure, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return Combine(failure, exception);
        }
        return failure;
    }

    private void SelectSlot() => native.Execute("ioctl_select_slot", [slot], 0);

    private void SetAddress(ushort address)
    {
        Out(0x2e, 0x11);
        Out(0x2f, (ulong)(address >> 8));
        Out(0x2e, 0x10);
        Out(0x2f, (ulong)(address & 0xff));
    }

    private void ParkOuterIndex() =>
        native.Execute("ioctl_pio_outb", [outerIndexPort, 0x20], 0);

    private ulong In(ulong port) =>
        native.Execute("ioctl_superio_inb", [port], 1)[0];

    private void Out(ulong port, ulong value) =>
        native.Execute("ioctl_superio_outb", [port, value], 0);

    private sealed class SelectedAccess(PawnIoTransport owner) : IM1TransportAccess
    {
        public byte[] Read(ushort[] addresses) => owner.ReadSelected(addresses);

        public void Write(EcWrite[] writes) => owner.WriteSelected(writes);
    }

    private static PawnIoNative CreateNative()
    {
        string pawnIoPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PawnIO",
            "PawnIOLib.dll");
        return new PawnIoNative(pawnIoPath);
    }

    private static (IPawnIoExecutor Native, byte[] Module) CreateResources()
    {
        IPawnIoExecutor native = CreateNative();
        try
        {
            return (native, LoadLpcModule());
        }
        catch (Exception loadFailure)
        {
            Exception? failure = CaptureFailure(loadFailure, native.Dispose);
            ExceptionDispatchInfo.Capture(failure!).Throw();
            throw;
        }
    }

    private static byte[] LoadLpcModule()
    {
        string directory = File.Exists(
            Path.Combine(AppContext.BaseDirectory, "LibreHardwareMonitorLib.dll"))
                ? AppContext.BaseDirectory
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "FanControl");
        string path = Path.Combine(directory, "LibreHardwareMonitorLib.dll");
        Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(item => item.GetName().Name == "LibreHardwareMonitorLib") ??
            Assembly.LoadFrom(path);
        using Stream stream = assembly.GetManifestResourceStream(
            M1EcLayout.LpcResourceName) ?? throw new InvalidOperationException(
                $"PawnIO resource was not found: {M1EcLayout.LpcResourceName}");
        byte[] module = new byte[checked((int)stream.Length)];
        stream.ReadExactly(module);
        return module;
    }
}

internal sealed class PawnIoNative : IPawnIoExecutor
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int VersionDelegate(out uint version);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int OpenDelegate(out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int LoadDelegate(IntPtr handle, [In] byte[] blob, nuint size);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private delegate int ExecuteDelegate(
        IntPtr handle,
        [MarshalAs(UnmanagedType.LPStr)] string name,
        [In] ulong[] input,
        nuint inputCount,
        [Out] ulong[] output,
        nuint outputCount,
        out nuint returnedCount);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CloseDelegate(IntPtr handle);

    private IntPtr library;
    private IntPtr handle;
    private readonly OpenDelegate open;
    private readonly LoadDelegate load;
    private readonly ExecuteDelegate execute;
    private readonly CloseDelegate close;

    internal PawnIoNative(string libraryPath)
    {
        library = NativeLibrary.Load(libraryPath);
        try
        {
            VersionDelegate version = Export<VersionDelegate>("pawnio_version");
            open = Export<OpenDelegate>("pawnio_open");
            load = Export<LoadDelegate>("pawnio_load");
            execute = Export<ExecuteDelegate>("pawnio_execute");
            close = Export<CloseDelegate>("pawnio_close");
            Check(version(out uint apiVersion), "pawnio_version");
            if ((apiVersion >> 16) != M1EcLayout.PawnIoMajorVersion)
            {
                throw new InvalidOperationException(
                    $"PawnIO API 0x{apiVersion:X8} is not supported; expected major " +
                    $"version {M1EcLayout.PawnIoMajorVersion}.");
            }
        }
        catch
        {
            NativeLibrary.Free(library);
            library = IntPtr.Zero;
            throw;
        }
    }

    public void OpenAndLoad(byte[] module)
    {
        Check(open(out handle), "pawnio_open");
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("pawnio_open returned a null handle.");
        }
        try
        {
            Check(load(handle, module, (nuint)module.Length), "pawnio_load");
        }
        catch (Exception loadFailure)
        {
            try
            {
                Check(close(handle), "pawnio_close");
            }
            catch (Exception closeFailure)
            {
                handle = IntPtr.Zero;
                throw new AggregateException(loadFailure, closeFailure);
            }
            handle = IntPtr.Zero;
            throw;
        }
    }

    public ulong[] Execute(string name, ulong[] input, int outputCount)
    {
        ulong[] output = new ulong[outputCount];
        Check(
            execute(
                handle,
                name,
                input,
                (nuint)input.Length,
                output,
                (nuint)output.Length,
                out nuint returned),
            name);
        if (returned != (nuint)outputCount)
        {
            throw new InvalidOperationException(
                $"PawnIO {name} returned {returned} values; expected {outputCount}.");
        }
        return output;
    }

    public void Dispose()
    {
        Exception? failure = null;
        if (handle != IntPtr.Zero)
        {
            try
            {
                Check(close(handle), "pawnio_close");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            handle = IntPtr.Zero;
        }
        if (library != IntPtr.Zero)
        {
            try
            {
                NativeLibrary.Free(library);
            }
            catch (Exception exception)
            {
                failure = failure is null
                    ? exception
                    : new AggregateException(failure, exception);
            }
            library = IntPtr.Zero;
        }
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static void Check(int result, string operation)
    {
        if (result != 0)
        {
            throw new ExternalException(
                $"{operation} failed with HRESULT 0x{unchecked((uint)result):X8}.",
                result);
        }
    }
}
