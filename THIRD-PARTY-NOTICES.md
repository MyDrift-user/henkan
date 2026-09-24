# Third-party notices

Henkan is licensed under the GNU General Public License, version 3 or any later
version (see [LICENSE](LICENSE)). It uses the components below. None of the
bundled programs are kept in this repository: `tools/fetch-deps.ps1` downloads
them at build time from the addresses in [tools/deps.json](tools/deps.json),
checks their SHA-256, and the package build places them, together with their
own license texts, in the `tools` folder of the installed application.

## Programs bundled with the package

Henkan starts these as separate programs and passes them files on the command
line. They are not linked into Henkan.

| Program | Version | License | Source code |
|---|---|---|---|
| FFmpeg | n9.0.1-27-g9b0578816c, BtbN win64-gpl build | GPL-3.0-or-later (built with `--enable-gpl --enable-version3`, without non-free components) | Build: https://github.com/BtbN/FFmpeg-Builds (release `autobuild-2026-09-09-14-51`). FFmpeg itself: https://git.ffmpeg.org/ffmpeg.git at commit `9b0578816c` |
| Ghostscript | 10.06.0 | AGPL-3.0-or-later | https://github.com/ArtifexSoftware/ghostpdl-downloads/releases/tag/gs10060 |
| 7-Zip | 25.01 | GNU LGPL-2.1-or-later, parts under the BSD 3-clause license, and the unRAR code under the unRAR license restriction (the code may not be used to create a RAR compatible archiver) | https://www.7-zip.org/download.html (7z2501-src) |

Anyone distributing a built Henkan package passes these programs on in binary
form and must make their corresponding source available as their licenses
require, for example by publishing the source archives listed above next to the
package.

## Libraries

| Library | License | Project |
|---|---|---|
| Magick.NET, with the ImageMagick library and its bundled delegates | Apache-2.0 (Magick.NET), ImageMagick License (ImageMagick) | https://github.com/dlemstra/Magick.NET |
| Windows App SDK and WinUI 3 | MIT | https://github.com/microsoft/WindowsAppSDK |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.DependencyInjection and Logging | MIT | https://github.com/dotnet/runtime |
| Microsoft.Windows.CsWin32 (build time only) | MIT | https://github.com/microsoft/CsWin32 |
| xUnit (tests only) | Apache-2.0 | https://github.com/xunit/xunit |

## Programs used when installed

Henkan does not include or redistribute these. It uses them only when they are
already installed on the computer, under the user's own license.

| Program | How it is used |
|---|---|
| Microsoft Office (Word, Excel, PowerPoint) | through its COM automation interface |
| LibreOffice | as a separate program, `soffice.exe --headless` |

All product names are trademarks of their respective owners and are used here
only to say which programs Henkan works with.
