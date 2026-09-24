using System.Globalization;
using ImageMagick;

namespace Henkan.Core.Backends;

/// <summary>
/// In-process image conversion through Magick.NET.
/// </summary>
/// <remarks>
/// Unlike <see cref="ProcessBackend"/> the execution here is code rather than a
/// command template, but the option <em>schema</em> still comes from the JSON
/// definition. Ranges, defaults, labels and visibility conditions are therefore
/// editable in the same place as everything else; only the set of recognised
/// option identifiers is fixed, and they are listed in docs/backends.md.
/// </remarks>
public sealed class ImageMagickBackend : IConversionBackend
{
    public ImageMagickBackend(BackendDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        this.Definition = definition;
    }

    public BackendDefinition Definition { get; }

    public BackendAvailability Availability { get; private set; } = BackendAvailability.Unknown;

    public Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Touching the native library here surfaces a missing or mismatched
            // runtime as an unavailable backend rather than a crash mid-job.
            string version = MagickNET.Version;
            this.Availability = BackendAvailability.Available(null, version);
        }
        catch (Exception ex)
        {
            this.Availability = BackendAvailability.Unavailable(
                $"The Magick.NET native library could not be loaded: {ex.Message}");
        }

        return Task.FromResult(this.Availability);
    }

    public async Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!this.Availability.IsAvailable)
        {
            await this.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!this.Availability.IsAvailable)
        {
            throw new ConversionFailedException(
                this.Availability.Reason ?? "Magick.NET is not available.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPath)!);
        context.Write($"Magick.NET {this.Availability.Version}: {context.Target.Label}");

        string operation = context.Target.Operation ?? "convert";

        await Task.Run(
            () =>
            {
                switch (operation.ToLowerInvariant())
                {
                    case "icon":
                        WriteIcon(context, cancellationToken);
                        break;

                    case "animation":
                        WriteAnimation(context, cancellationToken);
                        break;

                    case "combine":
                        WriteCombined(context, cancellationToken);
                        break;

                    default:
                        WriteImage(context, cancellationToken);
                        break;
                }
            },
            cancellationToken).ConfigureAwait(false);

        if (!File.Exists(context.OutputPath))
        {
            throw new ConversionFailedException(
                $"Magick.NET produced no file at \"{context.OutputPath}\".");
        }

        context.Report(1d);
    }

    private static void WriteImage(ConversionContext context, CancellationToken cancellationToken)
    {
        using var image = new MagickImage(context.InputPath);
        cancellationToken.ThrowIfCancellationRequested();

        context.Report(0.25d);
        ApplyCommonOptions(image, context);
        cancellationToken.ThrowIfCancellationRequested();

        context.Report(0.75d);
        Write(image, context);
    }

    /// <summary>
    /// Writes with the format the target asked for rather than letting ImageMagick
    /// guess from the file name. The guess is wrong whenever an extension is
    /// ambiguous, and for a few of them it reaches for an external delegate that
    /// is not installed instead of the coder that is built in.
    /// </summary>
    private static void Write(IMagickImage<ushort> image, ConversionContext context)
    {
        if (TryResolveFormat(context.Target.OutputExtension, out MagickFormat format))
        {
            image.Write(context.OutputPath, format);
            return;
        }

        image.Write(context.OutputPath);
    }

    private static void Write(IMagickImageCollection<ushort> frames, ConversionContext context)
    {
        if (TryResolveFormat(context.Target.OutputExtension, out MagickFormat format))
        {
            frames.Write(context.OutputPath, format);
            return;
        }

        frames.Write(context.OutputPath);
    }

    private static bool TryResolveFormat(string outputExtension, out MagickFormat format) =>
        Enum.TryParse(outputExtension.TrimStart('.'), ignoreCase: true, out format)
        && format != MagickFormat.Unknown;

    /// <summary>
    /// Keeps every frame of an animated source. Reading such a file as a single
    /// image silently throws the animation away and keeps frame one, which is
    /// the wrong answer for a GIF or an APNG.
    /// </summary>
    private static void WriteAnimation(ConversionContext context, CancellationToken cancellationToken)
    {
        using var frames = new MagickImageCollection(context.InputPath);
        cancellationToken.ThrowIfCancellationRequested();

        context.Report(0.25d);

        // Frames of an animation are often stored as differences against what
        // came before. Coalescing makes each one a complete picture, so resizing
        // and the other options act on what the viewer actually sees.
        if (frames.Count > 1)
        {
            frames.Coalesce();
        }

        foreach (IMagickImage<ushort> frame in frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCommonOptions(frame, context);
        }

        context.Report(0.75d);

        if (frames.Count > 1)
        {
            frames.Optimize();
        }

        Write(frames, context);
    }

    /// <summary>
    /// Puts every selected image into one file, one page each, in the order
    /// they were selected. For a PDF of scanned pages or a multi-page TIFF.
    /// </summary>
    private static void WriteCombined(ConversionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> inputs = context.Inputs.Count > 0 ? context.Inputs : [context.InputPath];

        using var pages = new MagickImageCollection();

        for (int i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var page = new MagickImage(inputs[i]);
            ApplyCommonOptions(page, context);
            pages.Add(page);

            context.Report(0.8d * (i + 1) / inputs.Count);
        }

        context.Write($"{inputs.Count} image{(inputs.Count == 1 ? string.Empty : "s")} in one file.");
        Write(pages, context);
    }

    /// <summary>
    /// Icons hold several resolutions in one file, so each requested size becomes
    /// its own frame rather than a separate output.
    /// </summary>
    private static void WriteIcon(ConversionContext context, CancellationToken cancellationToken)
    {
        IReadOnlyList<int> sizes = ParseSizes(context.Options.GetString("IconSizes"));

        using var frames = new MagickImageCollection();

        foreach (int size in sizes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var frame = new MagickImage(context.InputPath);
            ApplyCommonOptions(frame, context);
            frame.Resize(new MagickGeometry((uint)size, (uint)size) { IgnoreAspectRatio = false });
            frames.Add(frame);
        }

        context.Report(0.75d);
        Write(frames, context);
    }

    private static void ApplyCommonOptions(IMagickImage<ushort> image, ConversionContext context)
    {
        OptionValueLookup options = new(context);

        if (options.TryGetInt("Quality", out int quality) && quality is > 0 and <= 100)
        {
            image.Quality = (uint)quality;
        }

        double rotation = options.GetDouble("Rotation");
        if (Math.Abs(rotation) > double.Epsilon)
        {
            image.Rotate(rotation);
        }

        string resizeMode = options.GetString("ResizeMode") ?? "None";

        if (resizeMode.Equals("Percent", StringComparison.OrdinalIgnoreCase))
        {
            double percent = options.GetDouble("ScalePercent", 100d);
            if (percent > 0d && Math.Abs(percent - 100d) > double.Epsilon)
            {
                image.Resize(new Percentage(percent));
            }
        }
        else if (resizeMode.Equals("Fit", StringComparison.OrdinalIgnoreCase))
        {
            options.TryGetInt("MaxWidth", out int maxWidth);
            options.TryGetInt("MaxHeight", out int maxHeight);

            if (maxWidth > 0 || maxHeight > 0)
            {
                // A zero dimension tells ImageMagick to derive it from the other,
                // which is exactly the "fit inside a box" behaviour wanted here.
                image.Resize(new MagickGeometry((uint)Math.Max(maxWidth, 0), (uint)Math.Max(maxHeight, 0))
                {
                    IgnoreAspectRatio = false,
                });
            }
        }

        // Formats without an alpha channel render transparency as black unless it
        // is composited away first.
        string? background = options.GetString("BackgroundColor");
        if (!string.IsNullOrWhiteSpace(background) && image.HasAlpha)
        {
            try
            {
                image.BackgroundColor = new MagickColor(background);
                image.Alpha(AlphaOption.Remove);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                context.Write($"Ignoring unrecognised background colour \"{background}\".");
            }
        }

        if (options.GetBoolean("Lossless"))
        {
            image.Settings.SetDefine("webp:lossless", "true");
            image.Settings.SetDefine("heic:lossless", "true");
        }

        if (options.GetBoolean("StripMetadata"))
        {
            image.Strip();
        }
    }

    private static IReadOnlyList<int> ParseSizes(string? value)
    {
        int[] fallback = [16, 32, 48, 256];

        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        List<int> sizes = [.. value
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int size) ? size : 0)
            .Where(size => size is > 0 and <= 1024)
            .Distinct()
            .OrderBy(size => size)];

        return sizes.Count > 0 ? sizes : fallback;
    }

    /// <summary>Thin wrapper so option reads stay terse and consistently coerced.</summary>
    private readonly struct OptionValueLookup(ConversionContext context)
    {
        public string? GetString(string key) => context.Options.GetString(key);

        public bool GetBoolean(string key) => context.Options.GetBoolean(key);

        public double GetDouble(string key, double fallback = 0d) => context.Options.GetDouble(key, fallback);

        public bool TryGetInt(string key, out int value)
        {
            value = context.Options.GetInt32(key, 0);
            return value != 0;
        }
    }
}
