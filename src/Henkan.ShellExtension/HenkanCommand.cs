using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Henkan.ShellExtension;

/// <summary>
/// The top-level "Henkan" entry in the Explorer context menu. Its submenu lists
/// the presets that accept every selected item, and nothing else: the
/// application has nowhere to put a file that was handed to it, so an entry that
/// opened it with the selection would promise something it cannot do.
/// </summary>
/// <remarks>
/// The CLSID here is registered in the app's Package.appxmanifest and must match it.
/// </remarks>
[GeneratedComClass]
[Guid(ClsidString)]
internal sealed partial class HenkanCommand : IExplorerCommand
{
    public const string ClsidString = "7A3E5D2C-4B1F-4E8A-9C6D-2F1B8E4A7D30";

    public static readonly Guid Clsid = new(ClsidString);

    /// <summary>
    /// What was right-clicked, kept from the last <see cref="GetState"/> call.
    /// </summary>
    /// <remarks>
    /// <see cref="EnumSubCommands"/> takes no arguments, so the only way to build
    /// a submenu that suits the selection is to remember the selection. Explorer
    /// always calls GetState on this object first, which is where it comes from.
    /// Filtering has to happen here rather than in each subcommand's GetState:
    /// the Windows 11 menu asks every subcommand for its state, is told Hidden,
    /// and draws it anyway.
    /// </remarks>
    private volatile IReadOnlyList<ShellItem> selection = [];

    public int GetTitle(nint items, out nint title)
    {
        ShellLog.Write("Henkan.GetTitle");
        title = Marshal.StringToCoTaskMemUni("Henkan");
        return Hresult.Ok;
    }

    public int GetIcon(nint items, out nint icon)
    {
        MenuFile menu = MenuFile.Load();
        icon = File.Exists(menu.Executable)
            ? Marshal.StringToCoTaskMemUni(menu.Executable + ",0")
            : 0;
        return icon == 0 ? Hresult.NotImplemented : Hresult.Ok;
    }

    public int GetToolTip(nint items, out nint tooltip)
    {
        tooltip = 0;
        return Hresult.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Clsid;
        return Hresult.Ok;
    }

    public int GetState(nint items, bool okToBeSlow, out uint state)
    {
        if (items == 0)
        {
            state = ExplorerCommandState.Enabled;
            ShellLog.Write("Henkan.GetState(null) -> enabled");
            return Hresult.Ok;
        }

        // Only files, and only files something in the menu can actually convert.
        // Folders, virtual items and a selection nothing applies to get no entry
        // at all, rather than a Henkan submenu holding nothing worth clicking.
        IReadOnlyList<ShellItem> paths = ShellItems.ReadItems(items);
        MenuFile menu = MenuFile.Load();

        this.selection = paths;

        bool anything = paths.Count > 0 && menu.Entries.Exists(entry => entry.AcceptsAll(paths));
        state = anything ? ExplorerCommandState.Enabled : ExplorerCommandState.Hidden;
        ShellLog.Write($"Henkan.GetState({paths.Count} files) -> {state}");
        return Hresult.Ok;
    }

    public int Invoke(nint items, nint bindContext)
    {
        // Never reached while the entry has subcommands. A menu host that ignores
        // the flag gets the application, without the selection: there is nothing
        // in it to drop files into, so carrying them across would only look like
        // something was about to happen.
        ShellLog.Write("Henkan.Invoke (top level)");
        Launcher.Launch(null, [], run: false);
        return Hresult.Ok;
    }

    public int GetFlags(out uint flags)
    {
        flags = ExplorerCommandFlags.HasSubCommands;
        ShellLog.Write("Henkan.GetFlags -> HasSubCommands");
        return Hresult.Ok;
    }

