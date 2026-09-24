using Henkan.Core.Settings;
using Henkan.Core.Templating;

namespace Henkan.Core.Conversion;

/// <summary>
/// Turns a preset's output path template into a concrete, conflict-free path.
/// </summary>
public static class OutputPathResolver
{
    /// <summary>
    /// Renders the template and applies the conflict policy. Returns null when
    /// the policy is <see cref="FileConflictPolicy.Skip"/> and the file exists.
    /// </summary>
    /// <exception cref="ConversionSkippedException">
    /// Thrown for <see cref="FileConflictPolicy.Fail"/> when the file exists, so
    /// the job reports a clear reason instead of a cryptic tool error later.
    /// </exception>
    /// <param name="directory">
    /// True when the result is a folder. The rendered path then loses the empty
    /// extension the template asked for, and an existing folder counts as a
    /// conflict in the same way an existing file does.
    /// </param>
    public static string? Resolve(
        string template,
        string inputPath,
        string outputExtension,
        FileConflictPolicy policy,
        IReadOnlyDictionary<string, string>? optionValues = null,
        bool directory = false,
        Func<string, bool>? alreadyTaken = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        if (!directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(outputExtension);
        }

        // The template needs an output path to describe the output path, so seed
        // it with the plain default and let the template override the pieces.
        string extension = (outputExtension ?? string.Empty).TrimStart('.');
        string seed = Path.Combine(
            Path.GetDirectoryName(inputPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(inputPath) + (extension.Length > 0 ? "." + extension : string.Empty));

        VariableContext context = VariableContext.Create(inputPath, seed, Path.GetTempPath(), optionValues, outputExtension: extension);
        string rendered = TemplateRenderer.RenderText(template, context.Lookup);

        if (!Path.IsPathRooted(rendered))
        {
            rendered = Path.Combine(Path.GetDirectoryName(inputPath) ?? string.Empty, rendered);
        }

        // The default template ends in ".{outputExt}", which leaves a bare dot
        // behind when there is no extension to put there.
        rendered = directory ? rendered.TrimEnd('.', ' ') : rendered;
        rendered = Path.GetFullPath(rendered);

        // Converting in place would truncate the input before it is read. Every
        // policy gets a renamed output in that case, whatever it says.
        bool sameAsInput = string.Equals(rendered, Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase);

        bool taken = IsTaken(rendered, directory, alreadyTaken);

        if (!taken && !sameAsInput)
        {
            return rendered;
        }

        if (sameAsInput)
        {
            return NextFreeName(rendered, directory, alreadyTaken);
        }

        return policy switch
        {
            FileConflictPolicy.Overwrite => rendered,
            FileConflictPolicy.Rename => NextFreeName(rendered, directory, alreadyTaken),
            FileConflictPolicy.Skip => null,
            FileConflictPolicy.Fail => throw new ConversionSkippedException($"\"{rendered}\" already exists."),
            _ => rendered,
        };
    }

    /// <summary>Appends " (2)", " (3)" and so on until the name is unused.</summary>
    /// <param name="alreadyTaken">
    /// Names that are spoken for without being on disk yet. Two files converted
    /// together can render the same output name, and without this they race for
    /// it: one wins and the other fails on a file that appeared underneath it.
    /// </param>
    public static string NextFreeName(string path, bool directory = false, Func<string, bool>? alreadyTaken = null)
    {
        string parent = Path.GetDirectoryName(path) ?? string.Empty;
        string name = directory ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        string extension = directory ? string.Empty : Path.GetExtension(path);

        // "archive (2).tar.gz" rather than "archive.tar (2).gz".
        if (!directory && name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
            extension = ".tar" + extension;
        }

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = Path.Combine(parent, $"{name} ({i}){extension}");

            if (!IsTaken(candidate, directory, alreadyTaken))
            {
                return candidate;
            }
        }

        throw new ConversionSkippedException($"Could not find a free name for \"{path}\".");
    }

    private static bool IsTaken(string path, bool directory, Func<string, bool>? alreadyTaken) =>
        (directory ? Directory.Exists(path) : File.Exists(path)) || alreadyTaken?.Invoke(path) == true;
}

/// <summary>Raised when a job cannot proceed for a reason the user chose, such as an existing output.</summary>
public sealed class ConversionSkippedException(string message) : Exception(message);
