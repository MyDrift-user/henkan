using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Henkan.Core.Tools;

namespace Henkan.Core.Backends;

/// <summary>
/// Archive conversion through the bundled 7-Zip.
/// </summary>
/// <remarks>
/// <para>
/// Compressing a file is one command and would fit a plain process backend, but
/// converting one archive into another is not: it is an extraction followed by a
/// repack, with a directory in between rather than a file. The multi-step backend
/// passes files between its legs and cannot carry a directory, so this does the
/// two runs itself.
/// </para>
/// <para>
/// As with the ImageMagick and Office backends, only the recognised option
/// identifiers are fixed here. Their labels, ranges, defaults and visibility still
/// come from the JSON definition, so the shipped formats can be retuned and new
/// ones added without touching this file.
/// </para>
/// </remarks>
public sealed partial class ArchiveBackend : IConversionBackend
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private readonly ToolLocator locator;
    private string? executablePath;

    public ArchiveBackend(BackendDefinition definition, ToolLocator locator)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(locator);

        if (definition.Executable is null)
        {
            throw new ArgumentException(
                $"Backend \"{definition.Id}\" declares no executable.",
                nameof(definition));
        }

        this.Definition = definition;
        this.locator = locator;
    }

    public BackendDefinition Definition { get; }

    public BackendAvailability Availability { get; private set; } = BackendAvailability.Unknown;

    public async Task<BackendAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        ExecutableDefinition executable = this.Definition.Executable!;

        ToolResolution resolution = this.locator.Resolve(
            this.Definition.Id,
            executable.FileName,
            executable.BundledPath,
            executable.SearchPaths);

        if (!resolution.Found)
        {
            this.executablePath = null;
            return this.Availability = BackendAvailability.Unavailable(
                resolution.Reason ?? $"\"{executable.FileName}\" was not found.");
        }

        this.executablePath = resolution.Path;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            // 7-Zip prints its banner and exits non-zero when given no command,
            // so the exit code says nothing useful and only the banner is read.
            (_, string output) = await this.RunAsync([], null, timeout.Token).ConfigureAwait(false);
            Match version = VersionPattern().Match(output);

            return this.Availability = BackendAvailability.Available(
                resolution.Path,
                version.Success ? version.Groups[1].Value : null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
        {
            return this.Availability = BackendAvailability.Unavailable(
                $"\"{resolution.Path}\" could not be run: {ex.Message}");
        }
    }

    public async Task ExecuteAsync(ConversionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (this.executablePath is null)
        {
            await this.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }

        if (this.executablePath is null)
        {
            throw new ConversionFailedException(this.Availability.Reason ?? "7-Zip is not available.");
        }

        if (context.Target.OutputIsDirectory)
        {
            await this.ExtractAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        string format = context.Target.Setting("format")
            ?? throw new ConversionFailedException(
                $"Target \"{context.Target.Id}\" does not say which archive format to write.");

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutputPath)!);

        // Built under a scratch name and moved into place at the end. 7-Zip adds
        // to an existing archive rather than replacing it, and deleting the
        // target first only trades that for a worse problem: Windows keeps the
        // name reserved while anything still holds a handle on it, so the create
        // that follows fails with "the file exists".
        string scratch = Path.Combine(
            context.TemporaryDirectory,
            "building-" + Path.GetFileName(context.OutputPath));

        // A compressed tar is two formats, and 7-Zip writes one at a time: the
        // tar is built first and then compressed as a single file.
        (string inner, string? outer) = SplitCompound(format);
        string built = outer is null ? scratch : Path.Combine(context.TemporaryDirectory, "building.tar");

        if (Repacks(context))
        {
            await this.RepackAsync(context, inner, built, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await this.CompressAsync(context, inner, Sources(context), built, null, cancellationToken).ConfigureAwait(false);
        }

        if (outer is not null)
        {
            if (!File.Exists(built))
            {
                throw new ConversionFailedException("7-Zip reported success but produced no tar to compress.");
            }

            context.Report(0.7d);
            context.Write($"Compressing the tar as {outer}.");
            await this.CompressAsync(context, outer, [built], scratch, null, cancellationToken).ConfigureAwait(false);
        }

        if (!File.Exists(scratch))
        {
            throw new ConversionFailedException("7-Zip reported success but produced no archive.");
        }

        File.Move(scratch, context.OutputPath, overwrite: true);
        context.Report(1d);
    }

    /// <summary>
    /// Unpacks an archive into a folder of its own next to it.
    /// </summary>
    /// <remarks>
    /// Everything comes out under one folder named after the archive, never
    /// loose in the folder the archive was sitting in. An archive whose contents
    /// are not already in a folder is common enough that the alternative is a
    /// mess someone has to clean up by hand.
    /// </remarks>
    private async Task ExtractAsync(ConversionContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(context.OutputPath);
        context.Write($"Unpacking into \"{context.OutputPath}\".");

        var arguments = new List<string> { "x", context.InputPath, $"-o{context.OutputPath}", "-y" };
        AddPassword(arguments, context, reading: true);

        (int code, string output) = await this.RunAsync(arguments, null, cancellationToken).ConfigureAwait(false);
        context.Write(output);

        if (code != 0)
        {
            throw new ConversionFailedException(Explain(code, output, "unpack"));
        }

        if (!Directory.EnumerateFileSystemEntries(context.OutputPath).Any())
        {
            throw new ConversionFailedException("The archive unpacked to nothing.");
        }

        Flatten(context);

        int files = Directory.EnumerateFiles(context.OutputPath, "*", SearchOption.AllDirectories).Count();
        context.Write($"Unpacked {files} file{(files == 1 ? string.Empty : "s")}.");
        context.Report(1d);
    }

    /// <summary>
    /// Removes the wrapper folder when the archive already had one of its own.
    /// </summary>
    /// <remarks>
    /// Unpacking into a folder named after the archive is right for an archive
    /// whose files sit loose at the top. For one that already contains a single
    /// folder it gives project\project\..., which nobody wants and everybody has
    /// to tidy up by hand.
    /// </remarks>
    private static void Flatten(ConversionContext context)
    {
        string[] entries = [.. Directory.EnumerateFileSystemEntries(context.OutputPath)];

        if (entries.Length != 1 || !Directory.Exists(entries[0]))
        {
            return;
        }

        string wrapper = entries[0];

        try
        {
            foreach (string item in Directory.EnumerateFileSystemEntries(wrapper))
            {
                string destination = Path.Combine(context.OutputPath, Path.GetFileName(item));

                if (Directory.Exists(item))
                {
                    Directory.Move(item, destination);
                }
                else
                {
                    File.Move(item, destination);
                }
            }

            Directory.Delete(wrapper);
            context.Write($"The archive already contained one folder, so \"{Path.GetFileName(wrapper)}\" was not nested again.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The files are all extracted either way; one extra folder is a
            // cosmetic problem and not worth failing the conversion over.
            context.Write($"Could not remove the wrapper folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Splits a compressed tar into the archive and the compression around it,
    /// as <c>tar.gz</c> into tar and gzip. Any other format stands alone.
    /// </summary>
    public static (string Inner, string? Outer) SplitCompound(string format) => format.ToLowerInvariant() switch
    {
        "tar.gz" or "tgz" => ("tar", "gzip"),
        "tar.xz" or "txz" => ("tar", "xz"),
        "tar.bz2" or "tbz2" => ("tar", "bzip2"),
        _ => (format, null),
    };

    /// <summary>Everything the job was given, or its single input.</summary>
    private static IReadOnlyList<string> Sources(ConversionContext context) =>
        context.Inputs.Count > 0 ? context.Inputs : [context.InputPath];

    /// <summary>
    /// Whether this run converts an archive or wraps a file in one.
    /// </summary>
    /// <remarks>
    /// One target per output format rather than two. Asked for a ZIP, a RAR is
    /// converted into one and a spreadsheet is put into one, which is what either
    /// file being dropped on "To ZIP" plainly means. A target may still force
    /// either behaviour with its operation, and the Repack option turns the
    /// conversion off for someone who really does want an archive inside an
    /// archive.
    /// </remarks>
    private static bool Repacks(ConversionContext context)
    {
        string? operation = context.Target.Operation;

        if (string.Equals(operation, "repack", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(operation, "compress", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Several things at once are packed together, never unpacked; and a
        // folder has no extension and nothing to unpack either.
        return Sources(context).Count == 1
            && !Directory.Exists(context.InputPath)
            && context.Options.GetBoolean("Repack", true)
            && FormatFamilies.Matches(["$archive"], Path.GetExtension(context.InputPath));
    }

    /// <summary>Unpacks the archive and packs its contents into the new format.</summary>
    private async Task RepackAsync(ConversionContext context, string format, string archive, CancellationToken cancellationToken)
    {
        string unpacked = Path.Combine(context.TemporaryDirectory, "unpacked");
        Directory.CreateDirectory(unpacked);

        context.Write($"Unpacking \"{Path.GetFileName(context.InputPath)}\".");

        var extract = new List<string> { "x", context.InputPath, $"-o{unpacked}", "-y" };
        AddPassword(extract, context, reading: true);

        (int code, string output) = await this.RunAsync(extract, null, cancellationToken).ConfigureAwait(false);
        context.Write(output);

        if (code != 0)
        {
            throw new ConversionFailedException(Explain(code, output, "unpack"));
        }

        if (!Directory.EnumerateFileSystemEntries(unpacked).Any())
        {
            throw new ConversionFailedException("The archive unpacked to nothing.");
        }

        context.Report(0.5d);
        context.Write($"Packing as {format}.");

        // Packed from inside the unpacked directory so the archive holds the
        // files themselves rather than one folder containing everything.
        await this.CompressAsync(context, format, ["*"], archive, unpacked, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompressAsync(
        ConversionContext context,
        string format,
        IReadOnlyList<string> inputs,
        string archive,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(archive)!);

        var arguments = new List<string> { "a", $"-t{format}", archive };
        arguments.AddRange(inputs);
        arguments.Add("-y");

        int level = Math.Clamp(context.Options.GetInt32("Level", 5), 0, 9);
        arguments.Add($"-mx={level}");

        if (context.Options.GetBoolean("SolidArchive", true) && format is "7z")
        {
            arguments.Add("-ms=on");
        }

        AddPassword(arguments, context, reading: false);

        (int code, string output) = await this.RunAsync(arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        context.Write(output);

        if (code != 0)
        {
            throw new ConversionFailedException(Explain(code, output, "pack"));
        }
    }

    private static void AddPassword(List<string> arguments, ConversionContext context, bool reading)
    {
        string? password = context.Options.GetString("Password");

        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        arguments.Add($"-p{password}");

        // Hiding the file names as well only applies when writing, and only 7z
        // can do it; zip encrypts the contents but always lists what is inside.
        if (!reading && context.Options.GetBoolean("EncryptNames") && context.Target.Setting("format") == "7z")
        {
            arguments.Add("-mhe=on");
        }
    }

    /// <summary>Turns a 7-Zip exit code into something worth reading.</summary>
    private static string Explain(int code, string output, string what)
    {
        string reason = code switch
        {
            1 => "Some files could not be read.",
            2 => "7-Zip reported a fatal error.",
            7 => "The command line was wrong.",
            8 => "There was not enough memory.",
            255 => "The operation was stopped.",
            _ => $"7-Zip exited with code {code}.",
        };

        if (output.Contains("Wrong password", StringComparison.OrdinalIgnoreCase)
            || output.Contains("Can not open encrypted archive", StringComparison.OrdinalIgnoreCase))
        {
            reason = "The archive is encrypted and the password is missing or wrong.";
        }
        else if (output.Contains("Can not open the file as archive", StringComparison.OrdinalIgnoreCase))
        {
            reason = "7-Zip does not recognise this file as an archive.";
        }

        return $"Could not {what} the archive. {reason}";
    }

    private async Task<(int ExitCode, string Output)> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(this.executablePath!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(this.executablePath!)!,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new ConversionFailedException($"Could not start \"{this.executablePath}\".");

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var text = new StringBuilder();
        text.Append(await standardOutput.ConfigureAwait(false));
        text.Append(await standardError.ConfigureAwait(false));

        return (process.ExitCode, text.ToString());
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // It exited on its own between the check and the kill.
        }
    }

    [GeneratedRegex(@"7-Zip.*?(\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
