using Henkan.Core.Backends;
using Henkan.Core.Conversion;
using Henkan.Core.Presets;
using Henkan.Core.Settings;

namespace Henkan.Cli;

/// <summary>
/// Runs every available target against every sample file it claims to accept.
/// </summary>
/// <remarks>
/// A definition that renders a plausible command line still tells you nothing
/// about whether the tool accepts it. This is the only check that does: it takes
/// real files, runs the real backends, and looks at what came out. A target that
/// says it reads a .csv and then produces nothing is a bug, and this is what
/// finds it.
/// </remarks>
internal static class Verifier
{
    public static async Task<int> RunAsync(CommandLine options)
    {
        string samples = options.Get("samples")
            ?? throw new CommandException("--samples <dir> is required.", Cli.Usage);

        if (!Directory.Exists(samples))
        {
            throw new CommandException($"No such directory: {samples}");
        }

        string output = options.Get("out") ?? Path.Combine(Path.GetTempPath(), "henkan-verify");
        Directory.CreateDirectory(output);

        string? pattern = options.Get("target");
        BackendRegistry registry = await Cli.LoadRegistryAsync(options);

        string[] files = [.. Directory.EnumerateFiles(samples).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)];

        if (files.Length == 0)
        {
            throw new CommandException($"No sample files in {samples}.");
        }

        var results = new List<Result>();

        foreach (string file in files)
        {
            string extension = FormatFamilies.Normalise(Path.GetExtension(file));

            foreach (TargetDescriptor target in registry.GetTargets(extension).OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                if (pattern is not null && !target.Key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                results.Add(await RunOneAsync(registry, target, file, output));
            }
        }

        Report(results, options);

        int failures = results.Count(r => !r.Ok);

        if (!options.Has("keep"))
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (IOException)
            {
                // The results are already reported; a leftover directory is not a failure.
            }
        }

        return failures == 0 ? Cli.Ok : Cli.Failed;
    }

    private static async Task<Result> RunOneAsync(
        BackendRegistry registry,
        TargetDescriptor target,
        string input,
        string outputDirectory)
    {
        string folder = Path.Combine(outputDirectory, target.Key.Replace('/', '-'));
        Directory.CreateDirectory(folder);

        Preset preset = Preset.Create("verify", target.Key) with
        {
            OutputPathTemplate = Path.Combine(folder, "{inputName}.{outputExt}"),
            ConflictPolicy = FileConflictPolicy.Overwrite,
        };

        using var queue = new ConversionQueue(registry, 1);
        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue([input], preset);

        if (jobs.All(j => j.IsFinished))
        {
            finished.TrySetResult();
        }

        // A stuck backend must not hang the whole run. Two minutes is longer
        // than any of these conversions needs on a sample file.
        Task completed = await Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromMinutes(2)));

        if (completed != finished.Task)
        {
            queue.CancelAll();
            return new Result(Path.GetFileName(input), target.Key, false, 0, "Timed out.");
        }

        ConversionJob job = jobs[0];
        long bytes = job.OutputPath is not null && File.Exists(job.OutputPath)
            ? new FileInfo(job.OutputPath).Length
            : 0;

        if (job.State != ConversionState.Succeeded)
        {
            return new Result(Path.GetFileName(input), target.Key, false, bytes, job.Message ?? "Did not succeed.");
        }

        if (bytes == 0)
        {
            return new Result(Path.GetFileName(input), target.Key, false, 0, "Reported success but the file is empty.");
        }

        // The file exists and is not empty, which is where most checks stop. It
        // still has to be the format that was asked for.
        bool? sniffed = FormatSniffer.Matches(job.OutputPath!, target.Target.OutputExtension);

        if (sniffed == false)
        {
            return new Result(
                Path.GetFileName(input),
                target.Key,
                false,
                bytes,
                $"Wrote a .{target.Target.OutputExtension} that is really {FormatSniffer.Describe(job.OutputPath!)}.");
        }

        return new Result(Path.GetFileName(input), target.Key, true, bytes, sniffed is null ? "format not checked" : null);
    }

    private static void Report(List<Result> results, CommandLine options)
    {
        if (options.Json)
        {
            Commands.Write(new
            {
                Total = results.Count,
                Passed = results.Count(r => r.Ok),
                Failed = results.Count(r => !r.Ok),
                Results = results,
            });

            return;
        }

        string? heading = null;

        foreach (Result result in results)
        {
            if (result.Input != heading)
            {
                heading = result.Input;
                Console.WriteLine($"\n{heading}");
            }

            if (result.Ok)
            {
                string note = result.Message is null ? string.Empty : $"  ({result.Message})";
                Console.WriteLine($"  ok    {result.Target,-28} {result.Bytes,12:N0} bytes{note}");
            }
            else
            {
                Console.WriteLine($"  FAIL  {result.Target,-28} {Shorten(result.Message)}");
            }
        }

        int passed = results.Count(r => r.Ok);
        Console.WriteLine($"\n{passed} of {results.Count} conversions produced a file.");

        foreach (Result failure in results.Where(r => !r.Ok))
        {
            Console.WriteLine($"  FAIL {failure.Input} -> {failure.Target}: {Shorten(failure.Message)}");
        }
    }

    private static string Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "no output";
        }

        string single = message.ReplaceLineEndings(" ").Trim();
        return single.Length <= 160 ? single : single[..160] + "...";
    }

    private sealed record Result(string Input, string Target, bool Ok, long Bytes, string? Message);
}
