using System.Collections.Frozen;

namespace Henkan.Core.Backends;

/// <summary>
/// Named groups of file extensions, written in a definition as <c>$audio</c> or
/// <c>$image</c> wherever a list of input extensions is expected.
/// </summary>
/// <remarks>
/// Without these, every target has to repeat a long extension list, and the
/// tempting shortcut is <c>*</c>. That shortcut is what makes a menu offer to
/// turn a photograph into an MP3. A family says what a target actually reads,
/// stays short enough that nobody is tempted to cheat, and is available to
/// hand-written user definitions on the same terms as the built-ins.
/// </remarks>
public static class FormatFamilies
{
    /// <summary>Marks a token as a family name rather than a plain extension.</summary>
    public const char Prefix = '$';

    /// <summary>Accepts anything, which stays meaningful for a genuinely format-agnostic target.</summary>
    public const string Any = "*";

    private static readonly FrozenDictionary<string, string[]> Members = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["audio"] =
        [
            "mp3", "mp2", "wav", "w64", "flac", "m4a", "m4b", "aac", "ogg", "oga", "opus", "spx", "wma",
            "aiff", "aif", "aifc", "alac", "ape", "ac3", "eac3", "dts", "amr", "au", "mka", "wv", "ra",
            "tta", "voc", "caf",
        ],
        ["video"] =
        [
            "mp4", "m4v", "mkv", "avi", "mov", "webm", "wmv", "flv", "f4v", "mpg", "mpeg", "mpe", "m2v",
            "ts", "m2ts", "mts", "vob", "ogv", "3gp", "3g2", "asf", "rm", "rmvb", "divx", "mxf", "dv", "y4m",
        ],
        ["raw"] =
        [
            "cr2", "cr3", "nef", "arw", "dng", "raf", "orf", "rw2", "srw", "pef", "sr2", "raw",
            "3fr", "erf", "kdc", "mrw", "nrw", "x3f",
        ],
        ["image"] =
        [
            "png", "apng", "jpg", "jpeg", "jpe", "jfif", "bmp", "dib", "gif", "tif", "tiff", "webp",
            "avif", "heic", "heif", "jxl", "ico", "cur", "tga", "icb", "dds", "ppm", "pgm", "pbm", "pnm",
            "pam", "psd", "psb", "svg", "svgz", "jp2", "j2k", "jpf", "jpx", "pcx", "qoi", "exr", "hdr",
            "xpm", "xbm", "miff", "pict", "pcd", "wpg", "xcf", "emf", "wmf", "$raw",
        ],
        ["text"] =
        [
            "doc", "docx", "docm", "dot", "dotx", "dotm", "odt", "ott", "fodt", "rtf", "txt", "md",
            "markdown", "wps", "wpd", "sxw", "html", "htm", "xhtml", "epub", "abw",
        ],
        ["spreadsheet"] =
        [
            "xls", "xlsx", "xlsm", "xlsb", "xlt", "xltx", "ods", "ots", "fods", "sxc", "csv", "tsv",
            "dif", "slk", "dbf",
        ],
        ["presentation"] =
        [
            "ppt", "pptx", "pptm", "pps", "ppsx", "ppsm", "pot", "potx", "odp", "otp", "fodp", "sxi",
        ],
        ["postscript"] = ["pdf", "ps", "eps", "epsf", "epsi", "ai", "prn"],
        ["archive"] =
        [
            "7z", "zip", "zipx", "rar", "tar", "gz", "tgz", "gzip", "bz2", "tbz", "tbz2", "bzip2",
            "xz", "txz", "lzma", "cab", "iso", "img", "wim", "swm", "esd", "arj",
            "lzh", "lha", "z", "taz", "cpio", "deb", "rpm", "dmg", "xar", "chm", "msi", "vhd",
            "vhdx", "squashfs", "apfs", "udf", "crx",
        ],
        ["document"] = ["$text", "$spreadsheet", "$presentation"],
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each family flattened once at start-up, so a membership test is a set
    /// lookup rather than a walk through the nested lists.
    /// </summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> Resolved = Members.Keys
        .ToFrozenDictionary(
            name => name,
            name => Flatten([Prefix + name]).ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    /// <summary>Family names without the prefix, for documentation and the definition editor.</summary>
    public static IReadOnlyCollection<string> Names { get; } = [.. Members.Keys.Order(StringComparer.Ordinal)];

    /// <summary>The extensions a single family stands for, or an empty set for an unknown name.</summary>
    public static IReadOnlySet<string> Resolve(string family) =>
        Resolved.TryGetValue(family.TrimStart(Prefix), out FrozenSet<string>? members) ? members : FrozenSet<string>.Empty;

    /// <summary>True when the token names a family rather than an extension.</summary>
    public static bool IsFamily(string token) =>
        !string.IsNullOrEmpty(token) && token[0] == Prefix;

    /// <summary>True when the token names a family this build knows.</summary>
    public static bool IsKnownFamily(string token) =>
        IsFamily(token) && Members.ContainsKey(token[1..]);

    /// <summary>
    /// Flattens a list of tokens into plain lower-case extensions. Families are
    /// resolved recursively, duplicates collapse, and an unknown family
    /// contributes nothing rather than throwing, so one typo in a user
    /// definition cannot take the whole file down.
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return Flatten(tokens);
    }

    /// <summary>
    /// True when a file with this extension is accepted by the token list. An
    /// empty list and a <c>*</c> entry both mean everything.
    /// </summary>
    public static bool Matches(IReadOnlyList<string> tokens, string extension)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count == 0)
        {
            return true;
        }

        string candidate = Normalise(extension);

        foreach (string token in tokens)
        {
            if (token == Any)
            {
                return true;
            }

            if (IsFamily(token))
            {
                if (Resolve(token).Contains(candidate))
                {
                    return true;
                }
            }
            else if (Normalise(token).Equals(candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Strips a leading dot and lower-cases, which is how extensions are compared throughout.</summary>
    public static string Normalise(string extension) =>
        string.IsNullOrEmpty(extension) ? string.Empty : extension.TrimStart('.').ToLowerInvariant();

    private static List<string> Flatten(IEnumerable<string> tokens)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string token in tokens)
        {
            Add(token, result, seen, visited);
        }

        return result;
    }

    private static void Add(string token, List<string> result, HashSet<string> seen, HashSet<string> visited)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        string trimmed = token.Trim();

        if (trimmed == Any)
        {
            if (seen.Add(Any))
            {
                result.Add(Any);
            }

            return;
        }

        if (IsFamily(trimmed))
        {
            string name = trimmed[1..];

            // The guard is what makes a family able to name another family:
            // "$document" pulls in "$text" without any risk of a cycle.
            if (!visited.Add(name) || !Members.TryGetValue(name, out string[]? members))
            {
                return;
            }

            foreach (string member in members)
            {
                Add(member, result, seen, visited);
            }

            visited.Remove(name);
            return;
        }

        string extension = Normalise(trimmed);

        if (extension.Length > 0 && seen.Add(extension))
        {
            result.Add(extension);
        }
    }
}