    /// <summary>
    /// The entries as the submenu shows them: one flat list, with the entries of
    /// one kind next to each other in the order the kinds first appear.
    /// </summary>
    /// <remarks>
    /// Nesting a submenu per kind was tried and does not work: the Windows 11
    /// menu draws only one level of submenu for an extension. Explorer still
    /// asks the inner level for its entries, is given them, and then shows the
    /// submenu empty.
    /// </remarks>
    internal static List<IExplorerCommand> Arrange(IReadOnlyList<MenuEntry> entries) =>
        [.. entries
            .GroupBy(e => e.Group)
            .SelectMany(group => group)
            .Select(IExplorerCommand (e) => new PresetCommand(e))];

    public int EnumSubCommands(out nint enumerator)
    {
        MenuFile menu = MenuFile.Load();
        IReadOnlyList<ShellItem> paths = this.selection;

        // An empty selection means GetState never ran, which should not happen.
        // Showing everything is the better way to be wrong.
        List<MenuEntry> matching = [.. menu.Entries.Where(entry => paths.Count == 0 || entry.AcceptsAll(paths))];
        List<IExplorerCommand> commands = Arrange(matching);

        ShellLog.Write($"Henkan.EnumSubCommands -> {commands.Count} of {menu.Entries.Count} for {paths.Count} items");
        enumerator = ComServer.GetInterfacePointer(new CommandEnumerator(commands), in Iid.EnumExplorerCommand);
        return enumerator == 0 ? Hresult.NoInterface : Hresult.Ok;
    }
}

/// <summary>One preset in the submenu.</summary>
[GeneratedComClass]
internal sealed partial class PresetCommand(MenuEntry entry) : IExplorerCommand
{
    public int GetTitle(nint items, out nint title)
    {
        title = Marshal.StringToCoTaskMemUni(entry.Name);
        return Hresult.Ok;
    }

    public int GetIcon(nint items, out nint icon)
    {
        icon = 0;
        return Hresult.NotImplemented;
    }

    public int GetToolTip(nint items, out nint tooltip)
    {
        tooltip = 0;
        return Hresult.NotImplemented;
    }

    public int GetCanonicalName(out Guid name)
    {
        name = Guid.Empty;
        return Hresult.Ok;
    }

    public int GetState(nint items, bool okToBeSlow, out uint state)
    {
        if (items == 0)
        {
            state = ExplorerCommandState.Enabled;
            ShellLog.Write($"Preset '{entry.Name}'.GetState(null) -> enabled");
            return Hresult.Ok;
        }

        IReadOnlyList<ShellItem> paths = ShellItems.ReadItems(items);
        state = paths.Count > 0 && entry.AcceptsAll(paths)
            ? ExplorerCommandState.Enabled
            : ExplorerCommandState.Hidden;
        ShellLog.Write($"Preset '{entry.Name}'.GetState({paths.Count} files) -> {state}");
        return Hresult.Ok;
    }

    public int Invoke(nint items, nint bindContext)
    {
        ShellLog.Write($"Preset '{entry.Name}'.Invoke");
        Launcher.Launch(entry.Id, ShellItems.ReadItems(items), run: true);
        return Hresult.Ok;
    }

    public int GetFlags(out uint flags)
    {
        flags = ExplorerCommandFlags.Default;
        return Hresult.Ok;
    }

    public int EnumSubCommands(out nint enumerator)
    {
        enumerator = 0;
        return Hresult.NotImplemented;
    }
}

/// <summary>Hands the submenu entries to Explorer one page at a time.</summary>
[GeneratedComClass]
internal sealed partial class CommandEnumerator(IReadOnlyList<IExplorerCommand> commands) : IEnumExplorerCommand
{
    private int position;

    public unsafe int Next(uint count, nint commandsOut, nint fetchedOut)
    {
        uint fetched = 0;
        var slots = (nint*)commandsOut;

        if (slots is null)
        {
            return Hresult.InvalidArg;
        }

        while (fetched < count && this.position < commands.Count)
        {
            slots[fetched] = ComServer.GetInterfacePointer(commands[this.position], in Iid.ExplorerCommand);
            this.position++;
            fetched++;
        }

        if (fetchedOut != 0)
        {
            *(uint*)fetchedOut = fetched;
        }

        ShellLog.Write($"Enumerator.Next({count}) -> {fetched}, position {this.position}/{commands.Count}");
        return fetched == count ? Hresult.Ok : Hresult.False;
    }

