<#
.SYNOPSIS
    Downloads the bundled third-party tools into build\tools.

.DESCRIPTION
    Reads tools\deps.json, downloads each entry, verifies its SHA-256 against the
    pinned value, and unpacks only the files Henkan needs. The result lands in
    build\tools\<name>\, which git ignores and the app project picks up as content
    when it exists.

    Nothing about the build depends on this having run. Without it the ffmpeg and
    Ghostscript backends report themselves unavailable and their formats stay
    out of the preset editor.

    Downloads are streamed to a temporary file and deleted after unpacking, so the
    peak disk use is one archive at a time.

.PARAMETER Only
    Restrict to the named entries, for example -Only ffmpeg.

.PARAMETER Force
    Re-download even if the tool is already present.
#>
[CmdletBinding()]
param(
    [string[]]$Only,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$root = Split-Path $PSScriptRoot -Parent
$toolsDir = Join-Path $root 'build\tools'
$deps = Get-Content (Join-Path $PSScriptRoot 'deps.json') -Raw | ConvertFrom-Json

function Get-Sha256([string]$path) {
    (Get-FileHash -Path $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Find-7Zip {
    foreach ($candidate in @('7z', "$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe")) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    return $null
}

foreach ($dep in $deps.tools) {
    if ($Only -and ($Only -notcontains $dep.name)) { continue }

    $target = Join-Path $toolsDir $dep.name
    $marker = Join-Path $target ($dep.files[0])

    if ((Test-Path $marker) -and -not $Force) {
        Write-Host "$($dep.name): already present ($($dep.version)), skipping. Use -Force to refresh."
        continue
    }

    Write-Host "$($dep.name) $($dep.version): downloading $($dep.url)"
    $archive = Join-Path ([System.IO.Path]::GetTempPath()) ("henkan-" + [System.IO.Path]::GetFileName($dep.url))

    try {
        Invoke-WebRequest -Uri $dep.url -OutFile $archive -UseBasicParsing

        $actual = Get-Sha256 $archive
        if ($actual -ne $dep.sha256.ToLowerInvariant()) {
            throw "$($dep.name): SHA-256 mismatch.`n  expected $($dep.sha256)`n  actual   $actual`nThe download is either corrupt or the upstream file changed. Not installing it."
        }

        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        New-Item -ItemType Directory -Force $target | Out-Null

        switch ($dep.kind) {
            'zip' {
                # Pull out just the wanted files, matched by their leaf name,
                # regardless of the versioned folder the archive wraps them in.
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
                try {
                    foreach ($wanted in $dep.files) {
                        $entry = $zip.Entries | Where-Object { $_.FullName -replace '\\', '/' -like "*/$wanted" -or $_.FullName -eq $wanted } | Select-Object -First 1
                        if (-not $entry) { throw "$($dep.name): '$wanted' not found in the archive." }
                        $destination = Join-Path $target $wanted
                        New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $true)
                        Write-Host "  extracted $wanted"
                    }
                }
                finally {
                    $zip.Dispose()
                }
            }

            'msi' {
                # 7-Zip ships an MSI. An administrative install unpacks it into a
                # folder without installing anything and without needing elevation,
                # which is exactly what is wanted here.
                $scratch = Join-Path ([System.IO.Path]::GetTempPath()) "henkan-$($dep.name)-extract"
                if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }

                $process = Start-Process msiexec -ArgumentList "/a `"$archive`" /qn TARGETDIR=`"$scratch`"" -Wait -PassThru
                if ($process.ExitCode -ne 0) { throw "$($dep.name): msiexec exited with $($process.ExitCode)." }

                foreach ($wanted in $dep.files) {
                    $found = Get-ChildItem $scratch -Recurse -Filter (Split-Path $wanted -Leaf) | Select-Object -First 1
                    if (-not $found) { throw "$($dep.name): '$wanted' not found after extraction." }
                    $destination = Join-Path $target $wanted
                    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
                    Copy-Item $found.FullName $destination -Force
                    Write-Host "  extracted $wanted"
                }

                Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
            }

            'nsis' {
                # Ghostscript ships an NSIS installer rather than an archive.
                # 7-Zip can open it without installing anything; failing that,
                # the installer's own silent mode is used against a scratch
                # folder and the wanted files are copied out.
                $sevenZip = Find-7Zip
                $scratch = Join-Path ([System.IO.Path]::GetTempPath()) "henkan-$($dep.name)-extract"
                if (Test-Path $scratch) { Remove-Item $scratch -Recurse -Force }

                if ($sevenZip) {
                    & $sevenZip x $archive "-o$scratch" -y | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "$($dep.name): 7-Zip could not extract the installer." }
                }
                else {
                    Write-Host '  7-Zip not found, running the installer silently into a scratch folder instead.'
                    $process = Start-Process -FilePath $archive -ArgumentList "/S /D=$scratch" -Wait -PassThru
                    if ($process.ExitCode -ne 0) { throw "$($dep.name): installer exited with $($process.ExitCode)." }
                }

                foreach ($wanted in $dep.files) {
                    $found = Get-ChildItem $scratch -Recurse -Filter (Split-Path $wanted -Leaf) | Select-Object -First 1
                    if (-not $found) { throw "$($dep.name): '$wanted' not found after extraction." }
                    $destination = Join-Path $target $wanted
                    New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
                    Copy-Item $found.FullName $destination -Force
                    Write-Host "  extracted $wanted"
                }

                Remove-Item $scratch -Recurse -Force -ErrorAction SilentlyContinue
            }

            default { throw "$($dep.name): unknown kind '$($dep.kind)'." }
        }

        if ($dep.licenseUrl) {
            Invoke-WebRequest -Uri $dep.licenseUrl -OutFile (Join-Path $target 'LICENSE.txt') -UseBasicParsing
        }

        Set-Content -Path (Join-Path $target 'VERSION.txt') -Value "$($dep.name) $($dep.version)`n$($dep.url)`nsha256 $($dep.sha256)"
        Write-Host "$($dep.name): ready in $target"
    }
    finally {
        Remove-Item $archive -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`nDone. Build the app and the tools are copied next to it under tools\."
