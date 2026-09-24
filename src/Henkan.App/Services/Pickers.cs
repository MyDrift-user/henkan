using Windows.Storage;
using Windows.Storage.Pickers;

namespace Henkan.App.Services;

/// <summary>
/// File and folder pickers, wired to the main window. WinUI 3 pickers need a
/// window handle before they will show, which is the whole reason this exists.
/// </summary>
public static class Pickers
{
    private static nint WindowHandle =>
        App.Current.Window is { } window ? WinRT.Interop.WindowNative.GetWindowHandle(window) : 0;

    public static async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.Downloads,
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        return [.. files.Select(f => f.Path)];
    }

    public static async Task<string?> PickFileAsync(params string[] extensions)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        if (extensions.Length == 0)
        {
            picker.FileTypeFilter.Add("*");
        }
        else
        {
            foreach (string extension in extensions)
            {
                picker.FileTypeFilter.Add(extension.StartsWith('.') ? extension : "." + extension);
            }
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
