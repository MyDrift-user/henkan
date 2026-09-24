using System.Text.RegularExpressions;
using Henkan.Core.Backends;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Henkan.App.ViewModels;

/// <summary>One tool on the Settings page: whether it is here, and if not, what to do.</summary>
public sealed partial class ToolStatus(IConversionBackend backend)
{
    public string Name => backend.Definition.Name;

    public bool IsAvailable => backend.Availability.IsAvailable;

    public string Detail => this.IsAvailable ? Describe(backend) : Explain(backend);

    public string Glyph => this.IsAvailable ? "\uE73E" : "\uE7BA";

    public Brush Brush => (Brush)Application.Current.Resources[
        this.IsAvailable ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];

    public Uri? Homepage => backend.Definition.Executable?.HomepageUrl is { Length: > 0 } url
        ? new Uri(url)
        : backend.Definition.Kind == BackendKind.Office ? new Uri("https://www.microsoft.com/microsoft-365") : null;

    /// <summary>A download link only helps for a tool that is missing.</summary>
    public bool CanGet => !this.IsAvailable && this.Homepage is not null;

    /// <summary>
    /// Why a tool cannot be used, and what the user can do about it, in words
    /// that assume nothing about how Henkan was built.
    /// </summary>
    public static string Explain(IConversionBackend backend)
    {
        string name = backend.Definition.Name;
        string reason = backend.Availability.Reason ?? $"{name} is not available.";

        if (backend.Definition.Kind is not (BackendKind.Process or BackendKind.Archive))
        {
            return reason;
        }

        // A program that is simply not there is the usual case, and its file
        // name means nothing to most people; anything else is said as it is.
        string what = reason.EndsWith("was not found on this computer.", StringComparison.Ordinal)
            ? "Not installed."
            : reason;

        return $"{what} Install {name} to use its conversions. If it is installed somewhere Henkan does not look, choose its program on the Conversions page.";
    }

    private static string Describe(IConversionBackend backend)
    {
        BackendAvailability availability = backend.Availability;
        string? path = availability.ResolvedPath;

        bool included = path is not null
            && path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase);

        // Tools report versions as build strings, such as "n9.0.1-27-g9b05" or
        // "Magick.NET Q16 AnyCPU net8.0 14.16.0"; the number is what matters.
        string version = availability.Version is { Length: > 0 } reported
            ? LastVersion().Match(reported) is { Success: true } match ? match.Value : reported
            : string.Empty;

        return backend.Definition.Kind switch
        {
            BackendKind.Office => $"Installed: {availability.Version}",
            BackendKind.ImageMagick => $"Built into Henkan, version {version}",
            _ when included => version.Length > 0 ? $"Comes with Henkan, version {version}" : "Comes with Henkan",
            _ => version.Length > 0 ? $"Installed, version {version}" : "Installed",
        };
    }

    /// <summary>The last version-like number in a reported version string.</summary>
    [GeneratedRegex(@"\d+\.\d+(?:\.\d+)?(?!.*\d+\.\d+)")]
    private static partial Regex LastVersion();
}
