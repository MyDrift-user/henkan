using Henkan.Core.Conversion;
using Microsoft.UI;
using Windows.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace Henkan.App.Views;

public sealed partial class StateToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        ConversionState.Queued => "",
        ConversionState.Running => "",
        ConversionState.Succeeded => "",
        ConversionState.Skipped => "",
        ConversionState.Failed => "",
        ConversionState.Cancelled => "",
        _ => "",
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string key = value switch
        {
            ConversionState.Succeeded => "SystemFillColorSuccessBrush",
            ConversionState.Failed => "SystemFillColorCriticalBrush",
            ConversionState.Skipped or ConversionState.Cancelled => "SystemFillColorCautionBrush",
            ConversionState.Running => "AccentFillColorDefaultBrush",
            _ => "TextFillColorSecondaryBrush",
        };

        return Application.Current.Resources.TryGetValue(key, out object? brush) ? brush : new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>
/// The same colour as <see cref="StateToBrushConverter"/> at low opacity, for the
/// disc the state glyph sits on. A tinted disc reads as a state at a glance where
/// a bare glyph on the background does not.
/// </summary>
public sealed partial class StateToTintConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        Color color = value switch
        {
            ConversionState.Succeeded => Color.FromArgb(255, 15, 123, 15),
            ConversionState.Failed => Color.FromArgb(255, 196, 43, 28),
            ConversionState.Skipped or ConversionState.Cancelled => Color.FromArgb(255, 157, 93, 0),
            _ => Color.FromArgb(255, 0, 95, 184),
        };

        return new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>
/// True while a backend has not reported any progress yet. A bar sitting empty
/// for several seconds looks stuck; one that is moving says the opposite.
/// </summary>
public sealed partial class IndeterminateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double progress && progress <= 0d;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class ProgressVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is ConversionState.Running or ConversionState.Queued ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Pass "invert" as the parameter to show the element when the value is absent.</summary>
public sealed partial class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool empty = value is null || (value is string s && s.Length == 0);

        if (parameter as string == "invert")
        {
            empty = !empty;
        }

        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Pass "invert" as the parameter to flip it.</summary>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool b && b;
        if (parameter is string p && p.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed partial class BoolInverterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is not (bool and true);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => value is not (bool and true);
}

public sealed partial class FileNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string path ? Path.GetFileName(path) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
