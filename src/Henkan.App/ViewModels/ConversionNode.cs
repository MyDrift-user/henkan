using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Henkan.Core.Backends.Editing;

namespace Henkan.App.ViewModels;

public enum NodeKind
{
    /// <summary>A backend: the tool and how it is run.</summary>
    Tool,

    /// <summary>One of a backend's conversions, together with its own menu entry.</summary>
    Conversion,

    /// <summary>A further named set of values on a conversion.</summary>
    Variant,

    /// <summary>The heading for entries whose conversion no longer exists.</summary>
    Orphans,
}

/// <summary>One row of the conversions tree.</summary>
public sealed partial class ConversionNode : ObservableObject
{
    private ConversionNode(NodeKind kind, string backendId)
    {
        this.Kind = kind;
        this.BackendId = backendId;
    }

    public NodeKind Kind { get; }

    public string BackendId { get; }

    /// <summary>The conversion being edited, for a conversion row.</summary>
    public TargetDraft? Target { get; private init; }

    /// <summary>The preset behind a variant row, or behind an entry whose conversion is gone.</summary>
    public string? PresetId { get; private init; }

    public ObservableCollection<ConversionNode> Children { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Subtitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOnMenu { get; set; }

    [ObservableProperty]
    public partial bool IsAvailable { get; set; } = true;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool IsTool => this.Kind == NodeKind.Tool;

    public bool IsConversion => this.Kind == NodeKind.Conversion;

    public bool IsVariant => this.Kind == NodeKind.Variant;

    public string Glyph => this.Kind switch
    {
        NodeKind.Tool => "",
        NodeKind.Conversion => "",
        NodeKind.Variant => "\uE8EC",
        _ => "",
    };

    public static ConversionNode ForTool(string backendId, string title, string subtitle, bool available) => new(NodeKind.Tool, backendId)
    {
        Title = title,
        Subtitle = subtitle,
        IsAvailable = available,
    };

    public static ConversionNode ForConversion(string backendId, TargetDraft target)
    {
        var node = new ConversionNode(NodeKind.Conversion, backendId) { Target = target };
        node.Follow();
        target.PropertyChanged += node.OnTargetChanged;
        return node;
    }

    public static ConversionNode ForVariant(string backendId, string presetId, string title) => new(NodeKind.Variant, backendId)
    {
        PresetId = presetId,
        Title = title,
        Subtitle = "Variant",
    };

    public static ConversionNode ForOrphans() => new(NodeKind.Orphans, string.Empty)
    {
        Title = "Entries without a conversion",
        Subtitle = "Their backend or conversion is gone",
        IsAvailable = false,
    };

    /// <summary>Stops listening to the conversion, so a rebuilt tree does not keep old rows alive.</summary>
    public void Detach()
    {
        if (this.Target is not null)
        {
            this.Target.PropertyChanged -= this.OnTargetChanged;
        }

        foreach (ConversionNode child in this.Children)
        {
            child.Detach();
        }
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs e) => this.Follow();

    private void Follow()
    {
        if (this.Target is { } target)
        {
            this.Title = string.IsNullOrWhiteSpace(target.Label) ? target.Id : target.Label;
            this.Subtitle = $"{target.Category} from {target.InputExtensions}";
        }
    }
}
