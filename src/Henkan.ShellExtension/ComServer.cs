using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Henkan.ShellExtension;

/// <summary>
/// The two exports every COM DLL must have, plus the class factory behind them.
/// NativeAOT turns the <see cref="UnmanagedCallersOnlyAttribute"/> methods into
/// real DLL exports, which is what lets a C# assembly stand in for the C++ DLL
/// the surrogate expects.
/// </summary>
internal static unsafe class ComServer
{
    public static readonly StrategyBasedComWrappers Wrappers = new();

    private static int liveObjects;

    public static void AddRef() => Interlocked.Increment(ref liveObjects);

    public static void Release() => Interlocked.Decrement(ref liveObjects);

    /// <summary>
    /// Returns a pointer to the requested interface of a managed object, not its
    /// IUnknown. Out parameters typed as a specific interface must carry that
    /// interface's vtable; the shell calls through it without querying first.
    /// </summary>
    public static nint GetInterfacePointer(object instance, in Guid iid)
    {
        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(instance, CreateComInterfaceFlags.None);
        try
        {
            return Marshal.QueryInterface(unknown, in iid, out nint result) == Hresult.Ok ? result : 0;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
    public static int DllGetClassObject(Guid* clsid, Guid* iid, void** result)
    {
        *result = null;

        if (*clsid != HenkanCommand.Clsid)
        {
            return Hresult.ClassNotAvailable;
        }

        nint unknown = Wrappers.GetOrCreateComInterfaceForObject(new ClassFactory(), CreateComInterfaceFlags.None);
        int hr = Marshal.QueryInterface(unknown, in *iid, out nint requested);
        Marshal.Release(unknown);

        *result = (void*)requested;
        return hr;
    }

    [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
    public static int DllCanUnloadNow() => Volatile.Read(ref liveObjects) == 0 ? Hresult.Ok : Hresult.False;
}

[GeneratedComClass]
internal sealed partial class ClassFactory : IClassFactory
{
    public int CreateInstance(nint outer, in Guid iid, out nint result)
    {
        result = 0;

        if (outer != 0)
        {
            // Aggregation is not supported, and nothing in the shell asks for it.
            return unchecked((int)0x80040110);
        }

        nint unknown = ComServer.Wrappers.GetOrCreateComInterfaceForObject(new HenkanCommand(), CreateComInterfaceFlags.None);
        int hr = Marshal.QueryInterface(unknown, in iid, out result);
        Marshal.Release(unknown);
        return hr;
    }

    public int LockServer(bool lockIt)
    {
        if (lockIt)
        {
            ComServer.AddRef();
        }
        else
        {
            ComServer.Release();
        }

        return Hresult.Ok;
    }
}
