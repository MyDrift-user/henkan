namespace Henkan.Core.Presets;

/// <summary>
/// The rules that turn a flat list of presets into conversions with an entry of
/// their own and variants beneath it.
/// </summary>
/// <remarks>
/// Presets stay one flat list on disk because everything that runs them, the
/// queue, the command line and the Explorer menu, only ever needs one at a time.
/// The grouping is for the editor, and these rules keep it consistent however
/// the file was written.
/// </remarks>
public static class PresetList
{
    /// <summary>
    /// Makes sure no target has more than one entry of its own. The first one in
    /// menu order keeps the role and any further ones become variants, which is
    /// what a file written before variants existed turns into.
    /// </summary>
    public static List<Preset> Normalize(IEnumerable<Preset> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);

        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Preset>();

        foreach (Preset preset in presets.OrderBy(p => p.SortOrder))
        {
            if (!preset.IsVariant && !owners.Add(preset.TargetKey))
            {
                result.Add(preset with { IsVariant = true });
            }
            else
            {
                result.Add(preset);
            }
        }

        return result;
    }

    /// <summary>The conversion's own entry, or null when it has none yet.</summary>
    public static Preset? EntryFor(IEnumerable<Preset> presets, string targetKey) =>
        presets.FirstOrDefault(p => !p.IsVariant && SameKey(p.TargetKey, targetKey));

    /// <summary>The variants of a conversion, in menu order.</summary>
    public static IEnumerable<Preset> VariantsOf(IEnumerable<Preset> presets, string targetKey) =>
        presets.Where(p => p.IsVariant && SameKey(p.TargetKey, targetKey)).OrderBy(p => p.SortOrder);

    /// <summary>
    /// Points every preset of one target at another key, for a conversion whose
    /// identifier was changed or whose backend was copied under a new one.
    /// </summary>
    public static List<Preset> Retarget(IEnumerable<Preset> presets, string fromKey, string toKey) =>
        [.. presets.Select(p => SameKey(p.TargetKey, fromKey) ? p with { TargetKey = toKey } : p)];

    /// <summary>Points every preset of one backend at another backend of the same shape.</summary>
    public static List<Preset> RetargetBackend(IEnumerable<Preset> presets, string fromBackend, string toBackend) =>
        [.. presets.Select(p => BackendOf(p.TargetKey).Equals(fromBackend, StringComparison.OrdinalIgnoreCase)
            ? p with { TargetKey = toBackend + "/" + TargetOf(p.TargetKey) }
            : p)];

    /// <summary>A sort order after everything already there, so a new entry goes to the end of the menu.</summary>
    public static int NextSortOrder(IEnumerable<Preset> presets) =>
        presets.Select(p => p.SortOrder).DefaultIfEmpty(0).Max() + 10;

    public static string Key(string backendId, string targetId) => backendId + "/" + targetId;

    public static string BackendOf(string targetKey) => targetKey.Split('/', 2)[0];

    public static string TargetOf(string targetKey)
    {
        string[] parts = targetKey.Split('/', 2);
        return parts.Length == 2 ? parts[1] : string.Empty;
    }

    private static bool SameKey(string left, string right) =>
        left.Equals(right, StringComparison.OrdinalIgnoreCase);
}
