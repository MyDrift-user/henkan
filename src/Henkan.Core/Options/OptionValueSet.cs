using System.Collections;
using System.Globalization;
using Henkan.Core.Expressions;

namespace Henkan.Core.Options;

/// <summary>
/// The values behind an option schema. Everything is stored as a string so a set
/// round-trips through JSON unchanged and templates never have to care about
/// types; typed accessors coerce on the way out.
/// </summary>
public sealed class OptionValueSet : IReadOnlyDictionary<string, string>
{
    private readonly Dictionary<string, string> values;

    public OptionValueSet()
        : this(null)
    {
    }

    public OptionValueSet(IEnumerable<KeyValuePair<string, string>>? initial)
    {
        this.values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (initial is not null)
        {
            foreach ((string key, string value) in initial)
            {
                this.values[key] = value;
            }
        }
    }

    public IEnumerable<string> Keys => this.values.Keys;

    public IEnumerable<string> Values => this.values.Values;

    public int Count => this.values.Count;

    public string this[string key] => this.values[key];

    /// <summary>
    /// Produces a set covering the whole schema: the descriptor's default for
    /// every option, overwritten by anything present in <paramref name="stored"/>.
    /// Values whose option no longer exists in the schema are dropped, which keeps
    /// a preset usable after its backend was edited.
    /// </summary>
    public static OptionValueSet FromSchema(
        IEnumerable<OptionDescriptor> schema,
        IReadOnlyDictionary<string, string>? stored = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var set = new OptionValueSet();

        foreach (OptionDescriptor descriptor in schema)
        {
            string? value = descriptor.Default;

            if (stored is not null && stored.TryGetValue(descriptor.Id, out string? overridden))
            {
                value = overridden;
            }

            set.values[descriptor.Id] = value ?? string.Empty;
        }

        return set;
    }

    public bool ContainsKey(string key) => this.values.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => this.values.TryGetValue(key, out value!);

    public string? GetString(string key) => this.values.TryGetValue(key, out string? value) ? value : null;

    public bool GetBoolean(string key, bool fallback = false)
    {
        string? value = this.GetString(key);

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return !(value.Equals("false", StringComparison.OrdinalIgnoreCase)
              || value.Equals("0", StringComparison.Ordinal)
              || value.Equals("no", StringComparison.OrdinalIgnoreCase)
              || value.Equals("off", StringComparison.OrdinalIgnoreCase));
    }

    public int GetInt32(string key, int fallback = 0) =>
        int.TryParse(this.GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;

    public double GetDouble(string key, double fallback = 0d) =>
        double.TryParse(this.GetString(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    public void Set(string key, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        this.values[key] = value ?? string.Empty;
    }

    public void Set(string key, bool value) => this.Set(key, value ? "true" : "false");

    public void Set(string key, double value) => this.Set(key, value.ToString(CultureInfo.InvariantCulture));

    public bool Remove(string key) => this.values.Remove(key);

    /// <summary>
    /// Evaluates the descriptor's <c>visibleWhen</c> against the current values.
    /// A malformed condition shows the option rather than hiding it, so a typo in
    /// a hand-edited backend never makes settings silently disappear.
    /// </summary>
    public bool IsVisible(OptionDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (string.IsNullOrWhiteSpace(descriptor.VisibleWhen))
        {
            return true;
        }

        try
        {
            return ConditionExpression.Parse(descriptor.VisibleWhen).Evaluate(this.GetString);
        }
        catch (ExpressionException)
        {
            return true;
        }
    }

    /// <summary>
    /// Keeps only the values that differ from their schema default, which is what
    /// gets persisted. A preset therefore records intent rather than a snapshot,
    /// and picks up changed defaults when a backend is updated.
    /// </summary>
    public Dictionary<string, string> ToOverrides(IEnumerable<OptionDescriptor> schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (OptionDescriptor descriptor in schema)
        {
            if (!this.values.TryGetValue(descriptor.Id, out string? value))
            {
                continue;
            }

            if (!string.Equals(value, descriptor.Default ?? string.Empty, StringComparison.Ordinal))
            {
                overrides[descriptor.Id] = value;
            }
        }

        return overrides;
    }

    public OptionValueSet Clone() => new(this.values);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => this.values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
