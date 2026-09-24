using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Henkan.App.Services;
using Henkan.Core.Backends;
using Henkan.Core.Backends.Editing;
using Henkan.Core.Conversion;
using Henkan.Core.Options;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Templating;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Henkan.App.ViewModels;

/// <summary>A backend as the tool rows of the tree describe it.</summary>
public sealed class BackendRow(IConversionBackend backend)
{
    public IConversionBackend Backend { get; } = backend;

    public BackendDefinition Definition => this.Backend.Definition;

    public string Name => this.Definition.Name;

    public bool IsBuiltIn => this.Definition.IsBuiltIn;

    public bool IsUserFile => !this.Definition.IsBuiltIn && this.Definition.SourcePath is not null;

    public bool IsAvailable => this.Backend.Availability.IsAvailable;

    public string Origin => this.IsBuiltIn ? "Shipped with Henkan" : "Yours";

    public string Status => this.IsAvailable
        ? this.Backend.Availability.ResolvedPath ?? $"Ready ({this.Backend.Availability.Version})"
        : ToolStatus.Explain(this.Backend);
}

/// <summary>
/// Everything Henkan can convert, and what of it the Explorer menu offers, in one
/// tree: a tool, its conversions, and further variants of a conversion.
/// </summary>
/// <remarks>
/// Backends and presets used to be two pages, and most presets were nothing but a
/// conversion with its defaults, so the second page repeated the first. Here a
/// conversion carries its own menu entry, and a preset only exists separately
/// when it is a genuine variant. On disk nothing changed shape: definitions are
/// still backend files and entries are still presets, because the queue, the
/// command line and the Explorer menu only ever need one of those at a time.
/// </remarks>
public sealed partial class ConversionsViewModel : ObservableObject
{
    private readonly DispatcherQueueTimer watcher;
    private readonly Dictionary<string, ToolState> tools = new(StringComparer.OrdinalIgnoreCase);
    private List<Preset> entries = [];
    private string savedEntries = string.Empty;

    private string? entryId;
    private string entryKey = string.Empty;
    private bool loadingEntry;

    public ConversionsViewModel()
    {
        AppServices.BackendsChanged += (_, _) => this.Refresh();

        // The forms have dozens of fields in nested lists, and wiring a change
        // handler to each of them is a lot of machinery for two readouts. Half a
        // second is quick enough, and only the tool on screen can have changed.
        this.watcher = DispatcherQueue.GetForCurrentThread().CreateTimer();
        this.watcher.Interval = TimeSpan.FromMilliseconds(500);
        this.watcher.Tick += (_, _) => this.Watch();
        this.watcher.Start();

        this.LoadEntries();
        this.Refresh();
    }

    /// <summary>Raised when the option editor must be rebuilt for a new schema.</summary>
    public event EventHandler? SchemaChanged;

    public ObservableCollection<ConversionNode> Nodes { get; } = [];

    public ObservableCollection<string> Problems { get; } = [];

