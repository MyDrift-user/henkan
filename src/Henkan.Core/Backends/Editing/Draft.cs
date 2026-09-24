using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using Henkan.Core.Options;
using Henkan.Core.Templating;

namespace Henkan.Core.Backends.Editing;

/// <summary>
/// Base for the editable mirrors of the definition records.
/// </summary>
/// <remarks>
/// A <see cref="BackendDefinition"/> and everything under it is an immutable
/// record, which is right for something the conversion engine reads and wrong for
/// something a person edits a field at a time. These drafts are the mutable
/// counterpart: they notify, they can be bound to directly, and they convert back
/// to the records in one step.
/// <para>
/// They live in Core rather than in the application so that the round trip is
/// covered by tests. An editor that silently loses a field is the kind of fault
/// nobody notices until their backend stops working.
/// </para>
/// </remarks>
public abstract class Draft : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Stores a value and says so. Returns false, and says nothing, when it did not change.</summary>
    /// <remarks>
    /// The return value is what keeps a computed property honest: announcing it
    /// only when its source really changed. A combo box bound both ways to a
    /// computed name writes the same value back on every notification, and one
    /// announcement too many turns that into a loop that ends in a stack overflow.
    /// </remarks>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    /// <summary>Raises a change for a property that is computed from another.</summary>
    protected void Notify(string propertyName) =>
        this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Trims and turns an empty string into null, which is how the records spell "absent".</summary>
    protected static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Splits a comma or whitespace separated list into its entries.</summary>
    protected internal static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : [.. value.Split([',', ';', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>Joins a list back into the single line the editor shows.</summary>
    protected internal static string JoinList(IEnumerable<string> values) => string.Join(", ", values);
}

/// <summary>One choice in a choice option.</summary>
public sealed class ChoiceDraft : Draft
{
    private string value = string.Empty;
    private string? label;
    private string? description;

    public string Value
    {
        get => this.value;
        set => this.Set(ref this.value, value);
    }

    public string? Label
    {
        get => this.label;
        set => this.Set(ref this.label, value);
    }

    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    public static ChoiceDraft From(OptionChoice choice) => new()
    {
        Value = choice.Value,
        Label = choice.Label,
        Description = choice.Description,
    };

    public OptionChoice ToChoice() => new()
    {
        Value = this.Value.Trim(),
        Label = Blank(this.Label),
        Description = Blank(this.Description),
    };
}

/// <summary>One user-facing option.</summary>
public sealed class OptionDraft : Draft
{
    private string id = string.Empty;
    private string label = string.Empty;
    private string? description;
    private OptionKind kind = OptionKind.Text;
    private string? group;
    private string? defaultValue;
    private string? unit;
    private string? minimum;
    private string? maximum;
    private string? step;
    private string? visibleWhen;
    private bool advanced;

    public string Id
    {
        get => this.id;
        set => this.Set(ref this.id, value);
    }

    public string Label
    {
        get => this.label;
        set => this.Set(ref this.label, value);
    }

    public string? Description
    {
        get => this.description;
        set => this.Set(ref this.description, value);
    }

    public OptionKind Kind
    {
        get => this.kind;
        set
        {
            if (this.Set(ref this.kind, value))
            {
                this.Notify(nameof(this.KindName));
                this.Notify(nameof(this.HasChoices));
                this.Notify(nameof(this.HasRange));
            }
        }
    }

    /// <summary>The kind by name, so a combo box of strings can drive it.</summary>
    public string KindName
    {
        get => this.Kind.ToString();
        set
        {
            if (Enum.TryParse(value, ignoreCase: true, out OptionKind parsed))
            {
                this.Kind = parsed;
            }
        }
    }

    /// <summary>True for the kind that needs a list of values.</summary>
    public bool HasChoices => this.Kind == OptionKind.Choice;

    /// <summary>True for the kinds where the bounds mean something.</summary>
    public bool HasRange => this.Kind is OptionKind.Range or OptionKind.Integer or OptionKind.Decimal;

    public string? Group
    {
        get => this.group;
        set => this.Set(ref this.group, value);
    }

    public string? Default
    {
        get => this.defaultValue;
        set => this.Set(ref this.defaultValue, value);
    }

    public string? Unit
    {
        get => this.unit;
        set => this.Set(ref this.unit, value);
    }

    /// <summary>Kept as text so a half-typed number does not throw away the field.</summary>
    public string? Minimum
    {
        get => this.minimum;
        set => this.Set(ref this.minimum, value);
    }

    public string? Maximum
    {
        get => this.maximum;
        set => this.Set(ref this.maximum, value);
    }

    public string? Step
    {
        get => this.step;
        set => this.Set(ref this.step, value);
    }

    public string? VisibleWhen
    {
        get => this.visibleWhen;
        set => this.Set(ref this.visibleWhen, value);
    }

    public bool Advanced
    {
        get => this.advanced;
        set => this.Set(ref this.advanced, value);
    }

    public ObservableCollection<ChoiceDraft> Choices { get; } = [];

    public static OptionDraft From(OptionDescriptor option)
    {
        ArgumentNullException.ThrowIfNull(option);

        var draft = new OptionDraft
        {
            Id = option.Id,
            Label = option.Label,
            Description = option.Description,
            Kind = option.Kind,
            Group = option.Group,
            Default = option.Default,
            Unit = option.Unit,
            Minimum = Number(option.Minimum),
            Maximum = Number(option.Maximum),
            Step = Number(option.Step),
            VisibleWhen = option.VisibleWhen,
            Advanced = option.Advanced,
        };

        foreach (OptionChoice choice in option.Choices)
        {
            draft.Choices.Add(ChoiceDraft.From(choice));
        }

        return draft;
    }

    public OptionDescriptor ToDescriptor() => new()
    {
        Id = this.Id.Trim(),
        Label = string.IsNullOrWhiteSpace(this.Label) ? this.Id.Trim() : this.Label.Trim(),
        Description = Blank(this.Description),
        Kind = this.Kind,
        Group = Blank(this.Group),
        Default = Blank(this.Default),
        Unit = Blank(this.Unit),
        Minimum = Parse(this.Minimum),
        Maximum = Parse(this.Maximum),
        Step = Parse(this.Step),
        VisibleWhen = Blank(this.VisibleWhen),
        Advanced = this.Advanced,
        Choices = [.. this.Choices.Where(c => !string.IsNullOrWhiteSpace(c.Value)).Select(c => c.ToChoice())],
    };

    private static string? Number(double? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static double? Parse(string? value) =>
        double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
}

/// <summary>One piece of a command line.</summary>
public sealed class FragmentDraft : Draft
{
    private string value = string.Empty;
    private string? when;
    private string? comment;
    private bool splitAfterExpansion;

    public string Value
    {
        get => this.value;
        set => this.Set(ref this.value, value);
    }

    public string? When
    {
        get => this.when;
        set => this.Set(ref this.when, value);
    }

    public string? Comment
    {
        get => this.comment;
        set => this.Set(ref this.comment, value);
    }

    public bool SplitAfterExpansion
    {
        get => this.splitAfterExpansion;
        set => this.Set(ref this.splitAfterExpansion, value);
    }

    public static FragmentDraft From(ArgumentFragment fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);

        return new FragmentDraft
        {
            Value = fragment.Value,
            When = fragment.When,
            Comment = fragment.Comment,
            SplitAfterExpansion = fragment.SplitAfterExpansion,
        };
    }

    public ArgumentFragment ToFragment() => new()
    {
        Value = this.Value,
        When = Blank(this.When),
        Comment = Blank(this.Comment),
        SplitAfterExpansion = this.SplitAfterExpansion,
    };
}
