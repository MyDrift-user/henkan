namespace Henkan.Core.Settings;

/// <summary>What happens to the source file once a conversion succeeds.</summary>
public enum InputAction
{
    /// <summary>Leave it where it is.</summary>
    Keep,

    /// <summary>Send it to the recycle bin, so a mistake is recoverable.</summary>
    Recycle,

    /// <summary>Delete it outright.</summary>
    Delete,

    /// <summary>Move it to the folder named by the preset's archive template.</summary>
    Archive,
}

/// <summary>What happens when the output file already exists.</summary>
public enum FileConflictPolicy
{
    /// <summary>Add a numeric suffix, so nothing is lost.</summary>
    Rename,

    /// <summary>Replace the existing file.</summary>
    Overwrite,

    /// <summary>Leave the existing file and report the job as skipped.</summary>
    Skip,

    /// <summary>Treat it as an error.</summary>
    Fail,
}

/// <summary>How a conversion started from the Explorer menu shows its progress.</summary>
public enum ProgressDisplay
{
    /// <summary>
    /// A small stacked card in a corner of the screen, one per file. Out of the
    /// way, and several at once are legible rather than fighting for one window.
    /// </summary>
    Toast,

    /// <summary>A floating window listing every file in the batch.</summary>
    Window,

    /// <summary>
    /// A Windows notification, which survives in the action centre and does not
    /// take focus. One per batch, with a progress bar while it runs.
    /// </summary>
    Notification,

    /// <summary>
    /// Nothing at all while it works. A failure is still shown, because a
    /// conversion that silently did not happen is the one thing worth a window.
    /// </summary>
    Silent,
}

/// <summary>Which corner of the screen the toasts stack in.</summary>
public enum ScreenCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Application-wide preferences. Per-conversion choices live on a preset instead.</summary>
public sealed record AppSettings
{
    /// <summary>
    /// How many conversions run at once. Defaults to half the logical processors
    /// because the backends are themselves multi-threaded, so running one job per
    /// core mostly makes them contend.
    /// </summary>
    public int MaxParallelConversions { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Explicit executable paths, keyed by backend id. An entry here beats both
    /// the bundled copy and PATH, which is how a user points at their own ffmpeg.
    /// </summary>
    public Dictionary<string, string> ToolPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Output path template used by new presets.</summary>
    public string DefaultOutputPathTemplate { get; init; } = "{inputDir}\\{inputName}.{outputExt}";

    public FileConflictPolicy DefaultConflictPolicy { get; init; } = FileConflictPolicy.Rename;

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; init; } = "System";

    /// <summary>
    /// Keep the progress window open after a batch finishes, rather than letting
    /// it close itself. Off by default: a conversion started from the Explorer
    /// menu that worked has nothing to say, and a window that has to be dismissed
    /// every time is the detour the context menu exists to avoid.
    /// </summary>
    public bool StayOpenWhenFinished { get; init; }

    /// <summary>Write a log file per batch under the logs directory.</summary>
    public bool WriteJobLogs { get; init; }

    /// <summary>How a conversion started from the Explorer menu reports itself.</summary>
    public ProgressDisplay ProgressDisplay { get; init; } = ProgressDisplay.Toast;

    /// <summary>Where <see cref="ProgressDisplay.Toast"/> stacks its cards.</summary>
    public ScreenCorner ToastCorner { get; init; } = ScreenCorner.BottomRight;

    /// <summary>
    /// How long a conversion has to run before anything is shown at all.
    /// </summary>
    /// <remarks>
    /// Converting one small image takes a fraction of a second, and a window that
    /// appears and vanishes again in that time is worse than no window: it is a
    /// flash on screen that cannot be read. Nothing is shown until a conversion
    /// has lasted long enough to be worth reporting, so the quick ones simply
    /// happen.
    /// </remarks>
    public int ProgressDelayMilliseconds { get; init; } = 600;

    /// <summary>How long a finished toast stays on screen before it fades out.</summary>
    public int ToastLingerMilliseconds { get; init; } = 2500;

    /// <summary>Schema version, so a future release can migrate an old file knowingly.</summary>
    public int Version { get; init; } = 1;

    /// <summary>
    /// The newest set of starter menu entries this installation has been given.
    /// A file from before the number existed has had the first set, so that is
    /// the default; entries added in later sets are offered once and never again,
    /// so one the user deleted stays deleted.
    /// </summary>
    public int StarterPresetsVersion { get; init; } = 1;
}