    // ---------------------------------------------------------------- selection

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowsTool), nameof(ShowsShared), nameof(ShowsEntry), nameof(ShowsDefinition),
        nameof(ShowsToolTabs), nameof(ShowsConversionTabs), nameof(ShowsBuiltInNote), nameof(HasNode))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand), nameof(AddVariantCommand), nameof(AddConversionCommand))]
    public partial ConversionNode? SelectedNode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanCopy), nameof(ShowsBuiltInNote))]
    [NotifyCanExecuteChangedFor(nameof(CopyToMineCommand))]
    public partial BackendRow? Selected { get; set; }

    [ObservableProperty]
    public partial BackendDraft? Draft { get; set; }

    [ObservableProperty]
    public partial TargetDraft? SelectedTarget { get; set; }

    /// <summary>For a tool: 0 is the tool itself, 1 the options every conversion shares.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsTool), nameof(ShowsShared), nameof(ShowsBuiltInNote))]
    public partial int ToolSection { get; set; }

    /// <summary>For a conversion: 0 is its menu entry, 1 how it converts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsEntry), nameof(ShowsDefinition), nameof(ShowsBuiltInNote))]
    public partial int ConversionSection { get; set; }

    [ObservableProperty]
    public partial string HeaderTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HeaderDetail { get; set; } = string.Empty;

    public bool HasNode => this.SelectedNode is not null;

    private NodeKind? Kind => this.SelectedNode?.Kind;

    public bool ShowsToolTabs => this.Kind == NodeKind.Tool;

    public bool ShowsConversionTabs => this.Kind == NodeKind.Conversion;

    public bool ShowsTool => this.Kind == NodeKind.Tool && this.ToolSection == 0;

    public bool ShowsShared => this.Kind == NodeKind.Tool && this.ToolSection == 1;

    public bool ShowsEntry => this.Kind == NodeKind.Variant || (this.Kind == NodeKind.Conversion && this.ConversionSection == 0);

    public bool ShowsDefinition => this.Kind == NodeKind.Conversion && this.ConversionSection == 1;

    /// <summary>The note that a shipped backend is changed through a copy, where it applies.</summary>
    public bool ShowsBuiltInNote => this.CanCopy && (this.ShowsTool || this.ShowsShared || this.ShowsDefinition);

    /// <summary>Only a file of your own is saved in place; a shipped one is copied first.</summary>
    public bool CanEdit => this.Selected?.IsUserFile == true;

    public bool CanCopy => this.Selected?.IsBuiltIn == true;

    // ------------------------------------------------------------------ status

    [ObservableProperty]
    public partial string ValidationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(RevertCommand))]
    public partial bool IsDirty { get; set; }

    /// <summary>The selected conversion's command line with every option at its default.</summary>
    [ObservableProperty]
    public partial string CommandPreview { get; set; } = string.Empty;

    // ------------------------------------------------------------------- entry

    [ObservableProperty]
    public partial string EntryName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool EntryOnMenu { get; set; }

    [ObservableProperty]
    public partial string EntryExtensions { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputPathTemplate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int ConflictPolicyIndex { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsArchive))]
    public partial int AfterConversionIndex { get; set; }

    [ObservableProperty]
    public partial string ArchivePathTemplate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OutputPreview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EntryCommandPreview { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AcceptedInputs { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EntryWarning { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EntryDifferences { get; set; } = string.Empty;

    public IReadOnlyList<OptionDescriptor> CurrentSchema { get; private set; } = [];

    public OptionValueSet CurrentValues { get; private set; } = new();

    public bool IsArchive => this.AfterConversionIndex == (int)InputAction.Archive;

    // ------------------------------------------------------------------ lists

    /// <summary>
    /// Spelled exactly as <see cref="BackendDraft.KindName"/> produces them. A
    /// combo box only shows a selection that is literally one of its items.
    /// </summary>
    public static IReadOnlyList<string> Kinds { get; } = [.. Enum.GetNames<BackendKind>()];

    public static IReadOnlyList<string> ProgressStreams { get; } = [.. Enum.GetNames<Henkan.Core.Backends.ProgressStream>()];

    public static IReadOnlyList<string> OptionKinds { get; } = [.. Enum.GetNames<OptionKind>()];

    public static IReadOnlyList<string> ConflictPolicyLabels { get; } =
        ["Rename with a number", "Overwrite", "Skip the file", "Report an error"];

    public static IReadOnlyList<string> AfterConversionLabels { get; } =
        ["Keep the original", "Move the original to the recycle bin", "Delete the original", "Move the original to a folder"];

    // ============================================================ selection

    partial void OnSelectedNodeChanged(ConversionNode? value)
    {
        this.ValidationText = string.Empty;

        ToolState? state = value is null ? null : this.tools.GetValueOrDefault(value.BackendId);

        this.Selected = state?.Row;
        this.Draft = state?.Draft;
        this.SelectedTarget = value?.Kind switch
        {
            NodeKind.Conversion => value.Target,
            NodeKind.Variant => state is null ? null : this.FindTarget(state, this.Entry(value.PresetId)?.TargetKey),
            _ => null,
        };

        this.HeaderTitle = value?.Kind switch
        {
            null => string.Empty,
            NodeKind.Tool => state?.Draft.Name ?? value.Title,
            _ => value.Title,
        };

        this.HeaderDetail = value?.Kind switch
        {
            NodeKind.Tool or NodeKind.Conversion => state?.Row.Status ?? string.Empty,
            NodeKind.Variant when state is not null && this.SelectedTarget is { } target => $"A variant of {target.Label}, in {state.Draft.Name}",
            _ => string.Empty,
        };

        this.LoadEntry(value);
        this.UpdateCommandPreview();
    }

    // ================================================================= tree

    /// <summary>Reads the backends again, keeping unsaved edits to any whose file did not change.</summary>
    private void Refresh()
    {
        var kept = new Dictionary<string, ToolState>(this.tools, StringComparer.OrdinalIgnoreCase);
        this.tools.Clear();

        foreach (IConversionBackend backend in AppServices.Registry.Backends)
        {
            var row = new BackendRow(backend);
            string saved = BackendSerializer.Serialize(backend.Definition);

            if (kept.TryGetValue(backend.Definition.Id, out ToolState? previous) && previous.IsDirty && previous.SavedJson == saved)
            {
                previous.Row = row;
                this.tools[backend.Definition.Id] = previous;
            }
            else
            {
                this.tools[backend.Definition.Id] = ToolState.From(row);
            }
        }

        this.Problems.Clear();

        foreach (BackendLoadProblem problem in AppServices.Registry.Problems)
        {
            this.Problems.Add($"{Path.GetFileName(problem.Source)}: {problem.Message}");
        }

        this.BuildTree();
        this.Watch();
    }

    private void LoadEntries()
    {
        this.entries = [.. AppServices.Presets.Presets];
        this.savedEntries = Serialize(this.entries);
    }

    /// <summary>Rebuilds the tree from the drafts and entries, keeping what was open and selected.</summary>
    private void BuildTree()
    {
        (NodeKind Kind, string Backend, string? Key)? previous = this.SelectedNode is { } node ? this.Identify(node) : null;
        HashSet<string> expanded = [.. this.Nodes.Where(n => n.IsExpanded).Select(n => n.BackendId)];
        HashSet<string> expandedConversions = new([.. this.Nodes.SelectMany(n => n.Children).Where(n => n.IsExpanded && n.Target is not null).Select(this.StableKeyOf)], StringComparer.OrdinalIgnoreCase);

        foreach (ConversionNode old in this.Nodes)
        {
            old.Detach();
        }

        this.Nodes.Clear();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (ToolState state in this.tools.Values.OrderBy(s => s.Draft.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            string id = state.Row.Definition.Id;
            bool available = state.Row.IsAvailable;
            int count = state.Draft.Targets.Count;

            ConversionNode tool = ConversionNode.ForTool(
                id,
                state.Draft.Name,
                $"{count} conversion{(count == 1 ? string.Empty : "s")}  ·  {state.Row.Origin}",
                available);

            tool.IsExpanded = expanded.Contains(id);

            foreach (TargetDraft target in state.Draft.Targets)
            {
                string key = PresetList.Key(id, state.KeyIds[target]);
                claimed.Add(key);

                ConversionNode conversion = ConversionNode.ForConversion(id, target);
                conversion.IsAvailable = available;
                conversion.IsOnMenu = PresetList.EntryFor(this.entries, key)?.ShowInContextMenu == true;
                conversion.IsExpanded = expandedConversions.Contains(key);

                foreach (Preset variant in PresetList.VariantsOf(this.entries, key))
                {
                    ConversionNode child = ConversionNode.ForVariant(id, variant.Id, variant.Name);
                    child.IsOnMenu = variant.ShowInContextMenu;
                    child.IsAvailable = available;
                    conversion.Children.Add(child);
                }

                tool.Children.Add(conversion);
            }

            this.Nodes.Add(tool);
        }

        Preset[] orphans = [.. this.entries.Where(p => !claimed.Contains(p.TargetKey)).OrderBy(p => p.SortOrder)];

        if (orphans.Length > 0)
        {
            ConversionNode heading = ConversionNode.ForOrphans();
            heading.IsExpanded = true;

            foreach (Preset orphan in orphans)
            {
                ConversionNode child = ConversionNode.ForVariant(PresetList.BackendOf(orphan.TargetKey), orphan.Id, orphan.Name);
                child.Subtitle = orphan.TargetKey;
                child.IsAvailable = false;
                child.IsOnMenu = orphan.ShowInContextMenu;
                heading.Children.Add(child);
            }

            this.Nodes.Add(heading);
        }

        ConversionNode? again = previous is { } wanted ? this.FindNode(wanted) : null;
        this.SelectedNode = again ?? this.Nodes.FirstOrDefault();
    }

    private (NodeKind Kind, string Backend, string? Key) Identify(ConversionNode node) => node.Kind switch
    {
        NodeKind.Conversion => (node.Kind, node.BackendId, this.StableKeyOf(node)),
        NodeKind.Variant => (node.Kind, node.BackendId, node.PresetId),
        _ => (node.Kind, node.BackendId, null),
    };

    private ConversionNode? FindNode((NodeKind Kind, string Backend, string? Key) wanted)
    {
        foreach (ConversionNode node in this.AllNodes())
        {
            (NodeKind Kind, string Backend, string? Key) identity = this.Identify(node);

            if (identity.Kind == wanted.Kind
                && identity.Backend.Equals(wanted.Backend, StringComparison.OrdinalIgnoreCase)
                && string.Equals(identity.Key, wanted.Key, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    private IEnumerable<ConversionNode> AllNodes()
    {
        foreach (ConversionNode tool in this.Nodes)
        {
            yield return tool;

            foreach (ConversionNode conversion in tool.Children)
            {
                yield return conversion;

                foreach (ConversionNode variant in conversion.Children)
                {
                    yield return variant;
                }
            }
        }
    }

    /// <summary>The key a conversion row's entries are filed under, which does not move while its identifier is edited.</summary>
    private string? KeyOf(ConversionNode node) =>
        node.Target is { } target && this.tools.TryGetValue(node.BackendId, out ToolState? state) && state.KeyIds.TryGetValue(target, out string? id)
            ? PresetList.Key(node.BackendId, id)
            : null;

    /// <summary>
    /// A conversion row's key that survives the tree being rebuilt from fresh
    /// drafts, which the entry key alone does not: a reload replaces the drafts
    /// the old rows point at.
    /// </summary>
    private string StableKeyOf(ConversionNode node) =>
        this.KeyOf(node) ?? PresetList.Key(node.BackendId, node.Target?.Id ?? string.Empty);

    private TargetDraft? FindTarget(ToolState state, string? key)
    {
        if (key is null)
        {
            return null;
        }

        string id = PresetList.TargetOf(key);
        return state.KeyIds.FirstOrDefault(p => p.Value.Equals(id, StringComparison.OrdinalIgnoreCase)).Key;
    }

    private Preset? Entry(string? id) =>
        id is null ? null : this.entries.FirstOrDefault(p => p.Id == id);

    private ToolState? CurrentTool =>
        this.SelectedNode is { } node ? this.tools.GetValueOrDefault(node.BackendId) : null;

    /// <summary>The conversion row the selection belongs to, when it belongs to one.</summary>
    private ConversionNode? CurrentConversion => this.SelectedNode?.Kind switch
    {
        NodeKind.Conversion => this.SelectedNode,
        NodeKind.Variant => this.AllNodes().FirstOrDefault(n => n.Kind == NodeKind.Conversion && n.Children.Contains(this.SelectedNode)),
        _ => null,
    };

    // ================================================================ entry

    private void LoadEntry(ConversionNode? node)
    {
        this.loadingEntry = true;

        try
        {
            Preset? preset = null;
            string label = string.Empty;
            this.entryId = null;
            this.entryKey = string.Empty;

            if (node?.Kind == NodeKind.Conversion && this.KeyOf(node) is { } key)
            {
                this.entryKey = key;
                label = node.Target!.Label;
                preset = PresetList.EntryFor(this.entries, key);
            }
            else if (node?.Kind == NodeKind.Variant && this.Entry(node.PresetId) is { } variant)
            {
                this.entryKey = variant.TargetKey;
                preset = variant;
            }

            this.entryId = preset?.Id;
            AppSettings settings = AppServices.Settings.Current;

            this.EntryName = preset?.Name ?? (label.Length == 0 ? string.Empty : "To " + label);
            this.EntryOnMenu = preset?.ShowInContextMenu ?? false;
            this.EntryExtensions = preset is null ? string.Empty : string.Join(", ", preset.InputExtensions);
            this.OutputPathTemplate = preset?.OutputPathTemplate ?? settings.DefaultOutputPathTemplate;
            this.ConflictPolicyIndex = (int)(preset?.ConflictPolicy ?? settings.DefaultConflictPolicy);
            this.AfterConversionIndex = (int)(preset?.AfterConversion ?? InputAction.Keep);
            this.ArchivePathTemplate = preset?.ArchivePathTemplate ?? "{inputDir}\\originals\\{inputFileName}";

            (BackendDefinition Definition, ConversionTarget Target)? resolved = this.Resolve(this.entryKey);

            this.CurrentSchema = resolved is { } r ? r.Definition.GetOptionSchema(r.Target) : [];
            this.CurrentValues = OptionValueSet.FromSchema(this.CurrentSchema, preset?.Options);
            this.SchemaChanged?.Invoke(this, EventArgs.Empty);

            this.AcceptedInputs = resolved is { } found ? Describe(found.Target.ExpandInputExtensions()) : string.Empty;
            this.EntryWarning = this.WarningFor(node, resolved);
        }
        finally
        {
            this.loadingEntry = false;
        }

        this.UpdateEntryReadouts();
    }

    private string WarningFor(ConversionNode? node, (BackendDefinition Definition, ConversionTarget Target)? resolved)
    {
        if (node is null || node.Kind is NodeKind.Tool or NodeKind.Orphans)
        {
            return string.Empty;
        }

        if (resolved is null)
        {
            return $"This entry runs {this.entryKey}, which no backend provides any more, so the menu leaves it out. Delete it, or add that conversion back.";
        }

        return this.CurrentTool?.Row is { IsAvailable: false } row
            ? $"{row.Name} is not available, so the menu leaves this out until it is. {row.Status}"
            : string.Empty;
    }

    /// <summary>The definition and conversion behind a key, as currently edited rather than as last saved.</summary>
    private (BackendDefinition Definition, ConversionTarget Target)? Resolve(string key)
    {
        if (key.Length == 0 || !this.tools.TryGetValue(PresetList.BackendOf(key), out ToolState? state) || this.FindTarget(state, key) is not { } draft)
        {
            return null;
        }

        try
        {
            BackendDefinition definition = state.Draft.ToDefinition();
            return definition.FindTarget(draft.Id) is { } target ? (definition, target) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    partial void OnEntryNameChanged(string value) => this.CommitEntry();

    partial void OnEntryOnMenuChanged(bool value) => this.CommitEntry();

    partial void OnEntryExtensionsChanged(string value) => this.CommitEntry();

    partial void OnOutputPathTemplateChanged(string value) => this.CommitEntry();

    partial void OnConflictPolicyIndexChanged(int value) => this.CommitEntry();

    partial void OnAfterConversionIndexChanged(int value) => this.CommitEntry();

    partial void OnArchivePathTemplateChanged(string value) => this.CommitEntry();

    /// <summary>Called by the view whenever the option editor commits a value.</summary>
    public void OnOptionsChanged() => this.CommitEntry();

    /// <summary>
    /// Writes the form back into the working list. A conversion that had no entry
    /// gets one the moment anything about it is changed.
    /// </summary>
    private void CommitEntry()
    {
        if (this.loadingEntry || this.entryKey.Length == 0 || this.SelectedNode is not { } node)
        {
            return;
        }

        Preset basis = this.Entry(this.entryId)
            ?? Preset.Create(this.EntryName, this.entryKey) with { SortOrder = PresetList.NextSortOrder(this.entries) };

        Preset updated = basis with
        {
            Name = string.IsNullOrWhiteSpace(this.EntryName) ? basis.Name : this.EntryName.Trim(),
            Options = this.CurrentValues.ToOverrides(this.CurrentSchema),
            InputExtensions = [.. this.EntryExtensions
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(e => e.TrimStart('.').ToLowerInvariant())
                .Distinct()],
            OutputPathTemplate = string.IsNullOrWhiteSpace(this.OutputPathTemplate)
                ? AppServices.Settings.Current.DefaultOutputPathTemplate
                : this.OutputPathTemplate.Trim(),
            ConflictPolicy = (FileConflictPolicy)this.ConflictPolicyIndex,
            AfterConversion = (InputAction)this.AfterConversionIndex,
            ArchivePathTemplate = this.ArchivePathTemplate,
            ShowInContextMenu = this.EntryOnMenu,
        };

        int index = this.entries.FindIndex(p => p.Id == updated.Id);

        if (index >= 0)
        {
            this.entries[index] = updated;
        }
        else
        {
            this.entries.Add(updated);
        }

        this.entryId = updated.Id;
        node.IsOnMenu = updated.ShowInContextMenu;

        if (node.Kind == NodeKind.Variant)
        {
            node.Title = updated.Name;
            this.HeaderTitle = updated.Name;
        }

        this.UpdateEntryReadouts();
        this.Watch();
    }

    private void UpdateEntryReadouts()
    {
        (BackendDefinition Definition, ConversionTarget Target)? resolved = this.Resolve(this.entryKey);

        if (resolved is not { } found)
        {
            this.OutputPreview = string.Empty;
            this.EntryCommandPreview = string.Empty;
            this.EntryDifferences = string.Empty;
            return;
        }

        VariableContext sample = VariableContext.CreateSample(found.Target.OutputExtension, this.CurrentValues);

        try
        {
            string template = string.IsNullOrWhiteSpace(this.OutputPathTemplate)
                ? AppServices.Settings.Current.DefaultOutputPathTemplate
                : this.OutputPathTemplate;
            this.OutputPreview = TemplateRenderer.RenderText(template, sample.Lookup);
        }
        catch (TemplateException ex)
        {
            this.OutputPreview = ex.Message;
        }

        this.EntryCommandPreview = found.Definition.Kind == BackendKind.Process
            ? RenderCommand(found.Definition, found.Target, this.CurrentValues)
            : string.Empty;

        this.EntryDifferences = this.DescribeDifferences(found);
    }

    /// <summary>For a variant, which of its values differ from the conversion's own entry.</summary>
    private string DescribeDifferences((BackendDefinition Definition, ConversionTarget Target) found)
    {
        if (this.SelectedNode?.Kind != NodeKind.Variant)
        {
            return string.Empty;
        }

        Preset? main = PresetList.EntryFor(this.entries, this.entryKey);
        OptionValueSet reference = OptionValueSet.FromSchema(this.CurrentSchema, main?.Options);
        string against = main?.Name ?? $"the defaults of {found.Target.Label}";

        List<string> differing = [.. this.CurrentSchema
            .Where(o => !string.Equals(this.CurrentValues.GetString(o.Id), reference.GetString(o.Id), StringComparison.Ordinal))
            .Select(o => o.Label)];

        if (main is not null && !string.Equals(main.OutputPathTemplate, this.OutputPathTemplate, StringComparison.Ordinal))
        {
            differing.Add("where the result goes");
        }

        return differing.Count == 0
            ? $"Nothing differs from {against} yet. Change a value below to make this variant worth having."
            : $"Differs from {against} in: {string.Join(", ", differing)}.";
    }

    private static string Describe(IReadOnlyList<string> accepted)
    {
        if (accepted.Count == 0)
        {
            return "Accepts any file.";
        }

        string list = accepted.Count <= 14
            ? string.Join(", ", accepted)
            : string.Join(", ", accepted.Take(14)) + $", and {accepted.Count - 14} more";

        return $"Accepts {list}.";
    }

    // ================================================================ watch

    /// <summary>Updates the dirty flag and the command preview after the forms changed.</summary>
    private void Watch()
    {
        if (this.CurrentTool is { } state)
        {
            string? json = state.Serialize();

            if (json is not null && json != state.LastJson)
            {
                state.LastJson = json;
                this.UpdateCommandPreview();

                if (this.SelectedNode is { Kind: NodeKind.Tool } tool)
                {
                    tool.Title = state.Draft.Name;
                    this.HeaderTitle = state.Draft.Name;
                }
            }
        }

        this.IsDirty = this.tools.Values.Any(s => s.IsDirty) || Serialize(this.entries) != this.savedEntries;
    }

    private void UpdateCommandPreview()
    {
        if (this.CurrentTool is not { } state || this.SelectedTarget is not { } draft)
        {
            this.CommandPreview = string.Empty;
            return;
        }

        try
        {
            BackendDefinition definition = state.Draft.ToDefinition();

            this.CommandPreview = definition.Kind == BackendKind.Process && definition.FindTarget(draft.Id) is { } target
                ? RenderCommand(definition, target, OptionValueSet.FromSchema(definition.GetOptionSchema(target)))
                : string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            this.CommandPreview = string.Empty;
        }
    }

    /// <summary>
    /// The command line a conversion would run on made-up paths. Seeing the
    /// result is the quickest way to find a fragment that renders wrong.
    /// </summary>
    private static string RenderCommand(BackendDefinition definition, ConversionTarget target, OptionValueSet values)
    {
        try
        {
            VariableContext context = VariableContext.CreateSample(target.OutputExtension, values);
            IReadOnlyList<string> arguments = TemplateRenderer.RenderArguments(definition.GetArgumentTemplate(target), context);

            string executable = definition.Executable?.FileName ?? "tool";
            return executable + " " + string.Join(" ", arguments.Select(Quote));
        }
        catch (Exception ex) when (ex is TemplateException or Henkan.Core.Expressions.ExpressionException or ArgumentException or FormatException)
        {
            return "Cannot build the command line: " + ex.Message;
        }
    }

    private static string Quote(string argument) =>
        argument.Length == 0 || argument.Any(char.IsWhiteSpace) || argument.Contains('"')
            ? "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : argument;

    private static string Serialize(List<Preset> presets) =>
        JsonSerializer.Serialize(presets.OrderBy(p => p.SortOrder).ThenBy(p => p.Id), JsonDefaults.Options);

    // ============================================================= commands

    [RelayCommand]
    private async Task ReloadAsync()
    {
        this.IsBusy = true;

        try
        {
            await AppServices.ReloadBackendsAsync();
            this.StatusMessage = "Reloaded.";
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewToolAsync()
    {
        BackendDraft draft = BackendDraft.CreateNew();
        draft.Id = this.FreeId(draft.Id);

        if (await this.WriteAsync(draft))
        {
            await AppServices.ReloadBackendsAsync();
            this.SelectTool(draft.Id);
            this.StatusMessage = $"Created {draft.Name}. Point it at a tool and describe its first conversion.";
        }
    }

    private bool CanAddConversion => this.CurrentTool is not null;

    [RelayCommand(CanExecute = nameof(CanAddConversion))]
    private void AddConversion()
    {
        if (this.CurrentTool is not { } state)
        {
            return;
        }

        var target = new TargetDraft
        {
            Id = this.FreeTargetId(state, "new-format"),
            Label = "New format",
            OutputExtension = "out",
            Category = "Other",
            InputExtensions = "*",
        };

        state.Draft.Targets.Add(target);
        state.KeyIds[target] = target.Id;
        this.SelectConversion(state, target, definition: true);
    }

    [RelayCommand]
    private void DuplicateConversion()
    {
        if (this.CurrentTool is not { } state || this.SelectedTarget is not { } source)
        {
            return;
        }

        TargetDraft copy = TargetDraft.From(source.ToTarget());
        copy.Id = this.FreeTargetId(state, source.Id + "-copy");
        copy.Label += " (copy)";

        state.Draft.Targets.Insert(state.Draft.Targets.IndexOf(source) + 1, copy);
        state.KeyIds[copy] = copy.Id;
        this.SelectConversion(state, copy, definition: true);
    }

    [RelayCommand]
    private void MoveConversion(string direction)
    {
        if (this.CurrentTool is not { } state || this.SelectedNode is not { Kind: NodeKind.Conversion, Target: { } target })
        {
            return;
        }

        int index = state.Draft.Targets.IndexOf(target);
        int destination = direction == "up" ? index - 1 : index + 1;

        if (destination >= 0 && destination < state.Draft.Targets.Count)
        {
            state.Draft.Targets.Move(index, destination);
            this.BuildTree();
        }
    }

    private bool CanAddVariant => this.CurrentConversion is not null;

    [RelayCommand(CanExecute = nameof(CanAddVariant))]
    private void AddVariant()
    {
        if (this.CurrentConversion is not { Target: { } target } conversion || this.KeyOf(conversion) is not { } key)
        {
            return;
        }

        Preset? main = PresetList.EntryFor(this.entries, key);
        AppSettings settings = AppServices.Settings.Current;

        Preset variant = (main ?? Preset.Create(string.Empty, key)) with
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = $"{target.Label} ({PresetList.VariantsOf(this.entries, key).Count() + 2})",
            TargetKey = key,
            IsVariant = true,
            ShowInContextMenu = true,
            Options = new Dictionary<string, string>(main?.Options ?? [], StringComparer.OrdinalIgnoreCase),
            InputExtensions = [.. main?.InputExtensions ?? []],
            OutputPathTemplate = main?.OutputPathTemplate ?? settings.DefaultOutputPathTemplate,
            ConflictPolicy = main?.ConflictPolicy ?? settings.DefaultConflictPolicy,
            SortOrder = PresetList.NextSortOrder(this.entries),
        };

        this.entries.Add(variant);
        conversion.IsExpanded = true;
        this.BuildTree();
        this.SelectedNode = this.AllNodes().FirstOrDefault(n => n.PresetId == variant.Id) ?? this.SelectedNode;
        this.StatusMessage = "Added a variant with the same values. Name it and change what should differ.";
        this.Watch();
    }

    private bool CanDelete => this.SelectedNode?.Kind switch
    {
        NodeKind.Tool => this.CurrentTool?.Row.IsUserFile == true,
        NodeKind.Conversion or NodeKind.Variant => true,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        switch (this.SelectedNode)
        {
            case { Kind: NodeKind.Tool } when this.CurrentTool is { } state:
                await this.DeleteToolAsync(state);
                break;

            case { Kind: NodeKind.Conversion, Target: { } target } node when this.CurrentTool is { } state && this.KeyOf(node) is { } key:
                state.Draft.Targets.Remove(target);
                state.KeyIds.Remove(target);
                this.entries.RemoveAll(p => p.TargetKey.Equals(key, StringComparison.OrdinalIgnoreCase));
                this.SelectedNode = this.Nodes.FirstOrDefault(n => n.BackendId == node.BackendId);
                this.BuildTree();
                this.StatusMessage = $"Removed {target.Label} and its menu entries. Save to make it stick, or undo.";
                break;

            case { Kind: NodeKind.Variant, PresetId: { } id } node:
                ConversionNode? parent = this.CurrentConversion;
                this.entries.RemoveAll(p => p.Id == id);
                this.SelectedNode = parent ?? this.Nodes.FirstOrDefault(n => n.BackendId == node.BackendId);
                this.BuildTree();
                this.StatusMessage = "Removed the variant. Save to make it stick, or undo.";
                break;
        }

        this.Watch();
    }

    private async Task DeleteToolAsync(ToolState state)
    {
        string? path = state.Row.Definition.SourcePath;

        if (path is null || !File.Exists(path))
        {
            return;
        }

        bool confirmed = await ConfirmAsync(
            $"Delete {state.Draft.Name}?",
            "Its file is deleted along with every conversion in it. Menu entries that use it stay behind under \"Entries without a conversion\" until you delete them too.",
            "Delete");

        if (!confirmed)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.StatusMessage = $"Could not delete it: {ex.Message}";
            return;
        }

        this.tools.Remove(state.Row.Definition.Id);
        await AppServices.ReloadBackendsAsync();
        this.StatusMessage = "Deleted.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        this.IsBusy = true;

        try
        {
            await this.SaveAllAsync();
        }
        finally
        {
            this.IsBusy = false;
        }
    }

    private async Task SaveAllAsync()
    {
        this.ValidationText = string.Empty;
        List<string> needCopies = [];
        bool wroteTools = false;

        foreach (ToolState state in this.tools.Values.Where(s => s.IsDirty).ToList())
        {
            if (!state.Row.IsUserFile)
            {
                needCopies.Add(state.Row.Name);
                continue;
            }

            string oldId = state.Row.Definition.Id;

            if (!await this.WriteAsync(state.Draft))
            {
                this.SelectTool(oldId);
                return;
            }

            string newId = state.Draft.Id;
            this.MoveEntries(state, oldId, newId);

            // A changed identifier is a new file name. The old file would
            // otherwise come back as a second copy on the next load.
            if (!newId.Equals(oldId, StringComparison.OrdinalIgnoreCase) && state.Row.Definition.SourcePath is { } oldPath)
            {
                TryDelete(oldPath);
            }

            wroteTools = true;
        }

        if (!this.PersistEntries())
        {
            return;
        }

        if (wroteTools)
        {
            await AppServices.ReloadBackendsAsync();
        }
        else
        {
            ShellIntegration.PublishPresets(AppServices.Presets.Presets, AppServices.Registry);
        }

        this.StatusMessage = needCopies.Count == 0
            ? "Saved."
            : $"Saved, except the changes to how {string.Join(" and ", needCopies)} converts: a backend that ships with Henkan is changed through a copy of your own. Select it and use Save as my own copy.";

        this.Watch();
    }

    /// <summary>Carries the menu entries along when conversions or their backend were renamed.</summary>
    private void MoveEntries(ToolState state, string oldBackendId, string newBackendId)
    {
        foreach ((TargetDraft target, string keyId) in state.KeyIds)
        {
            string from = PresetList.Key(oldBackendId, keyId);
            string to = PresetList.Key(newBackendId, target.Id);

            if (!from.Equals(to, StringComparison.OrdinalIgnoreCase))
            {
                this.entries = PresetList.Retarget(this.entries, from, to);
            }
        }
    }

    private bool PersistEntries()
    {
        try
        {
            AppServices.Presets.Save(this.entries);
            this.entries = [.. AppServices.Presets.Presets];
            this.savedEntries = Serialize(this.entries);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            this.StatusMessage = $"The menu entries could not be saved: {ex.Message}";
            ErrorReport.Show(App.Current.Window, "Menu entries not saved", "Your changes are still here, but they could not be written to disk.", ex);
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCopy))]
    private async Task CopyToMineAsync()
    {
        if (this.CurrentTool is not { } state || !state.Row.IsBuiltIn)
        {
            return;
        }

        string oldId = state.Row.Definition.Id;
        string oldName = state.Row.Definition.Name;

        // A copy has to take a new id, or it would replace the built-in it came
        // from rather than sitting beside it.
        state.Draft.Id = this.FreeId(oldId + "-mine");
        state.Draft.Name = oldName + " (mine)";

        if (!await this.WriteAsync(state.Draft))
        {
            state.Draft.Id = oldId;
            state.Draft.Name = oldName;
            return;
        }

        // The copy is made to be used, so the menu follows it.
        string newId = state.Draft.Id;
        this.MoveEntries(state, oldId, newId);
        this.entries = PresetList.RetargetBackend(this.entries, oldId, newId);
        this.tools.Remove(oldId);

        if (!this.PersistEntries())
        {
            return;
        }

        await AppServices.ReloadBackendsAsync();
        this.SelectTool(newId);
        this.StatusMessage = $"Copied to {state.Draft.Name}. Your menu entries for {oldName} now use the copy; the original is left as it shipped.";
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (string id in this.tools.Keys.ToList())
        {
            this.tools[id] = ToolState.From(this.tools[id].Row);
        }

        this.LoadEntries();
        this.BuildTree();
        this.Watch();
        this.StatusMessage = "Back to what is saved.";
    }

    [RelayCommand]
    private void Validate()
    {
        if (this.Draft is null)
        {
            return;
        }

        IReadOnlyList<ValidationMessage> messages = BackendValidator.Validate(this.Draft.ToDefinition());

        this.ValidationText = messages.Count == 0
            ? "No problems found."
            : string.Join(Environment.NewLine, messages.Select(m => m.ToString()));
    }

    // ------------------------------------------------------ form row commands

    [RelayCommand]
    private static void AddStep(TargetDraft target) =>
        target?.Steps.Add(new StepDraft());

    [RelayCommand]
    private void RemoveStep(StepDraft step)
    {
        foreach (TargetDraft target in this.Draft?.Targets ?? [])
        {
            if (target.Steps.Remove(step))
            {
                return;
            }
        }
    }

    [RelayCommand]
    private static void AddOption(ObservableCollection<OptionDraft> options) =>
        options?.Add(new OptionDraft { Id = "NewOption", Label = "New option" });

    [RelayCommand]
    private void RemoveOption(OptionDraft option)
    {
        foreach (ObservableCollection<OptionDraft> options in this.OptionCollections())
        {
            if (options.Remove(option))
            {
                return;
            }
        }
    }

    [RelayCommand]
    private static void AddChoice(OptionDraft option) =>
        option?.Choices.Add(new ChoiceDraft { Value = "value" });

    [RelayCommand]
    private void RemoveChoice(ChoiceDraft choice)
    {
        foreach (ObservableCollection<OptionDraft> options in this.OptionCollections())
        {
            foreach (OptionDraft option in options)
            {
                if (option.Choices.Remove(choice))
                {
                    return;
                }
            }
        }
    }

    [RelayCommand]
    private static void AddFragment(ObservableCollection<FragmentDraft> fragments) =>
        fragments?.Add(new FragmentDraft { Value = string.Empty });

    [RelayCommand]
    private void RemoveFragment(FragmentDraft fragment)
    {
        foreach (ObservableCollection<FragmentDraft> fragments in this.FragmentCollections())
        {
            if (fragments.Remove(fragment))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Every option list in the draft.
    /// </summary>
    /// <remarks>
    /// A row does not know which list it belongs to, and giving each one a back
    /// reference to its parent would put a UI concern into the editing model for
    /// the sake of a delete button. Looking is cheap at these sizes.
    /// </remarks>
    private IEnumerable<ObservableCollection<OptionDraft>> OptionCollections()
    {
        if (this.Draft is not { } draft)
        {
            yield break;
        }

        yield return draft.SharedOptions;

        foreach (TargetDraft target in draft.Targets)
        {
            yield return target.Options;
        }
    }

    private IEnumerable<ObservableCollection<FragmentDraft>> FragmentCollections()
    {
        if (this.Draft is not { } draft)
        {
            yield break;
        }

        yield return draft.ArgumentPrefix;
        yield return draft.ArgumentSuffix;

        foreach (TargetDraft target in draft.Targets)
        {
            yield return target.Arguments;
        }
    }

    // ----------------------------------------------------------- tool commands

    [RelayCommand]
    private async Task SetExecutableAsync()
    {
        if (this.Selected is null)
        {
            return;
        }

        string? picked = await Pickers.PickFileAsync("exe", "cmd", "bat");

        if (picked is null)
        {
            return;
        }

        AppSettings settings = AppServices.Settings.Current;
        var paths = new Dictionary<string, string>(settings.ToolPaths, StringComparer.OrdinalIgnoreCase)
        {
            [this.Selected.Definition.Id] = picked,
        };

        AppServices.SaveSettings(settings with { ToolPaths = paths });
        await AppServices.ReloadBackendsAsync();
        this.StatusMessage = $"Using {picked}.";
    }

    [RelayCommand]
    private async Task ClearExecutableAsync()
    {
        if (this.Selected is null)
        {
            return;
        }

        AppSettings settings = AppServices.Settings.Current;
        var paths = new Dictionary<string, string>(settings.ToolPaths, StringComparer.OrdinalIgnoreCase);
        paths.Remove(this.Selected.Definition.Id);

        AppServices.SaveSettings(settings with { ToolPaths = paths });
        await AppServices.ReloadBackendsAsync();
        this.StatusMessage = "Henkan finds the program by itself again.";
    }

    [RelayCommand]
    private void OpenFolder()
    {
        Directory.CreateDirectory(HenkanPaths.BackendsDirectory);
        this.Start(new ProcessStartInfo("explorer.exe", $"\"{HenkanPaths.BackendsDirectory}\"") { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenHomepage()
    {
        if (this.Draft?.HomepageUrl is { Length: > 0 } url)
        {
            this.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }

    // ================================================================ helpers

    private void SelectTool(string id)
    {
        this.SelectedNode = this.Nodes.FirstOrDefault(n => n.Kind == NodeKind.Tool && n.BackendId.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? this.SelectedNode;
    }

    private void SelectConversion(ToolState state, TargetDraft target, bool definition)
    {
        ConversionNode? tool = this.Nodes.FirstOrDefault(n => n.BackendId == state.Row.Definition.Id);

        if (tool is not null)
        {
            tool.IsExpanded = true;
        }

        this.BuildTree();
        this.SelectedNode = this.AllNodes().FirstOrDefault(n => n.Target == target) ?? this.SelectedNode;

        if (definition)
        {
            this.ConversionSection = 1;
        }

        this.Watch();
    }

    /// <summary>Validates and writes a definition to the user's backend folder, reporting any problem.</summary>
    private async Task<bool> WriteAsync(BackendDraft draft)
    {
        BackendDefinition definition = draft.ToDefinition();
        IReadOnlyList<ValidationMessage> messages = BackendValidator.Validate(definition);

        this.ValidationText = messages.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, messages.Select(m => m.ToString()));

        if (!BackendValidator.IsUsable(messages))
        {
            this.StatusMessage = $"{definition.Name} was not saved: the problems below have to be fixed first.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(HenkanPaths.BackendsDirectory);
            string path = Path.Combine(HenkanPaths.BackendsDirectory, definition.Id + ".json");
            await BackendSerializer.SaveAsync(definition, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            this.StatusMessage = $"Could not save {definition.Name}: {ex.Message}";
            ErrorReport.Show(App.Current.Window, "Backend not saved", "The file could not be written.", ex);
            return false;
        }
    }

    /// <summary>An id nothing else is using, so a copy never replaces its original.</summary>
    private string FreeId(string wanted)
    {
        string candidate = wanted;

        for (int i = 2; this.tools.ContainsKey(candidate); i++)
        {
            candidate = $"{wanted}-{i}";
        }

        return candidate;
    }

    private string FreeTargetId(ToolState state, string wanted)
    {
        string candidate = wanted;

        for (int i = 2; state.Draft.Targets.Any(t => t.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase))
            || this.entries.Any(p => p.TargetKey.Equals(PresetList.Key(state.Row.Definition.Id, candidate), StringComparison.OrdinalIgnoreCase)); i++)
        {
            candidate = $"{wanted}-{i}";
        }

        return candidate;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorReport.Log(ex, $"Removing the old file {path}");
        }
    }

    private static async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        if (App.Current.Window?.Content?.XamlRoot is not { } root)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
            PrimaryButtonText = action,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void Start(ProcessStartInfo startInfo)
    {
        try
        {
            Process.Start(startInfo)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            this.StatusMessage = $"Could not open it: {ex.Message}";
        }
    }

    /// <summary>One backend as edited: its draft, what is saved, and the keys its entries are filed under.</summary>
    private sealed class ToolState
    {
        public required BackendRow Row { get; set; }

        public required BackendDraft Draft { get; init; }

        public required string SavedJson { get; init; }

        /// <summary>
        /// Each conversion's identifier as its menu entries know it. Editing the
        /// identifier in the form must not orphan the entries before the change is
        /// saved, and on saving they are moved to the new one.
        /// </summary>
        public Dictionary<TargetDraft, string> KeyIds { get; } = [];

        public string LastJson { get; set; } = string.Empty;

        public bool IsDirty => this.LastJson.Length > 0 && this.LastJson != this.SavedJson;

        public static ToolState From(BackendRow row)
        {
            string saved = BackendSerializer.Serialize(row.Definition);
            var state = new ToolState { Row = row, Draft = BackendDraft.From(row.Definition), SavedJson = saved, LastJson = saved };

            foreach (TargetDraft target in state.Draft.Targets)
            {
                state.KeyIds[target] = target.Id;
            }

            return state;
        }

        public string? Serialize()
        {
            try
            {
                return BackendSerializer.Serialize(this.Draft.ToDefinition());
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return null;
            }
        }
    }
}
