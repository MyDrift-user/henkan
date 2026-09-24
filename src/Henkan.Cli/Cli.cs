using Henkan.Core.Backends;
using Henkan.Core.Presets;
using Henkan.Core.Settings;
using Henkan.Core.Tools;

namespace Henkan.Cli;

/// <summary>
/// The command line front end.
/// </summary>
/// <remarks>
/// The application is a WinUI process and cannot write to a console, so anything
/// scriptable lives here instead: listing what can convert what, showing the
/// options a target offers, running a conversion, and checking the whole
/// catalogue against real files. It shares every line of conversion code with
/// the application, so what it proves holds for the Explorer menu too.
/// </remarks>
public static class Cli
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintUsage();
            return args.Length == 0 ? Usage : Ok;
        }

        var options = CommandLine.Parse(args.Skip(1));
        string command = args[0].ToLowerInvariant();

        try
        {
            return command switch
            {
                "backends" => await Commands.BackendsAsync(options),
                "targets" => await Commands.TargetsAsync(options),
                "options" => await Commands.OptionsAsync(options),
                "presets" => await Commands.PresetsAsync(options),
                "probe" => await Commands.ProbeAsync(options),
                "convert" => await Commands.ConvertAsync(options),
                "verify" => await Commands.VerifyAsync(options),
                _ => Unknown(command),
            };
        }
        catch (CommandException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
            return Failed;
        }
    }

    /// <summary>Loads the backends the same way the application does.</summary>
    internal static async Task<BackendRegistry> LoadRegistryAsync(CommandLine options)
    {
        var settings = new SettingsStore();
        settings.Load();

        var locator = new ToolLocator(
            backendId => settings.Current.ToolPaths.TryGetValue(backendId, out string? path) ? path : null,
            options.GetAll("tools"));

        var registry = new BackendRegistry(locator, HenkanPaths.BackendsDirectory);
        await registry.ReloadAsync();

        foreach (BackendLoadProblem problem in registry.Problems)
        {
            Console.Error.WriteLine($"warning: {problem.Source}: {problem.Message}");
        }

        return registry;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command \"{command}\". Run henkanc --help.");
        return Usage;
    }

    private static bool IsHelp(string argument) =>
        argument is "--help" or "-h" or "-?" or "/?" or "help";

    private static void PrintUsage()
    {
        Console.WriteLine("""
            henkanc - Henkan on the command line

            Usage:
              henkanc backends [--json]
                  Every backend, whether its tool was found, and the version.

              henkanc targets [--input <ext|file>] [--category <name>] [--all] [--json]
                  Conversions that are available. --input narrows to the ones that
                  accept that kind of file. --all includes unavailable backends.

              henkanc options <backend/target> [--json]
                  The options a target offers, with kinds, defaults and choices.

              henkanc presets [--json]
                  The saved presets, as the Explorer menu sees them.

              henkanc probe <file> [--json]
                  What this particular file can be converted into.

              henkanc convert <file...> --target <backend/target> [--set Name=Value]...
                                        [--out <path>] [--overwrite] [--json]
              henkanc convert <file...> --preset <name|id> [--out <path>] [--json]
                  Run a conversion. --set may be repeated. --out takes a file or a
                  directory; without it the result lands beside the input.

              henkanc verify --samples <dir> [--out <dir>] [--target <pattern>]
                             [--keep] [--json]
                  Run every available target against every sample file it accepts
                  and report what actually produced a file. This is the end to end
                  check; it uses the same code the application runs.

            Common:
              --json      machine readable output
              --tools     extra directory to search for bundled tools (repeatable)

            Exit codes: 0 success, 1 failure, 2 bad usage.
            """);
    }
}

/// <summary>A failure with a message already fit to print.</summary>
internal sealed class CommandException(string message, int exitCode = Cli.Failed) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}
