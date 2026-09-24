using System.Text.Json;
using Henkan.Core.Settings;

namespace Henkan.Core.Presets;

/// <summary>
/// Loads and saves the user's presets.
/// </summary>
/// <remarks>
/// On first run a small starter set is written out. They are ordinary presets
/// with nothing special about them, so the first thing a user does can be to
/// delete or rewrite the lot.
/// </remarks>
public sealed class PresetStore
{
    private readonly string path;
    private List<Preset> presets = [];

    public PresetStore(string? path = null)
    {
        this.path = path ?? HenkanPaths.PresetsFile;
    }

    public IReadOnlyList<Preset> Presets => this.presets;

    public string? LastError { get; private set; }

    public IReadOnlyList<Preset> Load()
    {
        this.LastError = null;

        if (!File.Exists(this.path))
        {
            this.presets = [.. CreateStarterPresets()];
            this.Save(this.presets);
            return this.presets;
        }

        try
        {
            string json = File.ReadAllText(this.path);
            this.presets = PresetList.Normalize(JsonSerializer.Deserialize<List<Preset>>(json, JsonDefaults.Options) ?? []);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            this.LastError = $"Presets could not be read ({ex.Message}).";

            try
            {
                File.Move(this.path, this.path + ".broken", overwrite: true);
            }
            catch (Exception moveError) when (moveError is IOException or UnauthorizedAccessException)
            {
                // Reporting the read failure is enough.
            }

            this.presets = [];
        }

        return this.presets;
    }

