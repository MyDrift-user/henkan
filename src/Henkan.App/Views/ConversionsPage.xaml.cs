using System.ComponentModel;
using Henkan.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.Views;

public sealed partial class ConversionsPage : Page
{
    private bool syncing;

    public ConversionsPage()
    {
        this.InitializeComponent();

        this.ViewModel.SchemaChanged += (_, _) => this.ValuesEditor.Load(this.ViewModel.CurrentSchema, this.ViewModel.CurrentValues);
        this.ViewModel.PropertyChanged += this.OnViewModelChanged;
        this.ValuesEditor.Load(this.ViewModel.CurrentSchema, this.ViewModel.CurrentValues);

        this.Loaded += (_, _) => this.SyncTree();
    }

    public ConversionsViewModel ViewModel { get; } = new();

    /// <summary>A tool that is not installed, and what belongs to it, is drawn faded.</summary>
    public static double Dimmed(bool available) => available ? 1.0 : 0.45;

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConversionsViewModel.SelectedNode):
                this.SyncTree();
                break;

            case nameof(ConversionsViewModel.ConversionSection):
                (this.ViewModel.ConversionSection == 1 ? this.DefinitionTab : this.EntryTab).IsSelected = true;
                break;
        }
    }

    /// <summary>
    /// Puts the tree's selection where the view model's is. The tree has no
    /// two-way selected item binding, and the view model moves the selection
    /// itself whenever it adds, removes or rebuilds rows.
    /// </summary>
    private void SyncTree()
    {
        if (this.ViewModel.SelectedNode is not { } node || ReferenceEquals(this.Tree.SelectedItem, node))
        {
            return;
        }

        foreach (ConversionNode ancestor in this.Ancestors(node))
        {
            ancestor.IsExpanded = true;
        }

        this.syncing = true;

        try
        {
            this.Tree.SelectedItem = node;
        }
        finally
        {
            this.syncing = false;
        }
    }

    private IEnumerable<ConversionNode> Ancestors(ConversionNode node)
    {
        foreach (ConversionNode tool in this.ViewModel.Nodes)
        {
            if (tool.Children.Contains(node))
            {
                yield return tool;
            }

            foreach (ConversionNode conversion in tool.Children)
            {
                if (conversion.Children.Contains(node))
                {
                    yield return tool;
                    yield return conversion;
                }
            }
        }
    }

    private void OnTreeSelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (!this.syncing && args.AddedItems.FirstOrDefault() is ConversionNode node)
        {
            this.ViewModel.SelectedNode = node;
        }
    }

    private void OnToolSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag && int.TryParse(tag, out int section))
        {
            this.ViewModel.ToolSection = section;
        }
    }

    private void OnConversionSectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is string tag && int.TryParse(tag, out int section))
        {
            this.ViewModel.ConversionSection = section;
        }
    }

    private void OnOptionsChanged(object? sender, EventArgs e) => this.ViewModel.OnOptionsChanged();
}
