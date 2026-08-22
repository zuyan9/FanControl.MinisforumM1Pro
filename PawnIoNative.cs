using System.Reflection;
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
    private readonly Mutex isaMutex = new(false, M1EcLayout.IsaMutexName);
    private readonly IPawnIoExecutor native;
    private readonly byte slot;
    private readonly ulong outerIndexPort;
    private readonly SelectedAccess selectedAccess;

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
        catch
        {
            native.Dispose();
            isaMutex.Dispose();
            throw;
        }
    }

    public T RunExclusive<T>(Func<IM1TransportAccess, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunIsa(() =>
        {
            bool selected = false;
            try
            {
                SelectSlot();
                selected = true;
                return action(selectedAccess);
            }
            finally
            {
                if (selected)
                {
                    Park();
                }
                else
                {
                    ParkOuterIndex();
                }
            }
        });
    }

    public byte[] ReadOuterIdentity() => RunIsa(() =>
    {
        try
        {
            SelectSlot();
            return new[]
            {
                checked((byte)In(0x20)),
                checked((byte)In(0x21)),
                checked((byte)In(0x22)),
            };
        }
        finally
        {
            ParkOuterIndex();
        }
    });

    private byte[] ReadSelected(ushort[] addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
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
        foreach (EcWrite write in writes)
        {
            SetAddress(write.Address);
            Out(0x2e, 0x12);
            Out(0x2f, write.Value);
        }
    }

    public void Dispose()
    {
        native.Dispose();
        isaMutex.Dispose();
    }

    private T RunIsa<T>(Func<T> action)
    {
        bool held = false;
        try
        {
            try
            {
                held = isaMutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                held = true;
            }

            if (!held)
            {
                throw new TimeoutException("Timed out acquiring the ISA mutex.");
            }
            return action();
        }
        finally
        {
            if (held)
            {
                isaMutex.ReleaseMutex();
            }
        }
    }

    private void SelectSlot() => native.Execute("ioctl_select_slot", [slot], 0);

    private void SetAddress(ushort address)
    {
        Out(0x2e, 0x11);
        Out(0x2f, (ulong)(address >> 8));
        Out(0x2e, 0x10);
        Out(0x2f, (ulong)(address & 0xff));
    }

    private void Park()
    {
        try
        {
            Out(0x2e, 0x10);
        }
        finally
        {
            ParkOuterIndex();
        }
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
        catch
        {
            native.Dispose();
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
            open = Export<OpenDelegate>("pawnio_open");
            load = Export<LoadDelegate>("pawnio_load");
            execute = Export<ExecuteDelegate>("pawnio_execute");
            close = Export<CloseDelegate>("pawnio_close");
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
        catch
        {
            close(handle);
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
        if (handle != IntPtr.Zero)
        {
            close(handle);
            handle = IntPtr.Zero;
        }
        if (library != IntPtr.Zero)
        {
            NativeLibrary.Free(library);
            library = IntPtr.Zero;
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
