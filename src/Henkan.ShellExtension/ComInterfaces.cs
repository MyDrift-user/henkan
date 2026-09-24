using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Henkan.ShellExtension;

/// <summary>
/// The handful of shell interfaces the handler needs, declared for the
/// source-generated COM interop so they work under NativeAOT. Method order is
/// the vtable order and must not change.
/// </summary>
internal static class Hresult
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NotImplemented = unchecked((int)0x80004001);
    public const int NoInterface = unchecked((int)0x80004002);
    public const int Fail = unchecked((int)0x80004005);
    public const int InvalidArg = unchecked((int)0x80070057);
    public const int ClassNotAvailable = unchecked((int)0x80040111);
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(nint outer, in Guid iid, out nint result);

    [PreserveSig]
    int LockServer([MarshalAs(UnmanagedType.Bool)] bool lockIt);
}

[GeneratedComInterface]
[Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
internal partial interface IShellItem
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handlerId, in Guid iid, out nint result);

    [PreserveSig]
    int GetParent(out nint parent);

    [PreserveSig]
    int GetDisplayName(uint form, out nint name);

    [PreserveSig]
    int GetAttributes(uint mask, out uint attributes);

    [PreserveSig]
    int Compare(nint other, uint hint, out int order);
}

[GeneratedComInterface]
[Guid("b63ea76d-1f85-456f-a19c-48159efa858b")]
internal partial interface IShellItemArray
{
    [PreserveSig]
    int BindToHandler(nint bindContext, in Guid handlerId, in Guid iid, out nint result);

    [PreserveSig]
    int GetPropertyStore(int flags, in Guid iid, out nint result);

    [PreserveSig]
    int GetPropertyDescriptionList(nint key, in Guid iid, out nint result);

    [PreserveSig]
    int GetAttributes(int attributeFlags, uint mask, out uint attributes);

    [PreserveSig]
    int GetCount(out uint count);

    [PreserveSig]
    int GetItemAt(uint index, out nint item);

    [PreserveSig]
    int EnumItems(out nint enumerator);
}

[GeneratedComInterface]
[Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9")]
internal partial interface IExplorerCommand
{
    [PreserveSig]
    int GetTitle(nint items, out nint title);

    [PreserveSig]
    int GetIcon(nint items, out nint icon);

    [PreserveSig]
    int GetToolTip(nint items, out nint tooltip);

    [PreserveSig]
    int GetCanonicalName(out Guid name);

    [PreserveSig]
    int GetState(nint items, [MarshalAs(UnmanagedType.Bool)] bool okToBeSlow, out uint state);

    [PreserveSig]
    int Invoke(nint items, nint bindContext);

    [PreserveSig]
    int GetFlags(out uint flags);

    [PreserveSig]
    int EnumSubCommands(out nint enumerator);
}

[GeneratedComInterface]
[Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8")]
internal partial interface IEnumExplorerCommand
{
    /// <remarks>
    /// <paramref name="fetched"/> is a raw pointer because callers may pass null
    /// when <paramref name="count"/> is 1, and Explorer does exactly that.
    /// </remarks>
    [PreserveSig]
    int Next(uint count, nint commands, nint fetched);

    [PreserveSig]
    int Skip(uint count);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out nint clone);
}

/// <summary>Interface ids for the pointers handed back to Explorer.</summary>
internal static class Iid
{
    public static readonly Guid ExplorerCommand = new("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9");

    public static readonly Guid EnumExplorerCommand = new("a88826f8-186f-4987-aade-ea0cef8fbfe8");
}

internal static class ExplorerCommandState
{
    public const uint Enabled = 0;
    public const uint Disabled = 1;
    public const uint Hidden = 8;
}

internal static class ExplorerCommandFlags
{
    public const uint Default = 0;
    public const uint HasSubCommands = 1;
}

internal static class ShellItemDisplayName
{
    /// <summary>SIGDN_FILESYSPATH</summary>
    public const uint FileSystemPath = 0x80058000;
}