    public void Save(IEnumerable<Preset> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        this.presets = PresetList.Normalize(values.OrderBy(p => p.SortOrder).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase));

        Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);

        string temporary = this.path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this.presets, JsonDefaults.Options));
        File.Move(temporary, this.path, overwrite: true);
    }

    public Preset? Find(string id) =>
        this.presets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Presets offering themselves for a file with the given extension.</summary>
    public IEnumerable<Preset> ForExtension(string inputExtension, bool contextMenuOnly = false) =>
        this.presets
            .Where(p => !contextMenuOnly || p.ShowInContextMenu)
            .Where(p => p.Accepts(inputExtension))
            .OrderBy(p => p.SortOrder);

    /// <summary>
    /// The set written on first run.
    /// </summary>
    /// <remarks>
    /// None of these names an input extension. A preset that says nothing
    /// inherits whatever its target can read, so the Explorer menu follows the
    /// backend definitions instead of a second list that drifts out of step.
    /// Presets whose backend is not installed never reach the menu, so listing a
    /// LibreOffice conversion here costs nothing on a machine without it.
    /// </remarks>
    public static IEnumerable<Preset> CreateStarterPresets() => Materialise(Starters, 0);

    /// <summary>The newest starter set. Raise it when adding to <see cref="Starters"/>.</summary>
    public const int StarterVersion = 2;

    /// <summary>
    /// Adds the starter entries introduced after <paramref name="seenVersion"/>
    /// whose conversion has no entry yet, and saves when anything was added.
    /// </summary>
    /// <remarks>
    /// Only newer sets are considered, so an entry from a set the user already
    /// had and then deleted does not come back. A conversion that already has an
    /// entry, shown in the menu or not, is left exactly as the user set it.
    /// </remarks>
    public int AddNewStarters(int seenVersion)
    {
        var taken = new HashSet<string>(this.presets.Select(p => p.TargetKey), StringComparer.OrdinalIgnoreCase);
        int order = PresetList.NextSortOrder(this.presets) - 10;

        List<Preset> added = [.. Materialise(Starters.Where(s => s.Since > seenVersion && !taken.Contains(s.TargetKey)), order)];

        if (added.Count > 0)
        {
            this.Save([.. this.presets, .. added]);
        }

        return added.Count;
    }

    private static IEnumerable<Preset> Materialise(IEnumerable<Starter> starters, int order)
    {
        foreach (Starter starter in starters)
        {
            order += 10;
            Preset preset = Preset.Create(starter.Name, starter.TargetKey) with { SortOrder = order };
            yield return starter.Output is null ? preset : preset with { OutputPathTemplate = starter.Output };
        }
    }

    private sealed record Starter(string Name, string TargetKey, int Since = 1, string? Output = null);

    /// <summary>
    /// The entries a new installation starts with, in menu order.
    /// </summary>
    /// <remarks>
    /// None of these names an input extension. An entry that says nothing
    /// inherits whatever its conversion can read, so the Explorer menu follows the
    /// backend definitions instead of a second list that drifts out of step.
    /// Entries whose backend is not installed never reach the menu, so listing a
    /// LibreOffice conversion here costs nothing on a machine without it. The
    /// menu groups by kind once a file has more than a handful of entries, so a
    /// long list here does not make a long menu.
    /// </remarks>
    private static IEnumerable<Starter> Starters =>
    [
        // Audio and video.
        new("To MP3", "ffmpeg/mp3"),
        new("To AAC (M4A)", "ffmpeg/aac"),
        new("To FLAC", "ffmpeg/flac"),
        new("To Opus", "ffmpeg/opus"),
        new("To WAV", "ffmpeg/wav"),
        new("To MP4 (H.264)", "ffmpeg/mp4"),
        new("To MKV", "ffmpeg/mkv"),
        new("To WebM", "ffmpeg/webm"),
        new("To MOV", "ffmpeg/mov"),
        new("To GIF", "ffmpeg/gif"),
        new("To animated WebP", "ffmpeg/webp-animated"),
        new("To animated PNG", "ffmpeg/apng"),
        new("Grab a frame (PNG)", "ffmpeg/frame-png"),

        // Images.
        new("To PNG", "imagemagick/png"),
        new("To JPEG", "imagemagick/jpg"),
        new("To WebP", "imagemagick/webp"),
        new("To AVIF", "imagemagick/avif"),
        new("To TIFF", "imagemagick/tiff"),
        new("To icon (ICO)", "imagemagick/ico"),
        new("Images to PDF", "pipeline/image-doc-pdf"),

        // PDF and PostScript.
        new("Shrink PDF", "ghostscript/pdf"),
        new("PDF to PNG", "ghostscript/png"),
        new("PDF to JPEG", "ghostscript/jpg"),
        new("PDF to WebP", "pipeline/pdf-webp"),
        new("PDF to text", "ghostscript/txt"),

        // Documents. These point at the multi-step backend rather than at Office
        // or LibreOffice directly, so one entry covers whichever of the two is
        // installed instead of the menu carrying a near-duplicate for each.
        new("To PDF", "pipeline/text-pdf"),
        new("To Word (DOCX)", "pipeline/text-docx"),
        new("To Rich text (RTF)", "pipeline/text-rtf"),
        new("To plain text", "pipeline/text-txt"),
        new("Document to PNG", "pipeline/text-png"),

        new("To PDF (spreadsheet)", "pipeline/sheet-pdf"),
        new("To Excel (XLSX)", "pipeline/sheet-xlsx"),
        new("To CSV", "pipeline/sheet-csv"),
        new("Spreadsheet to PNG", "pipeline/sheet-png"),

        // Archives. These accept anything, so they show up on every file: a
        // plain file goes into the archive, an archive is converted into it.
        new("To ZIP", "sevenzip/zip"),
        new("To 7z", "sevenzip/7z"),
        new("Extract", "sevenzip/extract"),

        new("To PDF (slides)", "pipeline/slides-pdf"),
        new("To PowerPoint (PPTX)", "pipeline/slides-pptx"),
        new("Slides to PNG", "pipeline/slides-png"),

        // Set 2: the rest of what people reach for day to day.
        new("To PDF/A", "pipeline/text-pdfa", 2),
        new("To Word 97-2003 (DOC)", "pipeline/text-doc", 2),
        new("To Word with macros (DOCM)", "pipeline/text-docm", 2),
        new("To Word template (DOTX)", "pipeline/text-dotx", 2),
        new("To Word template with macros (DOTM)", "office/word-dotm", 2),
        new("To OpenDocument text (ODT)", "pipeline/text-odt", 2),
        new("To HTML", "pipeline/text-html", 2),
        new("To EPUB", "pipeline/text-epub", 2),
        new("To XPS", "office/word-xps", 2),

        new("To Excel 97-2003 (XLS)", "pipeline/sheet-xls", 2),
        new("To Excel with macros (XLSM)", "pipeline/sheet-xlsm", 2),
        new("To Excel binary (XLSB)", "office/excel-xlsb", 2),
        new("To Excel template (XLTX)", "pipeline/sheet-xltx", 2),
        new("To OpenDocument spreadsheet (ODS)", "pipeline/sheet-ods", 2),
        new("To HTML (spreadsheet)", "pipeline/sheet-html", 2),

        new("To PowerPoint 97-2003 (PPT)", "pipeline/slides-ppt", 2),
        new("To PowerPoint with macros (PPTM)", "pipeline/slides-pptm", 2),
        new("To PowerPoint show (PPSX)", "pipeline/slides-ppsx", 2),
        new("To PowerPoint template (POTX)", "pipeline/slides-potx", 2),
        new("To OpenDocument presentation (ODP)", "pipeline/slides-odp", 2),
        new("To video (MP4)", "office/powerpoint-mp4", 2),

        new("Merge PDFs", "ghostscript/merge", 2, "{inputDir}\\{inputName} merged.{outputExt}"),
        new("To PDF/A (archival)", "ghostscript/pdfa", 2),
        new("PDF to TIFF", "ghostscript/tiff", 2),

        new("To BMP", "imagemagick/bmp", 2),
        new("To JPEG XL", "imagemagick/jxl", 2),
        new("Images into one PDF", "imagemagick/pdf-combined", 2, "{inputDir}\\{inputName} and more.{outputExt}"),

        new("To OGG Vorbis", "ffmpeg/ogg", 2),
        new("To WMA", "ffmpeg/wma", 2),
        new("To ALAC (Apple Lossless)", "ffmpeg/alac", 2),
        new("To AIFF", "ffmpeg/aiff", 2),
        new("To iPhone ringtone (M4R)", "ffmpeg/m4r", 2),
        new("Extract the audio untouched", "ffmpeg/audio-copy", 2),

        new("To MP4 (H.265, smaller)", "ffmpeg/mp4-hevc", 2),
        new("To AVI", "ffmpeg/avi", 2),
        new("To WMV", "ffmpeg/wmv", 2),
        new("Remove the sound", "ffmpeg/mp4-mute", 2),
        new("Grab a frame (JPEG)", "ffmpeg/frame-jpg", 2),

        new("To TAR.GZ", "sevenzip/tar-gz", 2),
    ];
}
