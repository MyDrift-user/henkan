# Architecture

## The one idea

Everything the user can change is data. The application does not know what a bitrate
is. It knows how to render an option schema into a form, how to turn an argument template
into an argument list, and how to run a process and watch it. A backend definition
supplies the rest.

That gives three properties that were design goals:

1. A conversion option added to a JSON file appears in the UI with no code.
2. A built-in backend and a user-written backend go through the same code path, so
   there is no "plugin" tier with fewer capabilities.
3. A missing tool removes options instead of breaking the build or the app.

## Projects

```
Henkan.Core            net10.0-windows      engine, no UI dependency
Henkan.App             WinUI 3, MSIX        UI, composition root, manifest
Henkan.ShellExtension  NativeAOT DLL        IExplorerCommand, no dependency on Core
Henkan.Core.Tests      xunit
```

## Core

`Options/`
`OptionDescriptor` declares an option; `OptionValueSet` holds the values (all strings)
with typed accessors and `visibleWhen` evaluation. `ToOverrides` reduces a set to the
values that differ from the defaults, which is what a preset stores.

`Expressions/`
`ConditionExpression` is a small recursive-descent parser and evaluator for the
`when` and `visibleWhen` language. Tokens, tree, evaluation, nothing else.

`Templating/`
`TemplateRenderer` tokenises a fragment first and substitutes variables second, so a
path can never split into two arguments. `VariableContext` supplies the built-in
variables and the option values, built-ins winning.

`Backends/`
`BackendDefinition` is the JSON shape. `BackendSerializer` reads and writes it.
`BackendValidator` produces messages for the editor and rejects unusable files.
`BackendRegistry` loads embedded and user definitions, lets user files override
built-ins by id, creates the `IConversionBackend` for each and probes availability.

`ProcessBackend` is the general executor: resolve the tool, render the arguments, run
with redirected output, parse progress with the definition's regexes, enforce exit codes
and timeouts, kill the process tree on cancel, collect a self-named output if asked.
`ImageMagickBackend` and `OfficeBackend` are the two in-process backends; they read
their option schema from JSON like everyone else but execute code.

`Conversion/`
`ConversionJob` is one file through one target, observable, with a log.
`ConversionQueue` runs jobs under a semaphore and never removes them on its own.
`OutputPathResolver` renders the output template and applies the conflict policy.

`Presets/`, `Settings/`
JSON files under `%LocalAppData%\Henkan\`. Corrupt files are renamed `.broken` and
defaults take over.

## App

`AppServices` is the composition root: one instance each of settings, presets, tool
locator, registry and queue. `Program.Main` makes the process single-instance so a
context-menu launch while a window is open lands in the existing queue.

`Controls/OptionEditor` is the piece that renders a schema. It groups by `group`, hides
by `visibleWhen`, and writes into an `OptionValueSet`. The Conversions page uses it for a menu entry's settings; the
same control could render a backend's shared options or anything else shaped like a
schema.

`ShellIntegration` writes `context-menu.json`: the executable path and the presets
that show in the menu, with their extension filters. It is rewritten on every start and
on every preset save.

## Shell extension

Windows 11's modern context menu loads `IExplorerCommand` handlers registered in an MSIX
manifest through a COM surrogate. The surrogate expects a native DLL that exports
`DllGetClassObject`. A managed assembly cannot be loaded that way, so the project is
compiled with NativeAOT: `[UnmanagedCallersOnly(EntryPoint = ...)]` methods become
exports, and `[GeneratedComInterface]`/`[GeneratedComClass]` provide the COM plumbing
without runtime reflection.

The handler reads `context-menu.json`, shows one `Henkan` entry with a submenu of the
presets whose filters accept every selected item, and launches the app with
`--preset <id> --run <files>`. Presets only: conversions are started from here, and
the application itself has nowhere to drop a file, so an entry that opened it with
the selection would be promising something that does not exist. It has no reference to Core on
purpose: the DLL stays small, starts fast, and cannot be broken by a change elsewhere.

## Showing a conversion

A conversion started from the Explorer menu never opens the application. What it does
open is a setting: stacked cards in a screen corner, a floating window listing the
batch, a Windows notification, or nothing at all. `IProgressPresenter` is the one
thing they have in common, and `App` picks an implementation per batch.

Nothing is shown for the first `ProgressDelayMilliseconds` of a batch. Converting a
small image takes a fraction of a second, and a window that appears and vanishes again
in that time is a flash that cannot be read; a batch that finishes inside the delay is
never drawn at all. A failure skips the wait, because a failure is the one outcome the
user has to be told about, and it stays on screen until dismissed whatever the other
settings say.

The batch itself is owned by `App`, not by any window, so it outlives whichever way it
is being shown. Windows therefore come and go freely, which is why the application uses
`DispatcherShutdownMode.OnExplicitShutdown` and decides in `ExitIfNothingLeft` when
there is nothing left to do. A silent run never opens a window at all, and a message
loop that never had one cannot be ended by `Application.Exit`, so that case leaves
deliberately once the queue has drained.

## Bundled tools

`tools\fetch-deps.ps1` downloads the builds pinned in `tools\deps.json`, checks their
SHA-256, and unpacks the needed files under `build\tools\`. The app project includes
that folder as content if it exists. `ToolLocator` searches the app's `tools\` folder,
the repository's `build\tools\` for developer builds, the definition's `searchPaths`,
and PATH, in that order, unless the user set an explicit path.

The repository does not contain the binaries. A clone is a few megabytes and stays that
way.
