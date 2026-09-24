using System.Globalization;
using Henkan.App.Services;
using Henkan.Core.Options;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Henkan.App.Controls;

/// <summary>
/// Builds a settings form from an option schema.
/// </summary>
/// <remarks>
/// This is the piece that makes a backend definition self-describing: nothing in
/// the application knows what "CRF" or "Bitrate" is. Every row is generated from
/// an <see cref="OptionDescriptor"/>, visibility conditions are re-evaluated on
/// each change, and the result is written straight into an
/// <see cref="OptionValueSet"/>. Adding an option to a JSON file is therefore
/// the whole job of adding it to the UI.
/// </remarks>
public sealed class OptionEditor : UserControl
{
    private readonly StackPanel root = new() { Spacing = 4 };
    private readonly List<Row> rows = [];
    private IReadOnlyList<OptionDescriptor> schema = [];
    private OptionValueSet values = new();
    private bool suppressEvents;

    public OptionEditor()
    {
        this.Content = this.root;
        this.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Raised after any value changed, once the dependent visibility has been updated.</summary>
    public event EventHandler? ValuesChanged;

    public OptionValueSet Values => this.values;

    public IReadOnlyList<OptionDescriptor> Schema => this.schema;

    /// <summary>Rebuilds the form for a new schema and value set.</summary>
    public void Load(IReadOnlyList<OptionDescriptor> newSchema, OptionValueSet newValues)
    {
        ArgumentNullException.ThrowIfNull(newSchema);
        ArgumentNullException.ThrowIfNull(newValues);

        this.schema = newSchema;
        this.values = newValues;
        this.Rebuild();
    }

    public void Clear()
    {
        this.schema = [];
        this.values = new OptionValueSet();
        this.Rebuild();
    }

    private void Rebuild()
    {
        this.suppressEvents = true;
        this.root.Children.Clear();
        this.rows.Clear();

        if (this.schema.Count == 0)
        {
            this.root.Children.Add(new TextBlock
            {
                Text = "This target has no options.",
                Style = (Style)Application.Current.Resources["CaptionStyle"],
            });
            this.suppressEvents = false;
            return;
        }

        // Groups keep their first-seen order so a definition author controls the
        // layout by ordering options, without a separate "groups" section.
        var groups = new List<(string Name, bool Advanced, List<OptionDescriptor> Options)>();

        foreach (OptionDescriptor option in this.schema)
        {
            string groupName = string.IsNullOrWhiteSpace(option.Group) ? "General" : option.Group;
            int index = groups.FindIndex(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase) && g.Advanced == option.Advanced);

            if (index < 0)
            {
                groups.Add((groupName, option.Advanced, []));
                index = groups.Count - 1;
            }

            groups[index].Options.Add(option);
        }

        foreach ((string name, bool advanced, List<OptionDescriptor> options) in groups.OrderBy(g => g.Advanced))
        {
            var panel = new StackPanel { Spacing = 4 };
            foreach (OptionDescriptor option in options)
            {
                Row row = this.CreateRow(option);
                this.rows.Add(row);
                panel.Children.Add(row.Container);
            }

            if (advanced)
            {
                this.root.Children.Add(new Expander
                {
                    Header = name == "General" ? "Advanced" : name,
                    Content = panel,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 8, 0, 0),
                });
            }
            else
            {
                if (groups.Count(g => !g.Advanced) > 1 || name != "General")
                {
                    this.root.Children.Add(new TextBlock
                    {
                        Text = name,
                        Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                    });
                }

                this.root.Children.Add(panel);
            }
        }