    public int Skip(uint count)
    {
        this.position = (int)Math.Min(commands.Count, this.position + count);
        return this.position < commands.Count ? Hresult.Ok : Hresult.False;
    }

    public int Reset()
    {
        this.position = 0;
        return Hresult.Ok;
    }

    public int Clone(out nint clone)
    {
        clone = ComServer.GetInterfacePointer(
            new CommandEnumerator(commands) { position = this.position },
            in Iid.EnumExplorerCommand);
        return clone == 0 ? Hresult.NoInterface : Hresult.Ok;
    }
}

/// <summary>Pulls file system paths out of an IShellItemArray.</summary>
internal static class ShellItems
{
    public static IReadOnlyList<ShellItem> ReadItems(nint itemsPointer)
    {
        var paths = new List<ShellItem>();

        if (itemsPointer == 0)
        {
            return paths;
        }

        object wrapper = ComServer.Wrappers.GetOrCreateObjectForComInstance(itemsPointer, CreateObjectFlags.UniqueInstance);
        if (wrapper is not IShellItemArray array || array.GetCount(out uint count) != Hresult.Ok)
        {
            return paths;
        }

        for (uint i = 0; i < count; i++)
        {
            if (array.GetItemAt(i, out nint itemPointer) != Hresult.Ok || itemPointer == 0)
            {
                continue;
            }

            try
            {
                object itemWrapper = ComServer.Wrappers.GetOrCreateObjectForComInstance(itemPointer, CreateObjectFlags.UniqueInstance);
                if (itemWrapper is IShellItem item
                    && item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out nint namePointer) == Hresult.Ok
                    && namePointer != 0)
                {
                    try
                    {
                        string? path = Marshal.PtrToStringUni(namePointer);

                        if (path is null)
                        {
                            continue;
                        }

                        // A folder is a legitimate thing to archive, so it is
                        // kept; what cannot be converted is filtered later by the
                        // entries themselves.
                        if (Directory.Exists(path))
                        {
                            paths.Add(new ShellItem(path, IsDirectory: true));
                        }
                        else if (File.Exists(path))
                        {
                            paths.Add(new ShellItem(path, IsDirectory: false));
                        }
                    }
                    finally
                    {
                        Marshal.FreeCoTaskMem(namePointer);
                    }
                }
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }

        return paths;
    }
}

/// <summary>Starts the app with the selection.</summary>
internal static class Launcher
{
    public static void Launch(string? presetId, IReadOnlyList<ShellItem> paths, bool run)
    {
        // A conversion with nothing to convert is not worth starting a process
        // for. Opening the application on its own is, and that passes no items.
        if (paths.Count == 0 && presetId is not null)
        {
            return;
        }

        MenuFile menu = MenuFile.Load();

        // The app records its own path. If that is stale, the execution alias
        // declared in the package manifest resolves through the shell instead.
        string executable = File.Exists(menu.Executable) ? menu.Executable : "henkan.exe";

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
        };

        if (presetId is not null)
        {
            startInfo.ArgumentList.Add("--preset");
            startInfo.ArgumentList.Add(presetId);
        }

        if (run)
        {
            startInfo.ArgumentList.Add("--run");
        }

        foreach (ShellItem item in paths)
        {
            startInfo.ArgumentList.Add(item.Path);
        }

        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A surrogate has no window to complain in, so the trace log is the
            // only place this can go. It is the difference between a diagnosable
            // "nothing happened" and an undiagnosable one.
            ShellLog.Write($"Launcher failed for \"{executable}\": {ex.Message}");
        }
    }
}
