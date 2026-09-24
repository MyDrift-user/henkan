# Backend definitions

A backend is one JSON file. The built-ins are embedded in `Henkan.Core` under
`src/Henkan.Core/Definitions/`; user backends live in `%LocalAppData%\Henkan\backends\`.
A user file with the same `id` as a built-in replaces it entirely.

## Top level

| Field | Type | Meaning |
|---|---|---|
| `id` | string | Stable identifier. Presets refer to `id/targetId`. |
| `name` | string | Shown in lists. |
| `description` | string | Shown on the Conversions page. |
| `kind` | `process`, `imageMagick`, `office` | How the backend runs. Only `process` is meaningful for user backends. |
| `executable` | object | Required for `process`. See below. |
| `successExitCodes` | int[] | Exit codes treated as success. Default `[0]`. |
| `progress` | object | How to read progress from the tool's output. Optional. |
| `outputHandling` | `path`, `temporaryDirectory` | `path`: the tool writes `{output}`. `temporaryDirectory`: the tool writes a file named after the input into `{tempDir}` and the engine moves it. |
| `timeoutSeconds` | int | Kill the tool after this long. `0` means never. |
| `sharedOptions` | option[] | Options every target gets, before its own. |
| `argumentPrefix` | fragment[] | Placed before every target's arguments. Typically the input. |
| `argumentSuffix` | fragment[] | Placed after every target's arguments. Typically the output. |
| `targets` | target[] | The formats this backend produces. |

## `executable`

| Field | Meaning |
|---|---|
| `fileName` | Name searched on PATH, for example `ffmpeg.exe`. |
| `bundledPath` | Path under the bundled tools folder, for example `ffmpeg/ffmpeg.exe`. Omit if never bundled. |
| `searchPaths` | Absolute paths tried before PATH. `%ProgramFiles%` style variables are expanded. |
| `probeArguments` | Arguments that make the tool print something and exit, for example `["-version"]`. |
| `versionPattern` | Regex with one capture group applied to the probe output. |
| `homepageUrl` | Shown when the tool is missing. |

Resolution order: a path set on the Conversions page, then `bundledPath` under the app's
`tools\` folder or the repository's `build\tools\`, then `searchPaths`, then PATH.

## `progress`

| Field | Meaning |
|---|---|
| `stream` | `standardError` (default) or `standardOutput`. |
| `durationPattern` | Matched once. Groups 1 to 3 are hours, minutes, seconds. |
| `positionPattern` | Matched repeatedly, same groups. Progress is position divided by duration. |
| `percentPattern` | Matched repeatedly, group 1 is a percentage. Wins over the pair above. |

## Targets

| Field | Meaning |
|---|---|
| `id` | Unique within the backend. |
| `label` | Shown in the format picker. |
| `outputExtension` | Without a dot. |
| `category` | Grouping heading, for example `Audio`. |
| `inputExtensions` | Without dots. A family name such as `$audio` stands for a whole group, see below. `["*"]` accepts anything. |
| `options` | This target's options, after the shared ones. |
| `arguments` | Fragments between the backend's prefix and suffix. |
| `operation` | For in-process backends only: `icon` or `animation` for ImageMagick, `word`, `excel` or `powerpoint` for Office, `compress`, `repack` or `extract` for 7-Zip. |
| `acceptsFolders` | True when a folder can be handed to this target instead of a file. Only archiving means anything for a folder. |
| `outputIsDirectory` | True when the result is a folder, which is what extracting produces. The output path is then built without an extension. |
| `settings` | Backend-specific values that are not user options, for example an Office file format code. |
| `steps` | For `pipeline` backends only, see below. |

## Format families

A target that declares `["*"]` is offered for every file, which is how an image ends up
with an offer to become an MP3. Instead of repeating a long list in every target, name a
family:

```json
"inputExtensions": [ "$audio", "$video" ]
```

| Family | Covers |
|---|---|
| `$audio` | mp3, wav, flac, m4a, aac, ogg, opus, wma, aiff, ac3, and the rest |
| `$video` | mp4, mkv, avi, mov, webm, wmv, mpg, ts, and the rest |
| `$image` | png, jpg, gif, tiff, webp, avif, heic, svg, psd, and `$raw` |
| `$raw` | Camera raw: cr2, cr3, nef, arw, dng, raf, orf, and the rest |
| `$text` | doc, docx, odt, rtf, txt, md, html, epub |
| `$spreadsheet` | xls, xlsx, ods, csv, tsv |
| `$presentation` | ppt, pptx, odp |
| `$document` | `$text`, `$spreadsheet` and `$presentation` together |
| `$postscript` | pdf, ps, eps, ai |

Families may be mixed with plain extensions in the same list, and one family may name
another. An unknown family name contributes nothing rather than failing the file, so a
typo costs you that one entry and not the whole definition.

The same names work in a preset's `inputExtensions`, where they narrow the target further.
A preset can only ever narrow: an extension its target cannot read is dropped, and a
preset left with nothing does not appear in the menu at all.

## Multi-step conversions

A backend with `"kind": "pipeline"` runs targets that already exist, in order,
each reading what the last produced:

```json
{
  "id": "sheet-png",
  "label": "PNG (via PDF)",
  "outputExtension": "png",
  "inputExtensions": [ "$spreadsheet" ],
  "options": [ { "id": "Resolution", "kind": "choice", "default": "150" } ],
  "steps": [
    { "targets": [ "office/excel-pdf", "libreoffice/pdf" ] },
    { "targets": [ "ghostscript/png" ], "options": { "Resolution": "{Resolution}" } }
  ]
}
```

Nothing converts a spreadsheet to an image in one move, which is why the obvious
conversions were the ones that did not work. Going through a PDF does, and both
legs already existed.

A step names one or more targets and takes the first that is available, so a
single definition covers a machine with Microsoft Office and a machine with
LibreOffice. With one step that is not a chain but a choice, which is how the
document presets manage one menu entry instead of one per office suite.

`options` on a step passes values down to it. `{Name}` is replaced with the
pipeline target's own option of that name, so a resolution asked for once reaches
whichever leg acts on it. Intermediate files go to the job's scratch directory
and are removed with it.

Availability is per target here rather than per backend: a route is offered only
when every one of its steps can run.

## Options

| Field | Meaning |
|---|---|
| `id` | Used as `{id}` in templates and as a name in conditions. Must not be one of the built-in variable names. |
| `label` | Shown next to the editor. |
| `description` | Help text under the editor. |
| `kind` | `text`, `boolean`, `integer`, `decimal`, `choice`, `range`, `filePath`, `directoryPath`. |
| `group` | Heading the option is filed under. |
| `default` | Always a string. |
| `minimum`, `maximum`, `step` | For numeric kinds. `range` needs minimum and maximum. |
| `unit` | Suffix shown after the editor. |
| `choices` | For `choice`: `[{ "value": "x", "label": "X", "description": "..." }]`. |
| `visibleWhen` | Condition. Hidden options keep their value but are not shown. |
| `advanced` | Collapsed behind an expander. |

Every value is stored as a string. Presets record only values that differ from the
default, so changing a default in the backend changes it for every preset that never
overrode it.

## Argument fragments

```json
{ "value": "-b:a {Bitrate}k", "when": "Mode == 'Bitrate'", "splitAfterExpansion": false, "comment": "..." }
```

The fragment is included only when `when` holds. Quoting is resolved before variables
are substituted: `"{input}"` and `{input}` both yield exactly one argument, whatever the
path contains. The result is passed as an argument list, never through a shell.

`splitAfterExpansion: true` substitutes first and then splits on whitespace. Use it only
for an option that is itself a list of arguments, such as an "extra arguments" box.

Literal braces are written `{{` and `}}`. A literal double quote inside a quoted section
is written `""`.

## Conditions

```
expression  := orExpr
orExpr      := andExpr ( '||' andExpr )*
andExpr     := unary  ( '&&' unary )*
unary       := '!' unary | comparison
comparison  := primary ( ( '==' | '!=' | '<' | '<=' | '>' | '>=' ) primary )?
primary     := '(' expression ')' | number | 'string' | "string" | identifier
```

Identifiers are option ids, case-insensitive. Comparisons are numeric when both sides
parse as numbers and case-insensitive text otherwise. A bare identifier is true unless
its value is empty, `false`, `0`, `no` or `off`. Unknown identifiers are empty, so a typo
hides a fragment rather than failing the conversion.

## Variables

| Name | Value |
|---|---|
| `input` | Full path of the source file. |
| `inputDir`, `inputName`, `inputExt`, `inputFileName` | Its directory, name without extension, extension without dot, and file name. |
| `output` | Full path of the destination, already resolved for conflicts. |
| `outputDir`, `outputName`, `outputExt`, `outputFileName` | As above. |
| `tempDir` | A per-job scratch directory, deleted afterwards. |
| `toolDir` | Directory of the resolved executable. |
| `option:Name` | Forces the option namespace for `Name`. |

Built-in names take precedence over option ids.

## ImageMagick option ids

The `imageMagick` backend executes code rather than a template, so its recognised option
ids are fixed. The schema (labels, ranges, defaults, visibility) is still the JSON.

`Quality`, `ResizeMode` (`None`, `Percent`, `Fit`), `ScalePercent`, `MaxWidth`,
`MaxHeight`, `Rotation`, `BackgroundColor`, `Lossless`, `StripMetadata`, and for the
`icon` operation `IconSizes`.

The `animation` operation reads the input as a frame collection instead of a single
image, which is what keeps a GIF or an APNG animated. Use it for any target whose output
format can hold more than one frame.

## Archive target settings

The `archive` backend runs the bundled 7-Zip. A target names the format it writes:

| Setting | Meaning |
|---|---|
| `format` | The 7-Zip type name: `zip`, `7z`, `tar`, `gzip`, `bzip2`, `xz`. |

`operation` is usually left out. Without it the backend decides from the input: an
archive is unpacked and its contents written in the new format, anything else is
put into a new archive. Set `compress` or `repack` to force one. The `gzip`,
`bzip2` and `xz` targets force `compress`, because those formats hold a single
file and have nothing to repack into.

Recognised option ids: `Level` (0 to 9), `Repack`, `Password`, `EncryptNames`
(7z only), and `SolidArchive` (7z only).

A target with `acceptsFolders` can archive a whole folder, and one with
`outputIsDirectory` unpacks into a folder beside the archive named after it. An
archive that already holds a single folder is not nested a second time, because
`project\project\...` is something someone then has to tidy up by hand.

## Office target settings

The `office` backend reads these from a target's `settings`:

| Setting | Meaning |
|---|---|
| `mode` | `fixed` exports through ExportAsFixedFormat (PDF and XPS), `saveas` uses SaveAs with `format`, `slide-image` renders one slide. |
| `format` | The numeric file format: `WdSaveFormat` for Word, `XlFileFormat` for Excel, `PpSaveAsFileType` for PowerPoint. |
| `imageFormat` | For `slide-image`: `PNG` or `JPG`. |
| `activeSheetOnly` | `true` notes in the log that a one-table format keeps only the active sheet. |

The codes are easy to get subtly wrong, and a wrong one is quiet: PowerPoint
given the code for an OpenXML show writes a PPTX and calls it an ODP. Regenerate
the definition from the table in `tools/generate-definitions.py` rather than
editing the numbers by hand, and check the result with `henkanc verify`.

## Office option ids

Word: `OptimizeFor` (`Print`, `Screen`), `CreateBookmarks` (`None`, `Headings`,
`Bookmarks`), `IncludeDocumentProperties`, `PdfA`.
Excel: `Quality` (`Standard`, `Minimum`), `IgnorePrintAreas`, `IncludeDocumentProperties`.
PowerPoint: `Intent` (`Print`, `Screen`).