        this.UpdateVisibility();
        this.suppressEvents = false;
    }

    private Row CreateRow(OptionDescriptor option)
    {
        FrameworkElement editor = this.CreateEditor(option);
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.MinWidth = 220;

        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        label.Children.Add(new TextBlock { Text = option.Label, TextWrapping = TextWrapping.Wrap });

        if (!string.IsNullOrWhiteSpace(option.Description))
        {
            label.Children.Add(new TextBlock
            {
                Text = option.Description,
                Style = (Style)Application.Current.Resources["CaptionStyle"],
            });
        }

        var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });

        Grid.SetColumn(label, 0);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(label);
        grid.Children.Add(editor);

        var container = new Border
        {
            Child = grid,
            BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };

        return new Row(option, container);
    }

    private FrameworkElement CreateEditor(OptionDescriptor option)
    {
        string current = this.values.GetString(option.Id) ?? option.Default ?? string.Empty;

        switch (option.Kind)
        {
            case OptionKind.Boolean:
            {
                var toggle = new ToggleSwitch
                {
                    IsOn = OptionValueSetExtensions.IsTruthy(current),
                    OnContent = string.Empty,
                    OffContent = string.Empty,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                toggle.Toggled += (_, _) => this.Commit(option, toggle.IsOn ? "true" : "false");
                return toggle;
            }

            case OptionKind.Choice:
            {
                var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (OptionChoice choice in option.Choices)
                {
                    combo.Items.Add(new ComboBoxItem
                    {
                        Content = choice.DisplayLabel + (string.IsNullOrEmpty(option.Unit) || choice.Label is not null ? string.Empty : $" {option.Unit}"),
                        Tag = choice.Value,
                    });
                }

                combo.SelectedIndex = Math.Max(0, option.Choices.ToList().FindIndex(c => c.Value.Equals(current, StringComparison.OrdinalIgnoreCase)));
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is ComboBoxItem item && item.Tag is string value)
                    {
                        this.Commit(option, value);
                    }
                };
                return combo;
            }

            case OptionKind.Range:
            {
                double min = option.Minimum ?? 0;
                double max = option.Maximum ?? 100;
                double step = option.Step ?? 1;
                double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);

                var slider = new Slider
                {
                    Minimum = min,
                    Maximum = max,
                    StepFrequency = step,
                    Value = Math.Clamp(value, min, max),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                var readout = new TextBlock
                {
                    Text = FormatNumber(slider.Value, option.Unit),
                    MinWidth = 64,
                    TextAlignment = TextAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                };

                slider.ValueChanged += (_, e) =>
                {
                    readout.Text = FormatNumber(e.NewValue, option.Unit);
                    this.Commit(option, e.NewValue.ToString(CultureInfo.InvariantCulture));
                };

                var panel = new Grid { ColumnSpacing = 12 };
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(readout, 1);
                panel.Children.Add(slider);
                panel.Children.Add(readout);
                return panel;
            }

            case OptionKind.Integer:
            case OptionKind.Decimal:
            {
                double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out double value);

                var box = new NumberBox
                {
                    Value = value,
                    Minimum = option.Minimum ?? double.MinValue,
                    Maximum = option.Maximum ?? double.MaxValue,
                    SmallChange = option.Step ?? (option.Kind == OptionKind.Integer ? 1 : 0.1),
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                    PlaceholderText = option.Unit ?? string.Empty,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                if (option.Kind == OptionKind.Integer)
                {
                    box.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter
                    {
                        FractionDigits = 0,
                        IsGrouped = false,
                    };
                }

                box.ValueChanged += (_, e) =>
                {
                    if (double.IsNaN(e.NewValue))
                    {
                        return;
                    }

                    double committed = option.Kind == OptionKind.Integer ? Math.Round(e.NewValue) : e.NewValue;
                    this.Commit(option, committed.ToString(CultureInfo.InvariantCulture));
                };

                return WithUnit(box, option.Unit);
            }

            case OptionKind.FilePath:
            case OptionKind.DirectoryPath:
            {
                var text = new TextBox { Text = current, HorizontalAlignment = HorizontalAlignment.Stretch };
                text.TextChanged += (_, _) => this.Commit(option, text.Text);

                var browse = new Button { Content = "Browse" };
                browse.Click += async (_, _) =>
                {
                    string? picked = option.Kind == OptionKind.FilePath
                        ? await Pickers.PickFileAsync()
                        : await Pickers.PickFolderAsync();

                    if (picked is not null)
                    {
                        text.Text = picked;
                    }
                };

                var panel = new Grid { ColumnSpacing = 8 };
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(browse, 1);
                panel.Children.Add(text);
                panel.Children.Add(browse);
                return panel;
            }

            default:
            {
                var text = new TextBox
                {
                    Text = current,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    PlaceholderText = option.Unit ?? string.Empty,
                };
                text.TextChanged += (_, _) => this.Commit(option, text.Text);
                return text;
            }
        }
    }

    private static FrameworkElement WithUnit(FrameworkElement editor, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return editor;
        }

        var panel = new Grid { ColumnSpacing = 8 };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = unit,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["CaptionStyle"],
        };

        Grid.SetColumn(label, 1);
        panel.Children.Add(editor);
        panel.Children.Add(label);
        return panel;
    }

    private static string FormatNumber(double value, string? unit)
    {
        string number = value.ToString(Math.Abs(value - Math.Round(value)) < 0.0001 ? "0" : "0.##", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? number : $"{number} {unit}";
    }

    private void Commit(OptionDescriptor option, string value)
    {
        if (this.suppressEvents)
        {
            return;
        }

        if (string.Equals(this.values.GetString(option.Id), value, StringComparison.Ordinal))
        {
            return;
        }

        this.values.Set(option.Id, value);
        this.UpdateVisibility();
        this.ValuesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateVisibility()
    {
        foreach (Row row in this.rows)
        {
            row.Container.Visibility = this.values.IsVisible(row.Option) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private sealed record Row(OptionDescriptor Option, Border Container);
}

internal static class OptionValueSetExtensions
{
    public static bool IsTruthy(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !(value.Equals("false", StringComparison.OrdinalIgnoreCase)
          || value.Equals("0", StringComparison.Ordinal)
          || value.Equals("no", StringComparison.OrdinalIgnoreCase)
          || value.Equals("off", StringComparison.OrdinalIgnoreCase));
}
