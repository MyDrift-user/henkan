using System.Text.Json;
using System.Text.Json.Serialization;
using Henkan.Core.Backends;
using Henkan.Core.Conversion;
using Henkan.Core.Options;
using Henkan.Core.Presets;
using Henkan.Core.Settings;

namespace Henkan.Cli;

internal static class Commands
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static async Task<int> BackendsAsync(CommandLine options)
    {
        BackendRegistry registry = await Cli.LoadRegistryAsync(options);

        var rows = registry.Backends
            .Select(b => new
            {
                Id = b.Definition.Id,
                b.Definition.Name,
                Kind = b.Definition.Kind.ToString(),
                Available = b.Availability.IsAvailable,
                b.Availability.Version,
                Path = b.Availability.ResolvedPath,
                b.Availability.Reason,
                Targets = b.Definition.Targets.Count,
            })
            .ToList();

        if (options.Json)
        {
            Write(rows);
            return Cli.Ok;
        }

        foreach (var row in rows)
        {
            string state = row.Available ? $"ok      {row.Version}" : $"missing {row.Reason}";
            Console.WriteLine($"{row.Id,-12} {row.Targets,3} targets  {state}");

            if (row.Available && row.Path is not null)
            {
                Console.WriteLine($"{string.Empty,-12}              {row.Path}");
            }
        }

        return registry.Backends.Any(b => b.Availability.IsAvailable) ? Cli.Ok : Cli.Failed;
    }

    public static async Task<int> TargetsAsync(CommandLine options)
    {
        BackendRegistry registry = await Cli.LoadRegistryAsync(options);
        string? input = NormaliseInput(options.Get("input"));
        string? category = options.Get("category");

        var rows = registry.GetTargets(input, availableOnly: !options.All)
            .Where(t => category is null || t.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Category, StringComparer.Ordinal)
            .ThenBy(t => t.Target.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new
            {
                t.Key,
                t.Target.Label,
                t.Category,
                Output = t.Target.OutputExtension,
                Accepts = t.Target.ExpandInputExtensions(),
                Available = t.IsAvailable,
                Options = t.Backend.Definition.GetOptionSchema(t.Target).Count,
                t.Target.Description,
            })
            .ToList();

        if (options.Json)
        {
            Write(rows);
            return Cli.Ok;
        }

        if (rows.Count == 0)
        {
            Console.WriteLine(input is null
                ? "No targets are available."
                : $"Nothing available converts a .{input} file.");
            return Cli.Failed;
        }

        string? heading = null;

        foreach (var row in rows)
        {
            if (row.Category != heading)
            {
                heading = row.Category;
                Console.WriteLine($"\n{heading}");
            }

            string mark = row.Available ? " " : "!";
            Console.WriteLine($" {mark} {row.Key,-26} {row.Label,-28} -> .{row.Output,-6} {row.Options,2} options");
        }

        Console.WriteLine($"\n{rows.Count} targets{(input is null ? string.Empty : $" accept .{input}")}.");
        return Cli.Ok;
    }

    public static async Task<int> OptionsAsync(CommandLine options)
    {
        BackendRegistry registry = await Cli.LoadRegistryAsync(options);
        string key = options.Get("target") ?? options.RequirePositional(0, "a backend/target key");

        TargetDescriptor target = registry.FindTarget(key)
            ?? throw new CommandException($"No target \"{key}\". Run henkanc targets to see them.", Cli.Usage);

        IReadOnlyList<OptionDescriptor> schema = target.Backend.Definition.GetOptionSchema(target.Target);
        OptionValueSet defaults = OptionValueSet.FromSchema(schema);

        if (options.Json)
        {
            Write(new
            {
                target.Key,
                target.Target.Label,
                Accepts = target.Target.ExpandInputExtensions(),
                Output = target.Target.OutputExtension,
                Available = target.IsAvailable,
                Options = schema.Select(o => new
                {
                    o.Id,
                    o.Label,
                    Kind = o.Kind.ToString(),
                    o.Default,
                    o.Group,
                    o.Unit,
                    o.Minimum,
                    o.Maximum,
                    o.Step,
                    o.VisibleWhen,
                    o.Advanced,
                    o.Description,
                    Choices = o.Choices.Select(c => new { c.Value, Label = c.Label ?? c.Value }),
                }),
            });

            return Cli.Ok;
        }

        Console.WriteLine($"{target.Key}  {target.Target.Label}");
        Console.WriteLine($"  accepts   {Describe(target.Target.ExpandInputExtensions())}");
        Console.WriteLine($"  produces  .{target.Target.OutputExtension}");
        Console.WriteLine($"  backend   {target.Backend.Definition.Name} ({(target.IsAvailable ? "available" : target.Backend.Availability.Reason)})");

        if (schema.Count == 0)
        {
            Console.WriteLine("\nNo options.");
            return Cli.Ok;
        }

        string? heading = null;

        foreach (OptionDescriptor option in schema)
        {
            string group = option.Group ?? "General";

            if (group != heading)
            {
                heading = group;
                Console.WriteLine($"\n{heading}");
            }

            string kind = option.Kind.ToString().ToLowerInvariant();
            string range = option.Minimum is not null || option.Maximum is not null
                ? $" [{option.Minimum} .. {option.Maximum}]"
                : string.Empty;
            string unit = option.Unit is null ? string.Empty : $" {option.Unit}";
            string visible = option.VisibleWhen is null ? string.Empty : $"   shown when: {option.VisibleWhen}";

            Console.WriteLine($"  {option.Id,-16} {kind,-8} default={defaults.GetString(option.Id) ?? "(none)"}{unit}{range}{visible}");

            if (option.Choices.Count > 0)
            {
                Console.WriteLine($"  {string.Empty,-16} choices: {string.Join(", ", option.Choices.Select(c => c.Label is null ? c.Value : $"{c.Value} ({c.Label})"))}");
            }

            if (option.Description is not null)
            {
                Console.WriteLine($"  {string.Empty,-16} {option.Description}");
            }
        }

        return Cli.Ok;
    }

    public static async Task<int> PresetsAsync(CommandLine options)
    {
        BackendRegistry registry = await Cli.LoadRegistryAsync(options);
        var store = new PresetStore();
        IReadOnlyList<Preset> presets = store.Load();

        var rows = presets
            .Select(p =>
            {
                TargetDescriptor? target = registry.FindTarget(p.TargetKey);
                return new
                {
                    p.Id,
                    p.Name,
                    p.TargetKey,
                    Available = target?.IsAvailable == true,
                    Accepts = target is null ? [] : p.ResolveInputExtensions(target.Value.Target) ?? [],
                    p.ShowInContextMenu,
                    p.OutputPathTemplate,
                };
            })
            .ToList();

        if (options.Json)
        {
            Write(rows);
            return Cli.Ok;
        }

        foreach (var row in rows)
        {
            Console.WriteLine($"{(row.Available ? " " : "!")} {row.Name,-24} {row.TargetKey,-26} {row.Id}");
        }

        Console.WriteLine($"\n{rows.Count} presets.");
        return Cli.Ok;
    }

    public static async Task<int> ProbeAsync(CommandLine options)
    {
        string path = options.Get("file") ?? options.RequirePositional(0, "a file");

        if (!File.Exists(path))
        {
            throw new CommandException($"No such file: {path}");
        }

        BackendRegistry registry = await Cli.LoadRegistryAsync(options);
        string extension = FormatFamilies.Normalise(Path.GetExtension(path));

        var rows = registry.GetTargets(extension, availableOnly: !options.All)
            .OrderBy(t => t.Category, StringComparer.Ordinal)
            .ThenBy(t => t.Target.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new { t.Key, t.Target.Label, t.Category, Output = t.Target.OutputExtension, Available = t.IsAvailable })
            .ToList();

        if (options.Json)
        {
            Write(new { File = Path.GetFullPath(path), Extension = extension, Targets = rows });
            return Cli.Ok;
        }

        Console.WriteLine($"{Path.GetFileName(path)}  (.{extension})");

        foreach (var group in rows.GroupBy(r => r.Category))
        {
            Console.WriteLine($"\n{group.Key}");

            foreach (var row in group)
            {
                Console.WriteLine($" {(row.Available ? " " : "!")} {row.Key,-26} {row.Label}");
            }
        }

        Console.WriteLine($"\n{rows.Count} conversions.");
        return rows.Count > 0 ? Cli.Ok : Cli.Failed;
    }

    public static async Task<int> ConvertAsync(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            throw new CommandException("Expected at least one file to convert.", Cli.Usage);
        }

        BackendRegistry registry = await Cli.LoadRegistryAsync(options);
        Preset preset = BuildPreset(registry, options);

        using var queue = new ConversionQueue(registry, 1);
        var finished = new TaskCompletionSource();
        queue.AllFinished += (_, _) => finished.TrySetResult();

        IReadOnlyList<ConversionJob> jobs = queue.Enqueue(options.Positional, preset);

        if (jobs.All(j => j.IsFinished))
        {
            finished.TrySetResult();
        }

        await finished.Task;

        var rows = jobs
            .Select(j => new
            {
                Input = j.InputPath,
                Output = j.OutputPath,
                State = j.State.ToString(),
                j.Message,
                Bytes = j.OutputPath is not null && File.Exists(j.OutputPath) ? new FileInfo(j.OutputPath).Length : 0,
                Log = options.Has("verbose") ? j.Log : null,
            })
            .ToList();

        if (options.Json)
        {
            Write(rows);
        }
        else
        {
            foreach (var row in rows)
            {
                Console.WriteLine($"{row.State,-9} {Path.GetFileName(row.Input)}");

                if (row.Output is not null && row.Bytes > 0)
                {
                    Console.WriteLine($"          -> {row.Output} ({row.Bytes:N0} bytes)");
                }

                if (row.Message is not null)
                {
                    Console.WriteLine($"          {row.Message}");
                }

                if (row.Log is not null)
                {
                    foreach (string line in row.Log)
                    {
                        Console.WriteLine($"          | {line}");
                    }
                }
            }
        }

        return jobs.All(j => j.State == ConversionState.Succeeded) ? Cli.Ok : Cli.Failed;
    }

    public static Task<int> VerifyAsync(CommandLine options) => Verifier.RunAsync(options);

    /// <summary>Turns the command line into the preset a conversion runs under.</summary>
    internal static Preset BuildPreset(BackendRegistry registry, CommandLine options)
    {
        string? presetName = options.Get("preset");

        if (presetName is not null)
        {
            var store = new PresetStore();
            IReadOnlyList<Preset> presets = store.Load();

            Preset saved = presets.FirstOrDefault(p => p.Id.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                ?? presets.FirstOrDefault(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                ?? throw new CommandException($"No preset called \"{presetName}\".", Cli.Usage);

            return ApplyOutput(saved, options);
        }

        string key = options.Require("target");

        if (registry.FindTarget(key) is null)
        {
            throw new CommandException($"No target \"{key}\". Run henkanc targets to see them.", Cli.Usage);
        }

        Preset preset = Preset.Create("command line", key);

        foreach ((string name, string value) in options.GetSettings())
        {
            preset.Options[name] = value;
        }

        return ApplyOutput(preset, options);
    }

    /// <summary>
    /// Applies <c>--out</c>. A path with an extension is taken as the exact file
    /// to write, anything else as a directory to write into.
    /// </summary>
    private static Preset ApplyOutput(Preset preset, CommandLine options)
    {
        FileConflictPolicy policy = options.Has("overwrite")
            ? FileConflictPolicy.Overwrite
            : FileConflictPolicy.Rename;

        string? destination = options.Get("out");

        if (destination is null)
        {
            return preset with { ConflictPolicy = policy };
        }

        bool isDirectory = Directory.Exists(destination) || Path.GetExtension(destination).Length == 0;

        if (isDirectory)
        {
            Directory.CreateDirectory(destination);
        }

        return preset with
        {
            ConflictPolicy = policy,
            OutputPathTemplate = isDirectory
                ? Path.Combine(destination, "{inputName}.{outputExt}")
                : destination,
        };
    }

    private static string? NormaliseInput(string? value)
    {
        if (value is null)
        {
            return null;
        }

        string extension = Path.GetExtension(value);
        return FormatFamilies.Normalise(extension.Length > 0 ? extension : value);
    }

    private static string Describe(IReadOnlyList<string> extensions) =>
        extensions.Count == 0
            ? "any file"
            : extensions.Count <= 12
                ? string.Join(", ", extensions)
                : string.Join(", ", extensions.Take(12)) + $", and {extensions.Count - 12} more";

    internal static void Write(object value) =>
        Console.WriteLine(JsonSerializer.Serialize(value, Json));
}
